using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Hangfire.Storage;
using MedMateAI.Application.Options;

namespace MedMateAI.Infrastructure.BackgroundJobs;

/// <summary>
/// Job-scoped AutomaticRetry with exponential backoff + jitter.
/// Order 10 so it runs before Hangfire's global AutomaticRetry (Order 20).
/// </summary>
public sealed class SessionJobAutomaticRetryAttribute : JobFilterAttribute, IElectStateFilter, IApplyStateFilter
{
    private const double JitterRatio = 0.1;
    private const int MaximumJitterSeconds = 5;
    private readonly object _lockObject = new();
    private int _attempts;
    private AttemptsExceededAction _onAttemptsExceeded = AttemptsExceededAction.Fail;

    public SessionJobAutomaticRetryAttribute()
    {
        var defaults = new BackgroundJobRetryOptions();
        Attempts = defaults.MaxAttempts;
        RetryBaseSeconds = defaults.RetryBaseSeconds;
        RetryMaxSeconds = defaults.RetryMaxSeconds;
        Order = 10;
    }

    public int Attempts
    {
        get { lock (_lockObject) { return _attempts; } }
        set
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            lock (_lockObject) { _attempts = value; }
        }
    }

    public int RetryBaseSeconds { get; set; }

    public int RetryMaxSeconds { get; set; }

    public AttemptsExceededAction OnAttemptsExceeded
    {
        get { lock (_lockObject) { return _onAttemptsExceeded; } }
        set { lock (_lockObject) { _onAttemptsExceeded = value; } }
    }

    public void OnStateElection(ElectStateContext context)
    {
        if (context.CandidateState is not FailedState failedState)
        {
            return;
        }

        var retryAttempt = context.GetJobParameter<int>("RetryCount") + 1;
        if (retryAttempt <= Attempts)
        {
            ScheduleAgainLater(context, retryAttempt, failedState);
            return;
        }

        if (OnAttemptsExceeded == AttemptsExceededAction.Delete)
        {
            context.CandidateState = new DeletedState
            {
                Reason = failedState.Exception?.Message,
            };
        }
    }

    public void OnStateApplied(ApplyStateContext context, IWriteOnlyTransaction transaction)
    {
    }

    public void OnStateUnapplied(ApplyStateContext context, IWriteOnlyTransaction transaction)
    {
    }

    private void ScheduleAgainLater(ElectStateContext context, int retryAttempt, FailedState failedState)
    {
        context.SetJobParameter("RetryCount", retryAttempt);

        var delayInSeconds = ComputeDelaySeconds(retryAttempt, RetryBaseSeconds, RetryMaxSeconds);
        var delay = TimeSpan.FromSeconds(delayInSeconds);

        const int maxMessageLength = 50;
        var exceptionMessage = failedState.Exception?.Message ?? "Unknown error";
        if (exceptionMessage.Length > maxMessageLength)
        {
            exceptionMessage = exceptionMessage[..(maxMessageLength - 1)] + "…";
        }

        context.CandidateState = delay > TimeSpan.Zero
            ? new ScheduledState(delay)
            {
                Reason = $"Retry attempt {retryAttempt} of {Attempts}: {exceptionMessage}",
            }
            : new EnqueuedState
            {
                Reason = $"Retry attempt {retryAttempt} of {Attempts}: {exceptionMessage}",
            };
    }

    internal static int ComputeDelaySeconds(long attempt, int retryBaseSeconds, int retryMaxSeconds)
    {
        var exponent = Math.Max(0, attempt - 1);
        var uncappedSeconds = retryBaseSeconds * Math.Pow(2, exponent);
        var backoffSeconds = Math.Min(retryMaxSeconds, uncappedSeconds);
        var jitterLimit = Math.Min(
            MaximumJitterSeconds,
            Math.Max(1, (int)Math.Ceiling(backoffSeconds * JitterRatio)));
        var jitterSeconds = Random.Shared.Next(0, jitterLimit + 1);
        return (int)Math.Min(retryMaxSeconds, backoffSeconds + jitterSeconds);
    }
}
