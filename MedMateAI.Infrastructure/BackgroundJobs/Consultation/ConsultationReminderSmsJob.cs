using Hangfire;
using MedMateAI.Application.IService;

namespace MedMateAI.Infrastructure.BackgroundJobs;

public sealed class ConsultationReminderSmsJob
{
    private readonly IConsultationSessionService _consultationSessionService;

    public ConsultationReminderSmsJob(IConsultationSessionService consultationSessionService)
    {
        _consultationSessionService = consultationSessionService;
    }

    [SessionJobAutomaticRetry]
    public Task ExecuteAsync(Guid sessionId)
    {
        return _consultationSessionService.ProcessSendReminderSmsAsync(sessionId);
    }
}
