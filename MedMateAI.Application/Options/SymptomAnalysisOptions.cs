namespace MedMateAI.Application.Options;

public sealed class SymptomAnalysisOptions
{
    public const string SectionName = "SymptomAnalysis";

    public int FreeDailyLimit { get; set; } = 5;

    /// <summary>
    /// Sessions still waiting for clinical answers after this many minutes are failed and their quota released.
    /// </summary>
    public int AbandonedSessionTimeoutMinutes { get; set; } = 30;

    public int AbandonedSessionCleanupIntervalMinutes { get; set; } = 5;

    public int AbandonedSessionCleanupBatchSize { get; set; } = 100;
}
