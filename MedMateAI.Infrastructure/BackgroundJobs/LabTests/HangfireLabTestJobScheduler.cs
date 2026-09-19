using Hangfire;
using MedMateAI.Application.IService;

namespace MedMateAI.Infrastructure.BackgroundJobs;

public sealed class HangfireLabTestJobScheduler : ILabTestJobScheduler
{
    public void EnqueueOcr(Guid sessionId)
    {
        BackgroundJob.Enqueue<LabTestOcrJob>(job => job.ExecuteAsync(sessionId, null!));
    }
}
