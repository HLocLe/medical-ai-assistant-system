using Hangfire;
using Hangfire.Server;
using MedMateAI.Application.Helpers;
using MedMateAI.Application.IService;
using MedMateAI.Application.Options;
using Microsoft.Extensions.Options;

namespace MedMateAI.Infrastructure.BackgroundJobs;

public sealed class LabTestSummaryJob
{
    private readonly ILabTestService _labTestService;
    private readonly BackgroundJobRetryOptions _retryOptions;

    public LabTestSummaryJob(
        ILabTestService labTestService,
        IOptions<BackgroundJobRetryOptions> retryOptions)
    {
        _labTestService = labTestService;
        _retryOptions = retryOptions.Value;
    }

    [SessionJobAutomaticRetry]
    public async Task ExecuteAsync(Guid sessionId, PerformContext context)
    {
        var retryCount = context.GetJobParameter<int>("RetryCount");
        var isFinalAttempt = BackgroundJobRetry.IsFinalAttempt(retryCount, _retryOptions.MaxAttempts);

        await _labTestService.ProcessSummaryAsync(sessionId, isFinalAttempt);
    }
}
