using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AutoMapper;
using MedMateAI.Application.DTOs.Common;
using MedMateAI.Application.DTOs.MedicalFacilities.Responses;
using MedMateAI.Application.DTOs.SymptomAnalysis.Requests;
using MedMateAI.Application.DTOs.SymptomAnalysis.Responses.Session;
using MedMateAI.Application.DTOs.SymptomAnalysis.Responses.ClinicalQuestions;
using MedMateAI.Application.DTOs.SymptomAnalysis.Responses.MedGemma;
using MedMateAI.Application.DTOs.SymptomAnalysis.Responses.Quota;
using MedMateAI.Application.Common.Time;
using MedMateAI.Application.IService;
using MedMateAI.Application.Models.ServiceCredits;
using MedMateAI.Domain.Common;
using MedMateAI.Domain.Entities;
using MedMateAI.Domain.Enums;
using MedMateAI.Domain.Persistence;
using Microsoft.Extensions.Logging;

namespace MedMateAI.Application.Service;

public sealed class SymptomAnalysisService : ISymptomAnalysisService
{
    private const int MaxMessageLength = 2000;
    private const int MaxFreeSubmissionsPerDay = 5;

    private const string UnsupportedSymptomMessage =
        "Không xác định được triệu chứng trong các khoa đang hỗ trợ (hô hấp, cơ xương khớp, truyền nhiễm siêu vi). Vui lòng mô tả rõ hơn.";

    private static readonly string FreeDailyQuotaExceededMessage =
        $"Bạn đã dùng hết lượt trong gói cước và đạt giới hạn tối đa {MaxFreeSubmissionsPerDay} lượt phân tích miễn phí trong ngày hôm nay. Vui lòng quay lại vào ngày mai hoặc mua thêm gói cước.";

    private static readonly JsonSerializerOptions DiagnosisJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserService _userService;
    private readonly ITranslationService _translationService;
    private readonly IMedGemmaChatService _medGemmaChatService;
    private readonly IIcdLookupService _icdLookupService;
    private readonly ISymptomAnalysisQuotaService _quotaService;
    private readonly IMapper _mapper;
    private readonly ILogger<SymptomAnalysisService> _logger;

    public SymptomAnalysisService(
        IUnitOfWork unitOfWork,
        IUserService userService,
        ITranslationService translationService,
        IMedGemmaChatService medGemmaChatService,
        IIcdLookupService icdLookupService,
        ISymptomAnalysisQuotaService quotaService,
        IMapper mapper,
        ILogger<SymptomAnalysisService> logger)
    {
        _unitOfWork = unitOfWork;
        _userService = userService;
        _translationService = translationService;
        _medGemmaChatService = medGemmaChatService;
        _icdLookupService = icdLookupService;
        _quotaService = quotaService;
        _mapper = mapper;
        _logger = logger;
    }

    // 
    public async Task<SymptomAnalysisResponse?> GetSessionByIdAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        if (sessionId == Guid.Empty)
        {
            return null;
        }

        var currentUser = await _userService.GetCurrentUserAsync(cancellationToken);
        if (currentUser is null)
        {
            return null;
        }

        var session = await _unitOfWork.SymptomAnalysisSessions.GetByIdAsync(sessionId, cancellationToken);
        if (session is null || session.IsDeleted)
        {
            return null;
        }

        var isAdmin = await _userService.IsInRoleAsync(currentUser.Id, "Admin", cancellationToken);
        if (!isAdmin && session.UserId != currentUser.Id)
        {
            return null;
        }

        return await BuildSessionResponseAsync(session, cancellationToken);
    }

    // 
    public async Task<PagedResponse<SymptomAnalysisSessionSummaryResponse>> GetSessionsByUserIdAsync(
        Guid userId,
        SymptomAnalysisSessionType? sessionType,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            return new PagedResponse<SymptomAnalysisSessionSummaryResponse>();
        }

        var paged = await _unitOfWork.SymptomAnalysisSessions.GetPagedByUserIdAsync(
            userId,
            sessionType,
            pageNumber,
            pageSize,
            cancellationToken);

        return PagedResponse<SymptomAnalysisSessionSummaryResponse>.From(paged, MapToSessionSummary);
    }

    public async Task<PagedResponse<SymptomAnalysisSessionSummaryResponse>> GetAllSessionsAsync(
        SymptomAnalysisSessionType? sessionType,
        SymptomAnalysisSessionStatus? status,
        Guid? userId,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var paged = await _unitOfWork.SymptomAnalysisSessions.GetPagedAllAsync(
            sessionType,
            status,
            userId,
            pageNumber,
            pageSize,
            cancellationToken);

        return PagedResponse<SymptomAnalysisSessionSummaryResponse>.From(paged, MapToSessionSummary);
    }

    public async Task<SymptomAnalysisQuotaResponse?> GetQuotaAsync(
        CancellationToken cancellationToken = default)
    {
        var currentUser = await _userService.GetCurrentUserAsync(cancellationToken);
        if (currentUser is null)
        {
            return null;
        }

        var utcNow = DateTime.UtcNow;
        var businessDate = VietnamBusinessDate.GetToday(utcNow);
        var usedToday = await CountFreeCompletedTodayAsync(currentUser.Id, businessDate, cancellationToken);
        var remainingToday = Math.Max(0, MaxFreeSubmissionsPerDay - usedToday);

        var usages = await _unitOfWork.QuotaUsages.GetEligibleByUserAsync(
            currentUser.Id,
            IServiceCreditService.QuotaCode,
            utcNow,
            cancellationToken);

        var hasServiceCredit = usages.Any(usage =>
            usage.UsedCount + usage.ReservedCount < usage.LimitValue);

        return new SymptomAnalysisQuotaResponse
        {
            BusinessDate = businessDate,
            LimitPerDay = MaxFreeSubmissionsPerDay,
            UsedToday = usedToday,
            RemainingToday = remainingToday,
            HasServiceCredit = hasServiceCredit,
            IsFreeTier = !hasServiceCredit,
        };
    }

    // 
    public async Task<SuggestClinicalQuestionsResponse> SuggestClinicalQuestionAsync(
     SuggestClinicalQuestionRequest request,
     CancellationToken cancellationToken = default)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.UserInput))
        {
            throw new ArgumentException("Nội dung triệu chứng là bắt buộc");
        }

        var trimmedInput = request.UserInput.Trim();

        if (trimmedInput.Length > MaxMessageLength)
        {
            throw new ArgumentException($"Nội dung triệu chứng không được vượt quá {MaxMessageLength} ký tự");
        }

        var normalizedInput = NormalizeMatchingText(trimmedInput);
        if (string.IsNullOrEmpty(normalizedInput))
        {
            throw new ArgumentException(UnsupportedSymptomMessage);
        }

        var inputWords = normalizedInput
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ToList();

        var activeChapters = await _unitOfWork.IcdChapters.GetActiveChaptersAsync(cancellationToken);
        var chapterMatches = MatchChaptersByKeywords(activeChapters, inputWords);
        if (chapterMatches.Count == 0)
        {
            throw new ArgumentException(UnsupportedSymptomMessage);
        }

        var topChapter = chapterMatches
            .OrderByDescending(x => x.Value.TotalScore)
            .ThenBy(x => x.Value.ChapterCode)
            .First();

        var matchedQuestions = await _unitOfWork.ClinicalQuestions
            .GetQuestionsByChapterIdsAsync(new List<Guid> { topChapter.Key }, cancellationToken);

        var currentUser = await _userService.GetCurrentUserAsync(cancellationToken);
        var session = new SymptomAnalysisSession
        {
            Id = Guid.NewGuid(),
            UserId = currentUser?.Id,
            InputText = trimmedInput,
            Status = SymptomAnalysisSessionStatus.Processing,
            DisclaimerShown = true,
            CreatedAt = DateTime.UtcNow,
        };

        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        try
        {
            if (currentUser is not null)
            {
                await ApplyQuotaForNewSessionAsync(session, currentUser.Id, cancellationToken);
            }

            _unitOfWork.SymptomAnalysisSessions.Add(session);

            var results = new List<SuggestedClinicalQuestionResponse>(matchedQuestions.Count);
            foreach (var question in matchedQuestions)
            {
                if (question.ChapterId is null
                    || !chapterMatches.TryGetValue(question.ChapterId.Value, out var chapterMatch))
                {
                    continue;
                }

                var questionAnswers = ResolveQuestionAnswers(question);
                var defaultAnswerValues = CreateDefaultAnswerValues(questionAnswers);

                results.Add(new SuggestedClinicalQuestionResponse
                {
                    QuestionId = question.Id,
                    QuestionVi = question.QuestionVi,
                    ChapterId = question.ChapterId,
                    ChapterCode = question.ChapterCode ?? chapterMatch.ChapterCode,
                    TotalScore = chapterMatch.TotalScore,
                    MatchedKeywords = chapterMatch.MatchedKeywords,
                    Answers = questionAnswers,
                });

                _unitOfWork.SessionClinicalQuestionAnswers.Add(new SessionClinicalQuestionAnswer
                {
                    Id = Guid.NewGuid(),
                    SymptomAnalysisSessionId = session.Id,
                    ClinicalQuestionId = question.Id,
                    AnswerValues = defaultAnswerValues,
                    CreatedAt = DateTime.UtcNow,
                });
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            var orderedResults = results
                .OrderByDescending(result => result.TotalScore)
                .ThenBy(result => result.ChapterCode)
                .ThenBy(result => result.QuestionVi)
                .ToList();

            return new SuggestClinicalQuestionsResponse
            {
                SessionId = session.Id,
                Questions = orderedResults,
            };
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            throw;
        }
    }

    // 
    public async Task<ClinicalQuestionAnswersResponse> SubmitClinicalQuestionAnswersAsync(
        SubmitClinicalQuestionAnswersRequest request,
        CancellationToken cancellationToken = default)
    {
        var prepared = await PrepareClinicalQuestionSubmissionAsync(request, cancellationToken);

        try
        {
            var analysis = await ExecuteMedGemmaAnalysisAsync(
                prepared.Session,
                prepared.BayesianPrompt,
                cancellationToken);

            return new ClinicalQuestionAnswersResponse
            {
                SessionId = prepared.Session.Id,
                UserInput = prepared.Session.InputText,
                Answers = _mapper.Map<List<ClinicalQuestionAnswerResult>>(prepared.Answers),
                MedGemmaPrompt = prepared.BayesianPrompt,
                Analysis = analysis,
            };
        }
        finally
        {
            if (prepared.Session.UserSubscriptionId.HasValue)
            {
                await _quotaService.FinalizeAsync(prepared.Session.Id, cancellationToken);
            }
        }
    }

    // private method cho SubmitClinicalQuestionAnswersAsync.

    private sealed record PreparedClinicalSubmission(SymptomAnalysisSession Session,
    IReadOnlyList<SessionClinicalQuestionAnswer> Answers,
    string BayesianPrompt);

    private async Task<PreparedClinicalSubmission> PrepareClinicalQuestionSubmissionAsync(
        SubmitClinicalQuestionAnswersRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null)
            throw new ArgumentException("Request body là bắt buộc");

        if (request.SessionId == Guid.Empty)
            throw new ArgumentException("Id phiên phân tích triệu chứng là bắt buộc");

        var session = await _unitOfWork.SymptomAnalysisSessions.GetByIdAsync(request.SessionId, cancellationToken);
        if (session is null || session.IsDeleted)
            throw new ArgumentException("Không tìm thấy phiên phân tích triệu chứng");

        if (session.UserId.HasValue && !session.UserSubscriptionId.HasValue)
        {
            await EnsureFreeDailyQuotaAvailableAsync(session.UserId.Value, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(session.InputText))
            throw new ArgumentException("Nội dung triệu chứng của phiên không tồn tại");

        var existingAnswers = await _unitOfWork.SessionClinicalQuestionAnswers
            .GetTrackedBySessionIdAsync(session.Id, cancellationToken);

        if (existingAnswers.Count == 0)
            throw new ArgumentException("Không tìm thấy câu hỏi lâm sàng cho phiên này");

        var submittedByQuestionId = (request.Answers ?? [])
            .Where(a => a.QuestionId != Guid.Empty)
            .GroupBy(a => a.QuestionId)
            .ToDictionary(g => g.Key, g => g.Last().Answers);

        foreach (var existingAnswer in existingAnswers)
        {
            var question = existingAnswer.ClinicalQuestion
                ?? throw new InvalidOperationException("ClinicalQuestion is required.");

            var validOptions = ResolveQuestionAnswers(question);
            submittedByQuestionId.TryGetValue(existingAnswer.ClinicalQuestionId, out var submitted);

            existingAnswer.AnswerValues = MergeSubmittedAnswerValues(validOptions, submitted);
            existingAnswer.UpdatedAt = DateTime.UtcNow;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var bayesianPrompt = await BuildMedGemmaBayesianPromptAsync(
            session.InputText,
            existingAnswers,
            cancellationToken);

        return new PreparedClinicalSubmission(session, existingAnswers, bayesianPrompt);
    }

    private async Task ApplyQuotaForNewSessionAsync(
        SymptomAnalysisSession session,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var quotaUtcNow = DateTime.UtcNow;
        var reserveResult = await _quotaService.ReserveAsync(
            userId,
            session.Id,
            userId,
            quotaUtcNow,
            cancellationToken);

        if (reserveResult.Success && reserveResult.Data is not null)
        {
            session.UserSubscriptionId = reserveResult.Data.UserSubscriptionId;
            session.UserSubscriptionUsageId = reserveResult.Data.Id;
            return;
        }

        if (reserveResult.Error is ServiceCreditErrorCode.NoCreditPackage
            or ServiceCreditErrorCode.ServiceCreditExhausted)
        {
            await EnsureFreeDailyQuotaAvailableAsync(userId, cancellationToken);
            return;
        }

        throw new InvalidOperationException(
            $"Không thể kiểm tra giới hạn lượt dùng: {reserveResult.Error}");
    }

    private async Task EnsureFreeDailyQuotaAvailableAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var today = VietnamBusinessDate.GetToday(DateTimeOffset.UtcNow);
        var todayFreeCount = await CountFreeCompletedTodayAsync(userId, today, cancellationToken);

        if (todayFreeCount >= MaxFreeSubmissionsPerDay)
        {
            throw new InvalidOperationException(FreeDailyQuotaExceededMessage);
        }
    }

    private async Task<int> CountFreeCompletedTodayAsync(
        Guid userId,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        var completedFreeSessions = await _unitOfWork.SymptomAnalysisSessions.GetAllAsync(
            s => s.UserId == userId
                 && s.Status == SymptomAnalysisSessionStatus.Completed
                 && s.CompletedAt.HasValue
                 && s.UserSubscriptionId == null
                 && !s.IsDeleted,
            cancellationToken: cancellationToken);

        return completedFreeSessions.Count(s =>
            VietnamBusinessDate.GetToday(new DateTimeOffset(DateTime.SpecifyKind(s.CompletedAt!.Value, DateTimeKind.Utc)))
            == today);
    }

    private static Dictionary<Guid, (int TotalScore, List<string> MatchedKeywords, string ChapterCode)> MatchChaptersByKeywords(
        IReadOnlyList<IcdChapter> activeChapters,
        IReadOnlyList<string> inputWords)
    {
        var chapterMatches = new Dictionary<Guid, (int TotalScore, List<string> MatchedKeywords, string ChapterCode)>();

        foreach (var chapter in activeChapters)
        {
            if (chapter.KeywordWeights is null || chapter.KeywordWeights.Count == 0)
            {
                continue;
            }

            var (totalScore, matchedKeywords) = ScoreChapterKeywords(chapter.KeywordWeights, inputWords);
            if (totalScore > 0)
            {
                chapterMatches[chapter.Id] = (totalScore, matchedKeywords, chapter.ChapterCode);
            }
        }

        return chapterMatches;
    }

    private static (int TotalScore, List<string> MatchedKeywords) ScoreChapterKeywords(
        IReadOnlyDictionary<string, int> keywordWeights,
        IReadOnlyList<string> inputWords)
    {
        var totalScore = 0;
        var matchedKeywords = new List<string>();

        foreach (var (keyword, weight) in keywordWeights)
        {
            if (!IsKeywordMatched(keyword, inputWords))
            {
                continue;
            }

            matchedKeywords.Add(keyword);
            totalScore += weight;
        }

        return (totalScore, matchedKeywords);
    }

    private static bool IsKeywordMatched(string keyword, IReadOnlyList<string> inputWords)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return false;
        }

        var normalizedKeyword = NormalizeMatchingText(keyword);
        if (normalizedKeyword is null)
        {
            return false;
        }

        var keywordTokens = normalizedKeyword.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return MatchesKeywordTokens(inputWords, keywordTokens);
    }

    private static bool MatchesKeywordTokens(IReadOnlyList<string> inputWords, string[] keywordTokens)
    {
        return keywordTokens.Length switch
        {
            1 => inputWords.Contains(keywordTokens[0]),
            2 => Check2WordDistanceByArray(
                inputWords,
                keywordTokens[0],
                keywordTokens[1],
                maxDistance: 2),
            3 => Check3WordDistanceByArray(
                inputWords,
                keywordTokens[0],
                keywordTokens[1],
                keywordTokens[2],
                maxDistance: 2),
            _ => false,
        };
    }

    //
    private async Task<SymptomAnalysisAnalyzeResponse> ExecuteMedGemmaAnalysisAsync(
        SymptomAnalysisSession session,
        string bayesianPrompt,
        CancellationToken cancellationToken)
    {
        var coreResult = await RunMedGemmaAnalysisCoreAsync(session, bayesianPrompt, cancellationToken);

        var vietnameseDiagnoses = await TranslateDiagnosesToVietnameseAsync(
            coreResult.Diagnoses,
            cancellationToken);

        await ReplaceSessionSymptomsAsync(session.Id, vietnameseDiagnoses, cancellationToken);

        var primaryDiagnosis = SelectPrimaryDiagnosisByPluralityChapter(vietnameseDiagnoses);

        var chapterCode = ExtractIcdChapterCode(primaryDiagnosis?.Icd10Code);

        var (recommendedDepartment, recommendedFacilities) =
            await ResolveDepartmentAndFacilitiesAsync(
                primaryDiagnosis,
                chapterCode,
                cancellationToken);

        if (recommendedDepartment is not null)
        {
            await SaveDepartmentRecommendationAsync(session.Id, recommendedDepartment, cancellationToken);
        }

        session.SessionType = SymptomAnalysisSessionType.Department;
        session.Status = SymptomAnalysisSessionStatus.Completed;
        session.CompletedAt = DateTime.UtcNow;
        session.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new SymptomAnalysisAnalyzeResponse
        {
            SessionId = session.Id,
            Status = session.Status,
            Model = coreResult.Model,
            Diagnoses = vietnameseDiagnoses,
            PrimaryDiagnosis = primaryDiagnosis,
            RecommendedDepartment = recommendedDepartment,
            RecommendedFacilities = recommendedFacilities,
        };
    }

    // private method cho ExecuteMedGemmaAnalysisAsync
    private sealed record MedGemmaAnalysisCoreResult(IReadOnlyList<BayesianDiagnosisResponse> Diagnoses,
    string? Model);
    private async Task<MedGemmaAnalysisCoreResult> RunMedGemmaAnalysisCoreAsync(
    SymptomAnalysisSession session,
    string bayesianPrompt,
    CancellationToken cancellationToken)
    {
        MedGemmaChatResult aiResult;

        try
        {
            aiResult = await _medGemmaChatService.GenerateAsync(bayesianPrompt, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "MedGemma analysis failed for session {SessionId}; category {ErrorCategory}.",
                session.Id,
                ex.GetType().Name);
            session.Status = SymptomAnalysisSessionStatus.Failed;
            session.UpdatedAt = DateTime.UtcNow;
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            throw;
        }

        if (!TryParseDiagnosesJson(aiResult.Content, out var parsedDiagnoses))
        {
            _logger.LogWarning(
                "MedGemma diagnoses JSON parse failed for session {SessionId}.",
                session.Id);
            session.Status = SymptomAnalysisSessionStatus.Failed;
            session.UpdatedAt = DateTime.UtcNow;
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            throw new InvalidOperationException("Không thể phân tích phản hồi JSON từ MedGemma");
        }

        var pB = parsedDiagnoses.Sum(d => d.PA * d.PBGivenA);

        var diagnoses = new List<BayesianDiagnosisResponse>(parsedDiagnoses.Count);

        foreach (var parsed in parsedDiagnoses)

        {
            var lookup = string.IsNullOrWhiteSpace(parsed.SearchKeyword)
            ? null
            : await _icdLookupService.SearchFirstAsync(parsed.SearchKeyword, cancellationToken);
            diagnoses.Add(new BayesianDiagnosisResponse
            {
                DiseaseName = parsed.DiseaseName,
                SearchKeyword = parsed.SearchKeyword,
                Icd10Code = lookup?.Icd10Code ?? string.Empty,
                PA = parsed.PA,
                PBGivenA = parsed.PBGivenA,
                PAGivenB = pB > 0 ? (parsed.PA * parsed.PBGivenA) / pB : 0,
                ClinicalReasoning = parsed.ClinicalReasoning,
            });
        }

        diagnoses = diagnoses
        .OrderByDescending(d => d.PAGivenB)
        .Select((d, index) => { d.Rank = index + 1; return d; })
        .ToList();

        return new MedGemmaAnalysisCoreResult(diagnoses, aiResult.Model);
    }

    private static SymptomAnalysisSessionSummaryResponse MapToSessionSummary(SymptomAnalysisSession session)
    {
        return new SymptomAnalysisSessionSummaryResponse
        {
            UserId = session.UserId,
            SessionId = session.Id,
            InputText = session.InputText,
            SeverityLevel = session.SeverityLevel,
            Status = session.Status,
            SessionType = session.SessionType,
            CreatedAt = session.CreatedAt,
        };
    }

    private static BayesianDiagnosisResponse? SelectPrimaryDiagnosisByPluralityChapter(
    IReadOnlyList<BayesianDiagnosisResponse> diagnoses)
    {
        if (diagnoses.Count == 0)
        {
            return null;
        }
        var chapterGroups = diagnoses
            .Select(d => new { Diagnosis = d, Chapter = ExtractIcdChapterCode(d.Icd10Code) })
            .Where(x => !string.IsNullOrEmpty(x.Chapter))
            .GroupBy(x => x.Chapter!)
            .OrderByDescending(g => g.Count())
            .ToList();

        if (chapterGroups.Count == 0)
        {
            return diagnoses.MaxBy(d => d.PAGivenB);
        }

        var winningGroup = chapterGroups
        .OrderByDescending(g => g.Count())
        .ThenByDescending(g => g.Max(x => x.Diagnosis.PAGivenB))
        .First();

        return winningGroup
        .Select(x => x.Diagnosis)
        .MaxBy(d => d.PAGivenB);
    }

    //
    private async Task<(RecommendedDepartmentResponse? Department, IReadOnlyList<MedicalFacilityResponse> Facilities)>
        ResolveDepartmentAndFacilitiesAsync(
            BayesianDiagnosisResponse? primaryDiagnosis,
            string? chapterCode,
            CancellationToken cancellationToken)
    {
        if (primaryDiagnosis is null || string.IsNullOrWhiteSpace(chapterCode))
        {
            return (null, Array.Empty<MedicalFacilityResponse>());
        }

        var department = await _unitOfWork.MedicalDepartments.GetActiveByChapterCodeAsync(
            chapterCode,
            cancellationToken);
        if (department is null)
        {
            return (null, Array.Empty<MedicalFacilityResponse>());
        }

        var recommendedDepartment = new RecommendedDepartmentResponse
        {
            DepartmentId = department.Id,
            DepartmentName = department.DepartmentName,
            IcdChapterCode = chapterCode,
            ConfidenceScore = primaryDiagnosis.PAGivenB,
            Reason = primaryDiagnosis.DiseaseName,
            PriorityRank = 1,
            IsEmergencySuggested = false,
        };

        var facilities = await _unitOfWork.MedicalFacilities.GetActiveWithDepartmentsAsync(
            departmentId: department.Id,
            search: null,
            cancellationToken: cancellationToken);

        var facilityResponses = facilities
            .Select(facility => _mapper.Map<MedicalFacilityResponse>(facility))
            .ToList();

        return (recommendedDepartment, facilityResponses);
    }

    //
    private async Task SaveDepartmentRecommendationAsync(
        Guid sessionId,
        RecommendedDepartmentResponse recommendation,
        CancellationToken cancellationToken)
    {
        var existingRecommendations = await _unitOfWork.DepartmentRecommendations.GetPagedAsync(
            1,
            50,
            recommendationEntity => !recommendationEntity.IsDeleted
                && recommendationEntity.SymptomAnalysisSessionId == sessionId,
            query => query.OrderBy(recommendationEntity => recommendationEntity.PriorityRank),
            cancellationToken: cancellationToken);

        foreach (var existingRecommendation in existingRecommendations.Items)
        {
            existingRecommendation.IsDeleted = true;
            existingRecommendation.UpdatedAt = DateTime.UtcNow;
        }

        _unitOfWork.DepartmentRecommendations.Add(new DepartmentRecommendation
        {
            Id = Guid.NewGuid(),
            SymptomAnalysisSessionId = sessionId,
            DepartmentId = recommendation.DepartmentId,
            ConfidenceScore = recommendation.ConfidenceScore,
            Reason = recommendation.Reason,
            PriorityRank = recommendation.PriorityRank,
            IsEmergencySuggested = recommendation.IsEmergencySuggested,
            CreatedAt = DateTime.UtcNow,
        });
    }

    //
    private async Task<string> BuildMedGemmaBayesianPromptAsync(
        string userInput,
        IReadOnlyList<SessionClinicalQuestionAnswer> answers,
        CancellationToken cancellationToken)
    {
        var translatedInput = await _translationService.TranslateToEnglishAsync(
            userInput,
            cancellationToken: cancellationToken);

        var builder = new StringBuilder();

        builder.AppendLine("You are a clinical decision support assistant.");
        builder.AppendLine();
        builder.AppendLine("Return STRICTLY valid JSON only.");
        builder.AppendLine("Do not include markdown.");
        builder.AppendLine("Do not include ```json.");
        builder.AppendLine("Do not include explanation outside JSON.");
        builder.AppendLine();
        builder.AppendLine("Clinical task:");
        builder.AppendLine("1. Identify the most likely differential diagnoses.");
        builder.AppendLine("2. Provide the precise,standard ICD-10 code for each disease.");
        builder.AppendLine("3. Estimate two numeric probabilities for each disease:");
        builder.AppendLine("   - p_A = annual symptomatic incidence in general population (0-1).Common respiratory: 0.05-0.20. Rare: 0.0001-0.001.");
        builder.AppendLine("   - p_B_given_A = probability of this exact symptom pattern given the disease.");
        builder.AppendLine();
        builder.AppendLine("Probability rules:");
        builder.AppendLine("- p_A must be a numeric float between 0 and 1.");
        builder.AppendLine("- p_B_given_A must be a numeric float between 0 and 1.");
        builder.AppendLine("- Do NOT use placeholder values.");
        builder.AppendLine("- Do NOT copy example probabilities.");
        builder.AppendLine("- Do NOT use 0.01 as a default value.");
        builder.AppendLine("- Do NOT assign the same probability to every disease.");
        builder.AppendLine("- Use different probability values for different diseases.");
        builder.AppendLine("- Common diseases should generally have higher p_A than rare diseases.");
        builder.AppendLine("- Diseases that strongly match the patient's presenting symptoms should have higher p_B_given_A.");
        builder.AppendLine("- Diseases contradicted by absent symptoms should have lower p_B_given_A.");
        builder.AppendLine("- Return 6 diagnoses.");
        builder.AppendLine();
        builder.AppendLine("Keyword Extraction Rules for 'search_keyword':");
        builder.AppendLine("- The keyword MUST be optimized for a strict string-matching medical database API.");
        builder.AppendLine("- REMOVE descriptive qualifiers, etiologies, and clinical states such as: 'viral', 'bacterial', 'acute', 'chronic', 'unspecified', 'infection', 'disease', 'infantile'.");
        builder.AppendLine("- Examples of transformation:");
        builder.AppendLine("  * 'Viral Exanthem' -> 'Exanthem'");
        builder.AppendLine("  * 'Rocky Mountain Spotted Fever' -> 'Spotted Fever'");
        builder.AppendLine("  * 'Acute Bronchitis' -> 'Bronchitis'");
        builder.AppendLine("- Keep the keyword to maximum 1 core nouns.");
        builder.AppendLine();

        var positive = new List<string>();

        var negative = new List<string>();
        
        foreach (var sessionAnswer in answers)
        {
            var question = sessionAnswer.ClinicalQuestion;
            if (question is null || question.Answers.Count == 0)
            {
                continue;
            }
            var values = sessionAnswer.AnswerValues ?? new Dictionary<string, bool>();
            foreach (var (vietnameseKey, englishLabel) in question.Answers)
            {
                var en = string.IsNullOrWhiteSpace(englishLabel)
                    ? vietnameseKey
                    : englishLabel.Trim();
                var isYes = values.TryGetValue(vietnameseKey, out var selected) && selected;
                if (isYes)
                {
                    positive.Add(en);
                }
                else
                {
                    negative.Add(en);
                }
              
            }
        }
        if (positive.Count > 0)
        {
            builder.AppendLine($"patient has the following signs: {string.Join(", ", positive)}");
        }

        else if (!string.IsNullOrWhiteSpace(translatedInput))
        {
            builder.AppendLine($"patient has the following signs: {translatedInput}");
        }

        //if (negative.Count > 0)
        //{
        //    builder.AppendLine($"patient does not has the following signs: {string.Join(", ", negative)}");
        //}

        builder.AppendLine();

        builder.AppendLine("Output JSON schema:");
        builder.AppendLine("diagnoses: array of diagnosis objects");
        builder.AppendLine();
        builder.AppendLine("Each diagnosis object must contain:");
        builder.AppendLine("rank: integer");
        builder.AppendLine("disease_name: string");
        builder.AppendLine("search_keyword: string (The cleaned 1 word core noun based on the rules above)");
        builder.AppendLine("p_A: numeric float");
        builder.AppendLine("p_B_given_A: numeric float");
        builder.AppendLine("clinical_reasoning: string");
        builder.AppendLine();
        builder.AppendLine("Return only this JSON object:");
        builder.AppendLine("{");
        builder.AppendLine("  \"diagnoses\": []");
        builder.AppendLine("}");

        return builder.ToString();
    }

    //
    private async Task<IReadOnlyList<BayesianDiagnosisResponse>> TranslateDiagnosesToVietnameseAsync(
        IReadOnlyList<BayesianDiagnosisResponse> diagnoses,
        CancellationToken cancellationToken)
    {
        if (diagnoses.Count == 0)
        {
            return diagnoses;
        }

        var texts = diagnoses
            .SelectMany(diagnosis => new[] { diagnosis.DiseaseName, diagnosis.ClinicalReasoning })
            .ToList();

        var translatedTexts = await _translationService.TranslateBatchToVietnameseAsync(texts, cancellationToken);

        if (translatedTexts.Count != texts.Count)
        {
            return diagnoses;
        }

        var vietnameseDiagnoses = new List<BayesianDiagnosisResponse>(diagnoses.Count);

        for (var i = 0; i < diagnoses.Count; i++)
        {
            var diagnosis = diagnoses[i];

            vietnameseDiagnoses.Add(new BayesianDiagnosisResponse
            {
                Rank = diagnosis.Rank,
                DiseaseName = translatedTexts[i * 2],
                SearchKeyword = diagnosis.SearchKeyword,
                Icd10Code = diagnosis.Icd10Code,
                PA = diagnosis.PA,
                PBGivenA = diagnosis.PBGivenA,
                PAGivenB = diagnosis.PAGivenB,
                ClinicalReasoning = translatedTexts[(i * 2) + 1],
            });
        }

        return vietnameseDiagnoses;
    }

    //
    private async Task ReplaceSessionSymptomsAsync(
        Guid sessionId,
        IReadOnlyList<BayesianDiagnosisResponse> diagnoses,
        CancellationToken cancellationToken)
    {
        var existingSymptoms = await _unitOfWork.SessionSymptoms.GetPagedAsync(
            1,
            100,
            symptom => !symptom.IsDeleted && symptom.SymptomAnalysisSessionId == sessionId,
            query => query.OrderBy(symptom => symptom.SymptomName),
            asNoTracking: false,
            cancellationToken: cancellationToken);

        foreach (var existingSymptom in existingSymptoms.Items)
        {
            existingSymptom.IsDeleted = true;
            existingSymptom.UpdatedAt = DateTime.UtcNow;
        }

        foreach (var diagnosis in diagnoses)
        {
            _unitOfWork.SessionSymptoms.Add(new SessionSymptom
            {
                Id = Guid.NewGuid(),
                SymptomAnalysisSessionId = sessionId,
                SymptomName = diagnosis.DiseaseName,
                Icd10Code = string.IsNullOrWhiteSpace(diagnosis.Icd10Code) ? null : diagnosis.Icd10Code.Trim(),
                ConfidenceScore = diagnosis.PAGivenB,
                ExtractedText = diagnosis.ClinicalReasoning,
                CreatedAt = DateTime.UtcNow,
            });
        }
    }

    //
    private static bool TryParseDiagnosesJson(
        string content,
        out IReadOnlyList<MedGemmaDiagnosisJsonItem> diagnoses)
    {
        diagnoses = Array.Empty<MedGemmaDiagnosisJsonItem>();

        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        var normalizedJson = StripMarkdownCodeFence(content);
        if (string.IsNullOrWhiteSpace(normalizedJson))
        {
            return false;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<MedGemmaDiagnosesJsonResponse>(normalizedJson, DiagnosisJsonOptions);
            if (parsed?.Diagnoses is null || parsed.Diagnoses.Count == 0)
            {
                return false;
            }

            diagnoses = parsed.Diagnoses;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }


    //private method cho SuggestClinicalQuestionAsync
    private static bool Check2WordDistanceByArray(IReadOnlyList<string> words, string w1, string w2, int maxDistance)
    {
        var indicesW1 = Enumerable.Range(0, words.Count).Where(i => words[i] == w1).ToList();
        var indicesW2 = Enumerable.Range(0, words.Count).Where(i => words[i] == w2).ToList();

        foreach (var index1 in indicesW1)
        {
            foreach (var index2 in indicesW2)
            {
                var distance = Math.Abs(index1 - index2) - 1;
                if (distance <= maxDistance)
                {
                    return true;
                }
            }
        }

        return false;
    }

    //
    private static bool Check3WordDistanceByArray(IReadOnlyList<string> words, string w1, string w2, string w3, int maxDistance)
    {
        var indicesW1 = Enumerable.Range(0, words.Count).Where(i => words[i] == w1).ToList();
        var indicesW2 = Enumerable.Range(0, words.Count).Where(i => words[i] == w2).ToList();
        var indicesW3 = Enumerable.Range(0, words.Count).Where(i => words[i] == w3).ToList();
        foreach (var index1 in indicesW1)
        {
            foreach (var index2 in indicesW2)
            {
                foreach (var index3 in indicesW3)
                {
                    var distance1 = Math.Abs(index1 - index2) - 1;
                    var distance2 = Math.Abs(index2 - index3) - 1;

                    if (distance1 <= maxDistance && distance2 <= maxDistance)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    //private method cho GetSessionByIdAsync
    private async Task<SymptomAnalysisResponse> BuildSessionResponseAsync(
        SymptomAnalysisSession session,
        CancellationToken cancellationToken)
    {
        var sessionId = session.Id;

        var symptomsPaged = await _unitOfWork.SessionSymptoms.GetPagedAsync(
            1,
            100,
            s => !s.IsDeleted && s.SymptomAnalysisSessionId == sessionId,
            q => q.OrderBy(s => s.SymptomName),
            cancellationToken: cancellationToken);

        var sessionAnswers = await _unitOfWork.SessionClinicalQuestionAnswers
            .GetBySessionIdAsync(sessionId, cancellationToken);

        var recommendationsPaged = await _unitOfWork.DepartmentRecommendations.GetPagedAsync(
            1,
            50,
            d => !d.IsDeleted && d.SymptomAnalysisSessionId == sessionId,
            q => q.OrderBy(d => d.PriorityRank),
            cancellationToken: cancellationToken);

        var departmentRecommendations = recommendationsPaged.Items;
        var recommendedFacilities = new List<MedicalFacilityResponse>();

        if (departmentRecommendations.Count > 0)
        {
            var departments = await _unitOfWork.MedicalDepartments.GetActiveAsync(cancellationToken);
            var departmentById = departments.ToDictionary(d => d.Id, d => d);

            foreach (var recommendation in departmentRecommendations)
            {
                if (departmentById.TryGetValue(recommendation.DepartmentId, out var department))
                {
                    recommendation.Department = department;
                }
            }

            var primaryRecommendation = departmentRecommendations.OrderBy(d => d.PriorityRank).FirstOrDefault();
            if (primaryRecommendation is not null)
            {
                var facilities = await _unitOfWork.MedicalFacilities.GetActiveWithDepartmentsAsync(
                    departmentId: primaryRecommendation.DepartmentId,
                    search: null,
                    cancellationToken: cancellationToken);

                recommendedFacilities = facilities
                    .Select(facility => _mapper.Map<MedicalFacilityResponse>(facility))
                    .ToList();
            }
        }

        var response = _mapper.Map<SymptomAnalysisResponse>(session);
        response.Symptoms = _mapper.Map<List<SessionSymptomResponse>>(symptomsPaged.Items);
        response.Answers = _mapper.Map<List<ClinicalQuestionAnswerResult>>(sessionAnswers);
        response.RecommendedDepartments =
            _mapper.Map<List<RecommendedDepartmentResponse>>(departmentRecommendations);
        response.RecommendedFacilities = recommendedFacilities;

        return response;
    }

    //helper
    private static string? ExtractIcdChapterCode(string? icd10Code)
   {
    if (string.IsNullOrWhiteSpace(icd10Code))
        return null;

    var first = icd10Code.Trim().ToUpperInvariant()[0];
    return char.IsLetter(first) ? first.ToString() : null;
    }

    //
    private static string StripMarkdownCodeFence(string content)
    {
        var trimmed = content.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        trimmed = trimmed[3..];
        if (trimmed.StartsWith("json", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[4..];
        }

        trimmed = trimmed.TrimStart('\r', '\n', ' ');

        var closingFenceIndex = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        if (closingFenceIndex >= 0)
        {
            trimmed = trimmed[..closingFenceIndex];
        }

        return trimmed.Trim();
    }

    // 
    private static string? NormalizeMatchingText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var normalized = text.ToLowerInvariant();
        normalized = MatchingPunctuationRegex.Replace(normalized, " ");
        normalized = MatchingWhitespaceRegex.Replace(normalized, " ").Trim();

        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
    
    //
    private static readonly Regex MatchingPunctuationRegex =
    new(@"[.,?!;:]", RegexOptions.Compiled, TimeSpan.FromSeconds(1));
     private static readonly Regex MatchingWhitespaceRegex =
    new(@"\s+", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    private static Dictionary<string, string> ResolveQuestionAnswers(ClinicalQuestion question)
    {
        if (question.Answers.Count > 0)
        {
            return question.Answers;
        }

        var englishLabel = string.IsNullOrWhiteSpace(question.EnglishPrefix)
            ? "symptom"
            : question.EnglishPrefix.Trim();

        return new Dictionary<string, string> { ["có"] = englishLabel };
    }

    private static Dictionary<string, bool> CreateDefaultAnswerValues(Dictionary<string, string> questionAnswers)
    {
        return questionAnswers.Keys.ToDictionary(key => key, _ => false);
    }

    private static Dictionary<string, bool> MergeSubmittedAnswerValues(
        Dictionary<string, string> validOptions,
        Dictionary<string, bool>? submitted)
    {
        var merged = CreateDefaultAnswerValues(validOptions);

        if (submitted is null)
        {
            return merged;
        }

        foreach (var (key, value) in submitted)
        {
            if (validOptions.ContainsKey(key))
            {
                merged[key] = value;
            }
        }

        return merged;
    }
}

