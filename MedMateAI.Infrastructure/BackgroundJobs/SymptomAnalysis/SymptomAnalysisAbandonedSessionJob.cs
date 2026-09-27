using Hangfire;
using MedMateAI.Application.IService;

namespace MedMateAI.Infrastructure.BackgroundJobs;

public sealed class SymptomAnalysisAbandonedSessionJob
{
    private readonly ISymptomAnalysisService _symptomAnalysisService;

    public SymptomAnalysisAbandonedSessionJob(ISymptomAnalysisService symptomAnalysisService)
    {
        _symptomAnalysisService = symptomAnalysisService;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    [AutomaticRetry(Attempts = 0)]
    public Task ExecuteAsync(CancellationToken cancellationToken) =>
        _symptomAnalysisService.ExpireAbandonedSessionsAsync(cancellationToken);
}
