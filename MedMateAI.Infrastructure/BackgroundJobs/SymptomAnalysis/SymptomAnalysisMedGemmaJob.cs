using Hangfire;
using Hangfire.Server;
using MedMateAI.Application.Helpers;
using MedMateAI.Application.IService;
using MedMateAI.Application.Options;
using Microsoft.Extensions.Options;

namespace MedMateAI.Infrastructure.BackgroundJobs;

public sealed class SymptomAnalysisMedGemmaJob
{
    private readonly ISymptomAnalysisService _symptomAnalysisService;
    private readonly ISymptomAnalysisQuotaService _quotaService;
    private readonly BackgroundJobRetryOptions _retryOptions;

    public SymptomAnalysisMedGemmaJob(
        ISymptomAnalysisService symptomAnalysisService,
        ISymptomAnalysisQuotaService quotaService,
        IOptions<BackgroundJobRetryOptions> retryOptions)
    {
        _symptomAnalysisService = symptomAnalysisService;
        _quotaService = quotaService;
        _retryOptions = retryOptions.Value;
    }

    [SessionJobAutomaticRetry]
    public async Task ExecuteAsync(Guid sessionId, PerformContext context)
    {
        var retryCount = context.GetJobParameter<int>("RetryCount");
        var isFinalAttempt = BackgroundJobRetry.IsFinalAttempt(retryCount, _retryOptions.MaxAttempts);

        try
        {
            await _symptomAnalysisService.ProcessAnalyzeAsync(sessionId, isFinalAttempt);
        }
        finally
        {
          
            await _quotaService.FinalizeAsync(sessionId);
        }
    }
}
