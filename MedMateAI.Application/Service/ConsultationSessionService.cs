using System.Text.Json;

using MedMateAI.Application.Common.Time;

using MedMateAI.Application.DTOs.Common;

using MedMateAI.Application.DTOs.ConsultationSessions.Requests;

using MedMateAI.Application.DTOs.ConsultationSessions.Responses;

using MedMateAI.Application.DTOs.WebChatbot.Requests;

using MedMateAI.Application.DTOs.WebChatbot.Responses;

using MedMateAI.Application.Helpers;

using MedMateAI.Application.IService;

using MedMateAI.Application.Models.Notifications;

using MedMateAI.Application.Models.ServiceCredits;

using MedMateAI.Domain.Entities;

using MedMateAI.Domain.Enums;

using MedMateAI.Domain.Persistence;

using MedMateAI.Domain.Repository;



namespace MedMateAI.Application.Service;



public sealed partial class ConsultationSessionService : IConsultationSessionService

{
    private const string DoctorQuestionsTaskType = "ConsultationDoctorQuestions";



    private static readonly JsonSerializerOptions QuestionJsonOptions = new()

    {

        PropertyNameCaseInsensitive = true,

    };



    private readonly IMedicalDepartmentService _medicalDepartmentService;

    private readonly IMedicalFacilityService _medicalFacilityService;

    private readonly IAIConfigService _aiConfigService;

    private readonly IAIChatProvider _aiChatProvider;

    private readonly IGenericRepository<ConsultationSession> _consultationSessions;

    private readonly IGenericRepository<ConsultationQuestion> _consultationQuestions;

    private readonly IUnitOfWork _unitOfWork;

    private readonly IUserService _userService;

    private readonly IChecklistItemService _checklistItemService;

    private readonly IEmailSender _emailSender;

    private readonly IConsultationSessionJobScheduler _jobScheduler;

    private readonly IConsultationSessionQuotaService _quotaService;

    private readonly IPushNotificationGateway _pushGateway;

    private readonly IUserPushDeviceRepository _pushDeviceRepository;



    public ConsultationSessionService(

        IMedicalDepartmentService medicalDepartmentService,

        IMedicalFacilityService medicalFacilityService,

        IAIConfigService aiConfigService,

        IAIChatProvider aiChatProvider,

        IGenericRepository<ConsultationSession> consultationSessions,

        IGenericRepository<ConsultationQuestion> consultationQuestions,

        IUnitOfWork unitOfWork,

        IUserService userService,

        IChecklistItemService checklistItemService,

        IEmailSender emailSender,

        IConsultationSessionJobScheduler jobScheduler,

        IConsultationSessionQuotaService quotaService,

        IPushNotificationGateway pushGateway,

        IUserPushDeviceRepository pushDeviceRepository)

    {

        _medicalDepartmentService = medicalDepartmentService;

        _medicalFacilityService = medicalFacilityService;

        _aiConfigService = aiConfigService;

        _aiChatProvider = aiChatProvider;

        _consultationSessions = consultationSessions;

        _consultationQuestions = consultationQuestions;

        _unitOfWork = unitOfWork;

        _userService = userService;

        _checklistItemService = checklistItemService;

        _emailSender = emailSender;

        _jobScheduler = jobScheduler;

        _quotaService = quotaService;

        _pushGateway = pushGateway;

        _pushDeviceRepository = pushDeviceRepository;

    }



    public async Task<(bool Succeeded, IEnumerable<string> Errors, GenerateConsultationQuestionsResponse? Data)> GenerateDoctorQuestionsAsync(

        Guid userId,

        Guid departmentId,

        string symptoms,

        Guid? facilityId = null,

        DateTime? appointmentTime = null,

        CancellationToken cancellationToken = default)

    {

        var inputError = ValidateGenerateDoctorQuestionsInput(userId, departmentId, symptoms);

        if (inputError is not null)

        {

            return (false, new[] { inputError }, null);

        }



        var appointmentError = ValidateAppointmentTime(appointmentTime);

        if (appointmentError is not null)

        {

            return (false, new[] { appointmentError }, null);

        }



        var department = await _medicalDepartmentService.GetMedicalDepartmentByIdAsync(departmentId, cancellationToken);

        var departmentName = department?.DepartmentName?.Trim();

        if (department is null || string.IsNullOrWhiteSpace(departmentName))

        {

            return (false, new[] { department is null ? "Department not found." : "Department name is not available." }, null);

        }



        var (facilityOk, facilityError, normalizedFacilityId) = await TryNormalizeFacilityIdAsync(

            facilityId,

            cancellationToken);

        if (!facilityOk)

        {

            return (false, new[] { facilityError! }, null);

        }



        var aiConfig = await _aiConfigService.GetActiveAIConfigByTaskTypeAsync(

            DoctorQuestionsTaskType,

            cancellationToken);



        if (aiConfig is null || string.IsNullOrWhiteSpace(aiConfig.SystemPrompt))

        {

            return (false, new[] { "AI is not config." }, null);

        }



        var hasDepartmentQuestions = await _unitOfWork.DepartmentConsultationQuestions.FirstOrDefaultAsync(

            question => !question.IsDeleted

                && question.IsActive

                && question.DepartmentId == departmentId,

            cancellationToken: cancellationToken);



        if (hasDepartmentQuestions is null)

        {

            return (false, new[] { "Không có câu hỏi tư vấn theo khoa để gửi cho AI." }, null);

        }



        var utcNow = DateTime.UtcNow;

        var trimmedSymptoms = symptoms.Trim();

        var session = new ConsultationSession

        {

            Id = Guid.NewGuid(),

            UserId = userId,

            DepartmentId = departmentId,

            FacilityId = normalizedFacilityId,

            AppointmentTime = NormalizeAppointmentTimeUtc(appointmentTime),

            UserSymptoms = trimmedSymptoms,

            Status = ConsultationSessionStatus.Processing,

            CreatedAt = utcNow,

        };

        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        try
        {
            var reserveResult = await _quotaService.ReserveAsync(
                userId,
                session.Id,
                userId,
                utcNow,
                cancellationToken);

            if (!reserveResult.Success || reserveResult.Data is null)
            {
                await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
                return (false, new[] { reserveResult.Error.ToStableCode() }, null);
            }

            session.UserSubscriptionId = reserveResult.Data.UserSubscriptionId;
            session.UserSubscriptionUsageId = reserveResult.Data.Id;

            _consultationSessions.Add(session);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            throw;
        }



        _jobScheduler.EnqueueGenerateDoctorQuestions(session.Id);



        return (true, Array.Empty<string>(), new GenerateConsultationQuestionsResponse

        {

            SessionId = session.Id,

            DepartmentId = departmentId,

            DepartmentName = departmentName,

            FacilityId = session.FacilityId,

            AppointmentTime = session.AppointmentTime,

            Symptoms = trimmedSymptoms,

            Status = session.Status,

            Questions = [],

            Model = null,

        });

    }



    private static string? ValidateGenerateDoctorQuestionsInput(

        Guid userId,

        Guid departmentId,

        string symptoms)

    {

        if (userId == Guid.Empty)

        {

            return "User id is required.";

        }



        if (departmentId == Guid.Empty)

        {

            return "Department id is required.";

        }



        if (string.IsNullOrWhiteSpace(symptoms))

        {

            return "Symptoms are required.";

        }



        return null;

    }



    private async Task<(bool Succeeded, string? Error, Guid? FacilityId)> TryNormalizeFacilityIdAsync(

        Guid? facilityId,

        CancellationToken cancellationToken)

    {

        if (!facilityId.HasValue || facilityId.Value == Guid.Empty)

        {

            return (true, null, null);

        }



        var facility = await _medicalFacilityService.GetMedicalFacilityByIdAsync(facilityId.Value, cancellationToken);

        if (facility is null)

        {

            return (false, "Facility not found.", null);

        }



        return (true, null, facilityId.Value);

    }



    private static DateTime? NormalizeAppointmentTimeUtc(DateTime? appointmentTime)

    {

        if (!appointmentTime.HasValue)

        {

            return null;

        }



        var value = appointmentTime.Value;

        return value.Kind switch

        {

            DateTimeKind.Utc => value,

            DateTimeKind.Local => value.ToUniversalTime(),

            _ => VietnamBusinessDate.ConvertVietnamLocalToUtc(value),

        };

    }



    private static string? ValidateAppointmentTime(DateTime? appointmentTime)

    {

        if (!appointmentTime.HasValue)

        {

            return null;

        }



        var utc = NormalizeAppointmentTimeUtc(appointmentTime)!.Value;

        if (utc.Kind != DateTimeKind.Utc)

        {

            utc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);

        }



        var vnNow = VietnamBusinessDate.ConvertUtcToVietnamLocal(DateTime.UtcNow);

        var vnAppointment = VietnamBusinessDate.ConvertUtcToVietnamLocal(utc);



        var today = DateOnly.FromDateTime(vnNow);

        var appointmentDate = DateOnly.FromDateTime(vnAppointment);



        if (appointmentDate.Year != today.Year)

        {

            return "Ngày hẹn khám phải nằm trong năm hiện tại.";

        }



        if (vnAppointment < vnNow)

        {

            return "Ngày hẹn khám phải là thời điểm hiện tại hoặc trong tương lai.";

        }



        var maxByMonth = today.AddMonths(1);

        var endOfYear = new DateOnly(today.Year, 12, 31);

        var maxAllowed = maxByMonth < endOfYear ? maxByMonth : endOfYear;



        if (appointmentDate > maxAllowed)

        {

            return "Ngày hẹn khám chỉ được chọn trước tối đa 1 tháng và trong năm hiện tại.";

        }



        return null;

    }



    public Task ProcessGenerateDoctorQuestionsAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default) =>
        ProcessGenerateDoctorQuestionsAsync(sessionId, isFinalAttempt: true, cancellationToken);

    public async Task ProcessGenerateDoctorQuestionsAsync(
        Guid sessionId,
        bool isFinalAttempt,
        CancellationToken cancellationToken = default)
    {
        if (sessionId == Guid.Empty)
        {
            return;
        }

        var session = await _consultationSessions.FirstOrDefaultAsync(
            x => !x.IsDeleted && x.Id == sessionId,
            cancellationToken: cancellationToken);

        if (session is null || session.Status != ConsultationSessionStatus.Processing)
        {
            return;
        }

        var department = await _medicalDepartmentService.GetMedicalDepartmentByIdAsync(
            session.DepartmentId,
            cancellationToken);
        var departmentName = department?.DepartmentName?.Trim();
        if (string.IsNullOrWhiteSpace(departmentName))
        {
            await MarkSessionFailedAsync(session, cancellationToken);
            return;
        }

        var aiConfig = await _aiConfigService.GetActiveAIConfigByTaskTypeAsync(
            DoctorQuestionsTaskType,
            cancellationToken);

        if (aiConfig is null || string.IsNullOrWhiteSpace(aiConfig.SystemPrompt))
        {
            await MarkSessionFailedAsync(session, cancellationToken);
            return;
        }

        var departmentQuestions = await _unitOfWork.DepartmentConsultationQuestions.GetAllAsync(
            question => !question.IsDeleted
                && question.IsActive
                && question.DepartmentId == session.DepartmentId,
            query => query
                .OrderBy(question => question.Category)
                .ThenBy(question => question.SortOrder)
                .ThenBy(question => question.QuestionText),
            cancellationToken: cancellationToken);

        if (departmentQuestions.Count == 0)
        {
            await MarkSessionFailedAsync(session, cancellationToken);
            return;
        }

        var trimmedSymptoms = session.UserSymptoms?.Trim() ?? string.Empty;
        var userPrompt = BuildUserPrompt(
            departmentName,
            trimmedSymptoms,
            departmentQuestions);

        AIProviderChatResult aiResult;
        try
        {
            aiResult = await _aiChatProvider.GenerateAsync(
                new AIProviderChatRequest
                {
                    SystemPrompt = aiConfig.SystemPrompt.Trim(),
                    UserMessage = userPrompt,
                    Model = aiConfig.Model ?? string.Empty,
                    Temperature = aiConfig.Temperature,
                    MaxTokens = aiConfig.MaxTokens,
                },
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (BackgroundJobRetry.IsRetryable(ex))
        {
            if (!isFinalAttempt)
            {
                throw;
            }

            await MarkSessionFailedAsync(session, cancellationToken);
            return;
        }
        catch (Exception)
        {
            await MarkSessionFailedAsync(session, cancellationToken);
            return;
        }

        if (!TryParseDoctorQuestionsJson(aiResult.Content, out var questions))
        {
            await MarkSessionFailedAsync(session, cancellationToken);
            return;
        }

        var utcNow = DateTime.UtcNow;
        var priority = 0;
        foreach (var question in questions)
        {
            _consultationQuestions.Add(new ConsultationQuestion
            {
                Id = Guid.NewGuid(),
                ConsultationSessionId = session.Id,
                Category = question.Category,
                QuestionText = question.Question,
                Priority = priority++,
                CreatedAt = utcNow,
            });
        }

        session.Status = ConsultationSessionStatus.Completed;
        session.UpdatedAt = utcNow;
        _consultationSessions.Update(session);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<PagedResponse<ConsultationSessionSummaryResponse>> GetMyCompletedSessionsAsync(

        Guid userId,

        int pageNumber,

        int pageSize,

        CancellationToken cancellationToken = default)

    {

        if (userId == Guid.Empty)

        {

            return new PagedResponse<ConsultationSessionSummaryResponse>();

        }



        var paged = await _consultationSessions.GetPagedAsync(

            pageNumber,

            pageSize,

            x => !x.IsDeleted && x.UserId == userId && x.Status == ConsultationSessionStatus.Completed,

            q => q.OrderByDescending(x => x.CreatedAt),

            cancellationToken: cancellationToken);



        var departmentNames = new Dictionary<Guid, string>();

        foreach (var departmentId in paged.Items.Select(session => session.DepartmentId).Distinct())

        {

            var department = await _medicalDepartmentService.GetMedicalDepartmentByIdAsync(

                departmentId,

                cancellationToken);

            departmentNames[departmentId] = department?.DepartmentName?.Trim() ?? string.Empty;

        }



        var facilityNames = new Dictionary<Guid, string>();

        foreach (var facilityIdValue in paged.Items

            .Where(session => session.FacilityId.HasValue)

            .Select(session => session.FacilityId!.Value)

            .Distinct())

        {

            var facility = await _medicalFacilityService.GetMedicalFacilityByIdAsync(

                facilityIdValue,

                cancellationToken);

            facilityNames[facilityIdValue] = facility?.FacilityName?.Trim() ?? string.Empty;

        }



        return new PagedResponse<ConsultationSessionSummaryResponse>

        {

            PageNumber = paged.PageNumber,

            PageSize = paged.PageSize,

            TotalCount = paged.TotalCount,

            TotalPages = paged.TotalPages,

            Items = paged.Items

                .Select(session => new ConsultationSessionSummaryResponse

                {

                    SessionId = session.Id,

                    DepartmentId = session.DepartmentId,

                    DepartmentName = departmentNames.GetValueOrDefault(session.DepartmentId),

                    FacilityId = session.FacilityId,

                    FacilityName = session.FacilityId.HasValue

                        ? facilityNames.GetValueOrDefault(session.FacilityId.Value)

                        : null,

                    AppointmentTime = session.AppointmentTime,

                    Symptoms = session.UserSymptoms?.Trim() ?? string.Empty,

                    Status = session.Status,

                    CreatedAt = session.CreatedAt,

                })

                .ToList(),

        };

    }



    public async Task<(bool NotFound, ConsultationSessionDetailResponse? Data)> GetConsultationSessionByIdAsync(

        Guid userId,

        Guid sessionId,

        CancellationToken cancellationToken = default)

    {

        if (userId == Guid.Empty || sessionId == Guid.Empty)

        {

            return (true, null);

        }



        var session = await _consultationSessions.FirstOrDefaultAsync(

            x => !x.IsDeleted && x.Id == sessionId && x.UserId == userId,

            cancellationToken: cancellationToken);



        if (session is null)

        {

            return (true, null);

        }



        var department = await _medicalDepartmentService.GetMedicalDepartmentByIdAsync(

            session.DepartmentId,

            cancellationToken);



        string? facilityName = null;

        if (session.FacilityId.HasValue)

        {

            var facility = await _medicalFacilityService.GetMedicalFacilityByIdAsync(

                session.FacilityId.Value,

                cancellationToken);

            facilityName = facility?.FacilityName?.Trim();

        }



        var questionsPaged = await _consultationQuestions.GetPagedAsync(

            1,

            100,

            q => !q.IsDeleted && q.ConsultationSessionId == sessionId,

            q => q.OrderBy(x => x.Priority),

            cancellationToken: cancellationToken);



        return (false, new ConsultationSessionDetailResponse

        {

            SessionId = session.Id,

            DepartmentId = session.DepartmentId,

            DepartmentName = department?.DepartmentName?.Trim() ?? string.Empty,

            FacilityId = session.FacilityId,

            FacilityName = facilityName,

            AppointmentTime = session.AppointmentTime,

            Symptoms = session.UserSymptoms?.Trim() ?? string.Empty,

            Status = session.Status,

            CreatedAt = session.CreatedAt,

            Questions = questionsPaged.Items

                .Select(question => new ConsultationQuestionResponse

                {

                    Id = question.Id,

                    QuestionText = question.QuestionText,

                    Category = question.Category,

                    Priority = question.Priority,

                })

                .ToList(),

        });

    }



    internal static string BuildUserPrompt(

        string departmentName,

        string symptoms,

        IReadOnlyList<DepartmentConsultationQuestion> departmentQuestions)

    {

        var lines = new List<string>

        {

            $"Department: {departmentName.Trim()}",

            $"Symptoms: {symptoms.Trim()}",

        };



        lines.Add("Department consultation questions (select exactly 5 questions in total, with exactly 1 question from each of the 5 categories: Diagnosis, Tests, Treatment, Lifestyle, Follow-up. If a category has no questions available in the list, select an additional question from another category to ensure the total is exactly 5 questions)");

        var index = 1;

        foreach (var question in departmentQuestions)

        {

            if (string.IsNullOrWhiteSpace(question.QuestionText))

            {

                continue;

            }



            lines.Add($"{index}. [{question.Category}] {question.QuestionText.Trim()}");

            index++;

        }



        return string.Join('\n', lines);

    }



    private async Task MarkSessionFailedAsync(

        ConsultationSession session,

        CancellationToken cancellationToken)

    {

        session.Status = ConsultationSessionStatus.Failed;

        session.UpdatedAt = DateTime.UtcNow;

        _consultationSessions.Update(session);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

    }



    private static bool TryParseDoctorQuestionsJson(

        string content,

        out IReadOnlyList<ConsultationDoctorQuestionItemResponse> questions)

    {

        questions = [];



        var normalizedJson = StripMarkdownCodeFence(content);

        if (string.IsNullOrWhiteSpace(normalizedJson))

        {

            return false;

        }



        try

        {

            var parsed = JsonSerializer.Deserialize<List<ConsultationDoctorQuestionItemResponse>>(

                normalizedJson,

                QuestionJsonOptions);



            if (parsed is null || parsed.Count == 0)

            {

                return false;

            }



            questions = parsed

                .Where(item =>

                    !string.IsNullOrWhiteSpace(item.Category) &&

                    !string.IsNullOrWhiteSpace(item.Question))

                .Select(item => new ConsultationDoctorQuestionItemResponse

                {

                    Category = item.Category.Trim(),

                    Question = item.Question.Trim(),

                })

                .ToList();



            return questions.Count > 0;

        }

        catch (JsonException)

        {

            return false;

        }

    }



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

}


