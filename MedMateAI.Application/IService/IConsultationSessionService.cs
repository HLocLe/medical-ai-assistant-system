using MedMateAI.Application.DTOs.Common;
using MedMateAI.Application.DTOs.ConsultationSessions.Requests;
using MedMateAI.Application.DTOs.ConsultationSessions.Responses;

namespace MedMateAI.Application.IService;

public interface IConsultationSessionService
{
    Task<(bool Succeeded, IEnumerable<string> Errors, GenerateConsultationQuestionsResponse? Data)> GenerateDoctorQuestionsAsync(
        Guid userId,
        Guid departmentId,
        string symptoms,
        Guid? facilityId = null,
        DateTime? appointmentTime = null,
        CancellationToken cancellationToken = default);

    Task ProcessGenerateDoctorQuestionsAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);

    Task ProcessGenerateDoctorQuestionsAsync(
        Guid sessionId,
        bool isFinalAttempt,
        CancellationToken cancellationToken = default);

    Task ProcessSendReminderSmsAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);

    Task<PagedResponse<ConsultationSessionSummaryResponse>> GetMyCompletedSessionsAsync(
        Guid userId,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<(bool NotFound, ConsultationSessionDetailResponse? Data)> GetConsultationSessionByIdAsync(
        Guid userId,
        Guid sessionId,
        CancellationToken cancellationToken = default);

    Task<(bool Succeeded, bool NotFound, IEnumerable<string> Errors)> RegisterReminderAsync(
        Guid userId,
        Guid sessionId,
        RegisterConsultationReminderRequest request,
        CancellationToken cancellationToken = default);

    Task<(bool NotFound, ConsultationSummaryResponse? Data)> GetSummaryAsync(
        Guid userId,
        Guid sessionId,
        CancellationToken cancellationToken = default);

    Task<(bool Succeeded, bool NotFound, IEnumerable<string> Errors, ConsultationSummaryResponse? Data)> CompleteSummaryAsync(
        Guid userId,
        Guid sessionId,
        CancellationToken cancellationToken = default);
}