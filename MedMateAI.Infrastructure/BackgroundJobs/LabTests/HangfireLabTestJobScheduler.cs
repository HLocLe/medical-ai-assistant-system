using Hangfire;
using MedMateAI.Application.IService;

namespace MedMateAI.Infrastructure.BackgroundJobs;

public sealed class HangfireLabTestJobScheduler : ILabTestJobScheduler
{
    public void EnqueueOcr(Guid sessionId)
    {
        var ocrJobId = BackgroundJob.Enqueue<LabTestOcrJob>(job => job.ExecuteAsync(sessionId, null!));
        BackgroundJob.ContinueJobWith<LabTestSummaryJob>(ocrJobId, job => job.ExecuteAsync(sessionId, null!));
    }

    public void EnqueueSummary(Guid sessionId)
    {
        BackgroundJob.Enqueue<LabTestSummaryJob>(job => job.ExecuteAsync(sessionId, null!));
    }
}
