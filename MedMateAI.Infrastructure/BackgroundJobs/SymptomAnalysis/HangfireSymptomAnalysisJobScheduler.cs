using Hangfire;
using MedMateAI.Application.IService;

namespace MedMateAI.Infrastructure.BackgroundJobs;

public sealed class HangfireSymptomAnalysisJobScheduler : ISymptomAnalysisJobScheduler
{
    public void EnqueueAnalyze(Guid sessionId)
    {
        BackgroundJob.Enqueue<SymptomAnalysisMedGemmaJob>(job => job.ExecuteAsync(sessionId, null!));
    }

    public void EnqueueQuotaFinalize(Guid sessionId)
    {
        BackgroundJob.Enqueue<SymptomAnalysisQuotaFinalizeJob>(job => job.ExecuteAsync(sessionId));
    }
}
