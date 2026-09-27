using MedMateAI.Application.IService;

namespace MedMateAI.Infrastructure.BackgroundJobs;

public sealed class SymptomAnalysisQuotaFinalizeJob
{
    private readonly ISymptomAnalysisQuotaService _quotaService;

    public SymptomAnalysisQuotaFinalizeJob(ISymptomAnalysisQuotaService quotaService)
    {
        _quotaService = quotaService;
    }

    [SessionJobAutomaticRetry]
    public Task ExecuteAsync(Guid sessionId) => _quotaService.FinalizeAsync(sessionId);
}
