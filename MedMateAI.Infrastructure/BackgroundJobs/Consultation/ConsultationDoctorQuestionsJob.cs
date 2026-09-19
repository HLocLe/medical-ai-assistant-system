using Hangfire;
using Hangfire.Server;
using MedMateAI.Application.Helpers;
using MedMateAI.Application.IService;
using MedMateAI.Application.Options;
using Microsoft.Extensions.Options;

namespace MedMateAI.Infrastructure.BackgroundJobs;

public sealed class ConsultationDoctorQuestionsJob
{
    private readonly IConsultationSessionService _consultationSessionService;
    private readonly IConsultationSessionQuotaService _quotaService;
    private readonly BackgroundJobRetryOptions _retryOptions;

    public ConsultationDoctorQuestionsJob(
        IConsultationSessionService consultationSessionService,
        IConsultationSessionQuotaService quotaService,
        IOptions<BackgroundJobRetryOptions> retryOptions)
    {
        _consultationSessionService = consultationSessionService;
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
            await _consultationSessionService.ProcessGenerateDoctorQuestionsAsync(
                sessionId,
                isFinalAttempt);
        }
        finally
        {
            // Finalize is a no-op while status is Processing (between Hangfire retries).
            await _quotaService.FinalizeAsync(sessionId);
        }
    }
}
