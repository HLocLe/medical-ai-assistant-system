namespace MedMateAI.Application.Options;

public sealed class BackgroundJobRetryOptions
{
    public const string SectionName = "BackgroundJobRetry";

    /// <summary>
    /// Hangfire AutomaticRetry attempts after the first failure (total runs = MaxAttempts + 1).
    /// </summary>
    public int MaxAttempts { get; set; } = 5;

    public int RetryBaseSeconds { get; set; } = 10;

    public int RetryMaxSeconds { get; set; } = 900;
}
