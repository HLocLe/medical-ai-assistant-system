using Hangfire;
using MedMateAI.Application.IService;

namespace MedMateAI.Infrastructure.BackgroundJobs;

public sealed class HangfireConsultationSessionJobScheduler : IConsultationSessionJobScheduler
{
    public void EnqueueGenerateDoctorQuestions(Guid sessionId)
    {
        BackgroundJob.Enqueue<ConsultationDoctorQuestionsJob>(job => job.ExecuteAsync(sessionId, null!));
    }

    public void EnqueueReminderSms(Guid sessionId)
    {
        BackgroundJob.Enqueue<ConsultationReminderSmsJob>(job => job.ExecuteAsync(sessionId));
    }

    public void ScheduleReminderSms(Guid sessionId, DateTime enqueueAtUtc)
    {
        BackgroundJob.Schedule<ConsultationReminderSmsJob>(
            job => job.ExecuteAsync(sessionId),
            enqueueAtUtc);
    }
}
