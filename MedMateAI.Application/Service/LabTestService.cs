using System.Globalization;
using System.Text;
using MedMateAI.Application.DTOs.Common;
using MedMateAI.Application.DTOs.LabIndicators.Responses;
using MedMateAI.Application.DTOs.LabTests.Requests;
using MedMateAI.Application.DTOs.LabTests.Responses;
using MedMateAI.Application.DTOs.WebChatbot.Requests;
using MedMateAI.Application.Helpers;
using MedMateAI.Application.IService;
using MedMateAI.Application.Models.ServiceCredits;
using MedMateAI.Domain.Common;
using MedMateAI.Domain.Entities;
using MedMateAI.Domain.Enums;
using MedMateAI.Domain.Persistence;
using Microsoft.Extensions.Logging;

namespace MedMateAI.Application.Service;

public sealed class LabTestService : ILabTestService
{
    private const string LabTestSummaryTaskType = "LabTestSummary";

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
        ".png",
        ".bmp",
        ".tif",
        ".tiff",
        ".pdf",
    };

    private readonly IUnitOfWork _unitOfWork;
    private readonly ILabTestJobScheduler _jobScheduler;
    private readonly ILabTestResultAnalyzer _resultAnalyzer;
    private readonly ILabTestQuotaService _quotaService;
    private readonly IAIConfigService _aiConfigService;
    private readonly IAIChatProvider _aiChatProvider;
    private readonly ILogger<LabTestService> _logger;

    public LabTestService(
        IUnitOfWork unitOfWork,
        ILabTestJobScheduler jobScheduler,
        ILabTestResultAnalyzer resultAnalyzer,
        ILabTestQuotaService quotaService,
        IAIConfigService aiConfigService,
        IAIChatProvider aiChatProvider,
        ILogger<LabTestService> logger)
    {
        _unitOfWork = unitOfWork;
        _jobScheduler = jobScheduler;
        _resultAnalyzer = resultAnalyzer;
        _quotaService = quotaService;
        _aiConfigService = aiConfigService;
        _aiChatProvider = aiChatProvider;
        _logger = logger;
    }

    public async Task<(bool Succeeded, IEnumerable<string> Errors, LabTestUploadResponse? Data)> AnalyzeFromDocumentUrlAsync(
        Guid userId,
        LabTestAnalyzeRequest request,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            return (false, new[] { "Id người dùng là bắt buộc" }, null);
        }

        if (request is null)
        {
            return (false, new[] { "Request body là bắt buộc" }, null);
        }

        var documentUrl = request.DocumentUrl?.Trim() ?? string.Empty;
        var validationErrors = ValidateDocumentUrl(documentUrl);
        if (validationErrors.Count > 0)
        {
            return (false, validationErrors, null);
        }

        if (request.PatientAgeAtTest is < 0 or > 150)
        {
            return (false, new[] { "PatientAgeAtTest không hợp lệ" }, null);
        }

        var sessionId = Guid.NewGuid();
        var session = new LabTestSession
        {
            Id = sessionId,
            UserId = userId,
            DocumentUrl = documentUrl,
            Status = LabTestSessionStatus.Processing,
            PatientGenderAtTest = request.PatientGenderAtTest,
            PatientAgeAtTest = request.PatientAgeAtTest,
            CreatedAt = DateTime.UtcNow,
        };

        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        try
        {
            var quotaUtcNow = DateTime.UtcNow;
            var reserveResult = await _quotaService.ReserveAsync(
                userId,
                sessionId,
                userId,
                quotaUtcNow,
                cancellationToken);

            if (!reserveResult.Success || reserveResult.Data is null)
            {
                await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
                return (false, new[] { reserveResult.Error.ToStableCode() }, null);
            }

            session.UserSubscriptionId = reserveResult.Data.UserSubscriptionId;
            session.UserSubscriptionUsageId = reserveResult.Data.Id;

            _unitOfWork.LabTestSessions.Add(session);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            throw;
        }

        _jobScheduler.EnqueueOcr(sessionId);

        return (true, Array.Empty<string>(), MapToResponse(session));
    }

    public async Task<LabTestUploadResponse?> GetSessionAsync(
        Guid userId,
        Guid sessionId,
        bool isAdmin = false,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty || sessionId == Guid.Empty)
        {
            return null;
        }

        var session = await _unitOfWork.LabTestSessionDetails.GetByIdWithResultsAsync(sessionId, cancellationToken);
        if (session is null || !await CanViewSessionAsync(session, userId, isAdmin, cancellationToken))
        {
            return null;
        }

        if ((session.Status == LabTestSessionStatus.Completed)
            && session.LabTestResultDetails.Count == 0
            && !string.IsNullOrWhiteSpace(session.RawOcrText))
        {
            await _resultAnalyzer.AnalyzeAndPersistAsync(sessionId, cancellationToken);
            session = await _unitOfWork.LabTestSessionDetails.GetByIdWithResultsAsync(sessionId, cancellationToken);
            if (session is null)
            {
                return null;
            }
        }

        return MapToResponse(session);
    }

    private async Task<bool> CanViewSessionAsync(
        LabTestSession session,
        Guid userId,
        bool isAdmin,
        CancellationToken cancellationToken)
    {
        if (session.UserId == userId || isAdmin)
        {
            return true;
        }

        var doctor = await _unitOfWork.RecoveryPlanRequests.GetDoctorByUserIdAsync(userId, cancellationToken);
        if (doctor is null || !doctor.IsActive)
        {
            return false;
        }

        return await _unitOfWork.RecoveryPlanRequests.IsLabSessionAssignedToDoctorAsync(
            session.Id,
            doctor.Id,
            cancellationToken);
    }

    public async Task<PagedResponse<LabTestSessionSummaryResponse>> GetSessionsByUserIdAsync(
        Guid userId,
        LabTestSessionStatus? status,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            return new PagedResponse<LabTestSessionSummaryResponse>();
        }

        var paged = await _unitOfWork.LabTestSessionDetails.GetPagedByUserIdAsync(
            userId,
            status,
            pageNumber,
            pageSize,
            cancellationToken);

        return PagedResponse<LabTestSessionSummaryResponse>.From(paged, MapToSessionSummary);
    }

    public async Task<PagedResponse<LabTestSessionSummaryResponse>> GetAllSessionsAsync(
        LabTestSessionStatus? status,
        Guid? userId,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var paged = await _unitOfWork.LabTestSessionDetails.GetPagedAllAsync(
            status,
            userId,
            pageNumber,
            pageSize,
            cancellationToken);

        return PagedResponse<LabTestSessionSummaryResponse>.From(paged, MapToSessionSummary);
    }

    public async Task<IReadOnlyList<LabTestOcrExtractResponse>?> GetOcrExtractsBySessionIdAsync(
        Guid userId,
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty || sessionId == Guid.Empty)
        {
            return null;
        }

        var session = await _unitOfWork.LabTestSessions.GetByIdAsync(sessionId, cancellationToken);
        if (session is null || session.IsDeleted || session.UserId != userId)
        {
            return null;
        }

        var extracts = await _unitOfWork.LabTestOcrExtracts.GetAllAsync(
            x => !x.IsDeleted && x.TestSessionId == sessionId,
            query => query.OrderBy(x => x.RowIndex).ThenBy(x => x.CreatedAt),
            cancellationToken);

        return extracts
            .Select(x => new LabTestOcrExtractResponse
            {
                OcrExtractId = x.Id,
                TestSessionId = x.TestSessionId,
                RowIndex = x.RowIndex,
                ExtractedTestName = x.ExtractedTestName,
                ExtractedValue = x.ExtractedValue,
                ExtractedUnit = x.ExtractedUnit,
                ExtractedReferenceText = x.ExtractedReferenceText,
                CreatedAt = x.CreatedAt,
            })
            .ToList();
    }

    public async Task<(bool Succeeded, IEnumerable<string> Errors, LabTestSummaryResponse? Data)> SummarizeSessionAsync(
        Guid userId,
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            return (false, new[] { "Id người dùng là bắt buộc" }, null);
        }

        if (sessionId == Guid.Empty)
        {
            return (false, new[] { "Id phiên xét nghiệm không hợp lệ" }, null);
        }

        var session = await _unitOfWork.LabTestSessionDetails.GetByIdWithResultsAsync(sessionId, cancellationToken);
        if (session is null || session.UserId != userId)
        {
            return (false, new[] { "Không tìm thấy phiên xét nghiệm" }, null);
        }

        if (session.Status != LabTestSessionStatus.Completed || session.LabTestResultDetails.Count == 0)
        {
            return (false, new[] { "Phiên xét nghiệm chưa hoàn tất hoặc không có kết quả để tóm tắt" }, null);
        }

        if (!string.IsNullOrWhiteSpace(session.AiSummary))
        {
            return (true, Array.Empty<string>(), BuildSummaryResponse(sessionId, LabTestSummaryStatus.Completed, session.AiSummary));
        }

        if (session.AiSummaryStatus == LabTestSummaryStatus.Processing)
        {
            return (true, Array.Empty<string>(), BuildSummaryResponse(sessionId, LabTestSummaryStatus.Processing, null));
        }

        var trackedSession = await _unitOfWork.LabTestSessions.GetByIdAsync(sessionId, cancellationToken);
        if (trackedSession is null || trackedSession.IsDeleted || trackedSession.UserId != userId)
        {
            return (false, new[] { "Không tìm thấy phiên xét nghiệm" }, null);
        }

        trackedSession.AiSummaryStatus = LabTestSummaryStatus.Processing;
        trackedSession.UpdatedAt = DateTime.UtcNow;
        _unitOfWork.LabTestSessions.Update(trackedSession);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _jobScheduler.EnqueueSummary(sessionId);

        return (true, Array.Empty<string>(), BuildSummaryResponse(sessionId, LabTestSummaryStatus.Processing, null));
    }

    public async Task ProcessSummaryAsync(
        Guid sessionId,
        bool isFinalAttempt,
        CancellationToken cancellationToken = default)
    {
        if (sessionId == Guid.Empty)
        {
            return;
        }

        var session = await _unitOfWork.LabTestSessionDetails.GetByIdWithResultsAsync(sessionId, cancellationToken);
        if (session is null || session.AiSummaryStatus != LabTestSummaryStatus.Processing)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(session.AiSummary))
        {
            await UpdateSummaryStateAsync(sessionId, LabTestSummaryStatus.Completed, session.AiSummary, cancellationToken);
            return;
        }

        if (session.Status != LabTestSessionStatus.Completed || session.LabTestResultDetails.Count == 0)
        {
            await UpdateSummaryStateAsync(sessionId, LabTestSummaryStatus.Failed, null, cancellationToken);
            return;
        }

        try
        {
            var aiConfig = await _aiConfigService.GetActiveAIConfigByTaskTypeAsync(
                LabTestSummaryTaskType,
                cancellationToken);

            if (aiConfig is null || string.IsNullOrWhiteSpace(aiConfig.SystemPrompt))
            {
                _logger.LogError(
                    "Lab test summary failed for session {SessionId}: missing AI config for task type {TaskType}.",
                    sessionId,
                    LabTestSummaryTaskType);
                await UpdateSummaryStateAsync(sessionId, LabTestSummaryStatus.Failed, null, cancellationToken);
                return;
            }

            var aiResult = await _aiChatProvider.GenerateAsync(
                new AIProviderChatRequest
                {
                    SystemPrompt = aiConfig.SystemPrompt,
                    UserMessage = BuildSummaryUserMessage(session),
                    Model = aiConfig.Model ?? string.Empty,
                    Temperature = aiConfig.Temperature,
                    MaxTokens = aiConfig.MaxTokens,
                },
                cancellationToken);

            var summary = aiResult.Content?.Trim();
            if (string.IsNullOrWhiteSpace(summary))
            {
                _logger.LogWarning("Lab test summary returned empty content for session {SessionId}.", sessionId);
                await UpdateSummaryStateAsync(sessionId, LabTestSummaryStatus.Failed, null, cancellationToken);
                return;
            }

            await UpdateSummaryStateAsync(sessionId, LabTestSummaryStatus.Completed, summary, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (BackgroundJobRetry.IsRetryable(ex))
        {
            _logger.LogWarning(
                ex,
                "Lab test summary hit a transient failure for session {SessionId}. FinalAttempt={IsFinalAttempt}.",
                sessionId,
                isFinalAttempt);

            if (!isFinalAttempt)
            {
                throw;
            }

            await UpdateSummaryStateAsync(sessionId, LabTestSummaryStatus.Failed, null, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lab test summary failed for session {SessionId}.", sessionId);
            await UpdateSummaryStateAsync(sessionId, LabTestSummaryStatus.Failed, null, cancellationToken);
        }
    }

    private async Task UpdateSummaryStateAsync(
        Guid sessionId,
        LabTestSummaryStatus status,
        string? summary,
        CancellationToken cancellationToken)
    {
        var trackedSession = await _unitOfWork.LabTestSessions.GetByIdAsync(sessionId, cancellationToken);
        if (trackedSession is null)
        {
            return;
        }

        trackedSession.AiSummaryStatus = status;
        if (summary is not null)
        {
            trackedSession.AiSummary = summary;
        }

        trackedSession.UpdatedAt = DateTime.UtcNow;
        _unitOfWork.LabTestSessions.Update(trackedSession);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static LabTestSummaryResponse BuildSummaryResponse(
        Guid sessionId,
        LabTestSummaryStatus status,
        string? summary) =>
        new()
        {
            SessionId = sessionId,
            Status = status,
            AiSummary = summary,
        };

    private static string BuildSummaryUserMessage(LabTestSession session)
    {
        var genderText = session.PatientGenderAtTest switch
        {
            Gender.Male => "Nam",
            Gender.Female => "Nữ",
            _ => "Chưa xác định",
        };

        var ageText = session.PatientAgeAtTest?.ToString(CultureInfo.InvariantCulture) ?? "Chưa cập nhật";
        var builder = new StringBuilder();
        builder.AppendLine($"Thông tin bệnh nhân: Giới tính: {genderText}, Tuổi: {ageText}");
        builder.AppendLine("Danh sách các chỉ số xét nghiệm đo được:");

        foreach (var detail in session.LabTestResultDetails.OrderBy(x => x.CreatedAt))
        {
            AppendResultDetailLine(builder, detail);
            AppendAdviceLines(builder, detail.AdviceCache);
        }

        return builder.ToString();
    }

    private static void AppendResultDetailLine(StringBuilder builder, LabTestResultDetail detail)
    {
        var name = detail.Indicator?.FullName ?? detail.RawExtractedName ?? "Chưa xác định";
        var value = detail.UserValue?.ToString(CultureInfo.InvariantCulture)
            ?? detail.RawExtractedValue
            ?? "N/A";
        var unit = detail.ReferenceUnitUsed ?? detail.Indicator?.Unit ?? string.Empty;
        var statusText = detail.Status switch
        {
            LabResultStatus.Normal => "Bình thường",
            LabResultStatus.High => "Cao (Vượt ngưỡng)",
            LabResultStatus.Low => "Thấp (Dưới ngưỡng)",
            _ => "Chưa xác định",
        };

        var range = detail.ReferenceMinUsed.HasValue || detail.ReferenceMaxUsed.HasValue
            ? $"Khoảng tham chiếu: {detail.ReferenceMinUsed} - {detail.ReferenceMaxUsed} {unit}".Trim()
            : "Không có khoảng tham chiếu tiêu chuẩn";

        builder.AppendLine($"- {name}: {value} {unit} | Trạng thái: {statusText} | {range}".Trim());
    }

    private static void AppendAdviceLines(StringBuilder builder, LabIndicatorAdviceCache? advice)
    {
        if (advice is null)
        {
            return;
        }

        AppendAdviceLine(builder, "Nhận định y khoa", advice.Summary);
        AppendAdviceLine(builder, "Nguyên nhân có thể", advice.PossibleCauses);
        AppendAdviceLine(builder, "Lời khuyên lối sống", advice.LifestyleAdvice);
        AppendAdviceLine(builder, "Lời khuyên dinh dưỡng", advice.NutritionalAdvice);
    }

    private static void AppendAdviceLine(StringBuilder builder, string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        builder.AppendLine($"  * {label}: {value}");
    }

    private static LabTestUploadResponse MapToResponse(LabTestSession session)
    {
        return new LabTestUploadResponse
        {
            SessionId = session.Id,
            DocumentUrl = session.DocumentUrl ?? string.Empty,
            Status = session.Status,
            RawOcrText = session.RawOcrText ?? string.Empty,
            PatientGenderAtTest = session.PatientGenderAtTest,
            PatientAgeAtTest = session.PatientAgeAtTest,
            ProcessedAt = session.ProcessedAt,
            AiSummary = session.AiSummary,
            AiSummaryStatus = session.AiSummaryStatus,
            Results = session.LabTestResultDetails
                .OrderBy(x => x.CreatedAt)
                .Select(MapResultItem)
                .ToList(),
        };
    }

    private static LabTestSessionSummaryResponse MapToSessionSummary(LabTestSession session)
    {
        return new LabTestSessionSummaryResponse
        {
            UserId = session.UserId,
            SessionId = session.Id,
            DocumentUrl = session.DocumentUrl,
            Status = session.Status,
            PatientGenderAtTest = session.PatientGenderAtTest,
            PatientAgeAtTest = session.PatientAgeAtTest,
            FacilityName = session.FacilityName,
            ProcessedAt = session.ProcessedAt,
            CreatedAt = session.CreatedAt,
        };
    }

    private static LabTestResultItemResponse MapResultItem(LabTestResultDetail detail)
    {
        ReferenceComparisonType? comparisonType = detail.ReferenceMinUsed.HasValue && detail.ReferenceMaxUsed.HasValue
            ? ReferenceComparisonType.Between
            : detail.ReferenceMaxUsed.HasValue && !detail.ReferenceMinUsed.HasValue
                ? ReferenceComparisonType.LessThanOrEqual
                : detail.ReferenceMinUsed.HasValue && !detail.ReferenceMaxUsed.HasValue
                    ? ReferenceComparisonType.GreaterThanOrEqual
                    : null;

        return new LabTestResultItemResponse
        {
            ResultDetailId = detail.Id,
            RawExtractedName = detail.RawExtractedName ?? string.Empty,
            RawExtractedValue = detail.RawExtractedValue,
            UserValue = detail.UserValue,
            Status = detail.Status,
            IsMatched = detail.IsMatched,
            MatchConfidence = detail.MatchConfidence,
            ReferenceMinUsed = detail.ReferenceMinUsed,
            ReferenceMaxUsed = detail.ReferenceMaxUsed,
            ReferenceUnitUsed = detail.ReferenceUnitUsed,
            ComparisonTypeUsed = comparisonType,
            DeviationPercent = detail.DeviationPercent,
            Indicator = detail.Indicator is null ? null : MapIndicator(detail.Indicator),
            ReferenceRangeUsed = comparisonType.HasValue
                ? new LabIndicatorReferenceRangeResponse
                {
                    ReferenceRangeId = Guid.Empty,
                    IndicatorId = detail.IndicatorId ?? Guid.Empty,
                    ComparisonType = comparisonType.Value,
                    MinValue = detail.ReferenceMinUsed,
                    MaxValue = detail.ReferenceMaxUsed,
                    Unit = detail.ReferenceUnitUsed,
                }
                : null,
            Advice = detail.AdviceCache is null ? null : MapAdvice(detail.AdviceCache),
        };
    }

    private static LabIndicatorResponse MapIndicator(LabIndicatorMaster indicator)
    {
        return new LabIndicatorResponse
        {
            IndicatorId = indicator.Id,
            Symbol = indicator.Symbol,
            FullName = indicator.FullName,
            Unit = indicator.Unit,
            MinReference = indicator.MinReference,
            MaxReference = indicator.MaxReference,
            Description = indicator.Description,
            Category = indicator.Category,
            IsActive = indicator.IsActive,
        };
    }

    private static LabIndicatorAdviceCacheResponse MapAdvice(LabIndicatorAdviceCache advice)
    {
        return new LabIndicatorAdviceCacheResponse
        {
            CacheId = advice.Id,
            IndicatorId = advice.IndicatorId,
            Status = advice.Status,
            DisplayTitle = advice.DisplayTitle,
            Summary = advice.Summary,
            PossibleCauses = advice.PossibleCauses,
            LifestyleAdvice = advice.LifestyleAdvice,
            NutritionalAdvice = advice.NutritionalAdvice,
            UrgencyLevel = advice.UrgencyLevel,
            SeverityLevel = advice.SeverityLevel,
            WarningSigns = advice.WarningSigns,
        };
    }

    private static List<string> ValidateDocumentUrl(string documentUrl)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(documentUrl))
        {
            errors.Add("DocumentUrl là bắt buộc");
            return errors;
        }

        if (!Uri.TryCreate(documentUrl, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
        {
            errors.Add("DocumentUrl không hợp lệ");
            return errors;
        }

        var extension = Path.GetExtension(uri.AbsolutePath);
        if (string.IsNullOrWhiteSpace(extension) || !AllowedExtensions.Contains(extension))
        {
            errors.Add("Định dạng file không được hỗ trợ. Cho phép: jpg, jpeg, png, bmp, tif, tiff, pdf.");
        }

        return errors;
    }
}
