namespace MedMateAI.Application.DTOs.SymptomAnalysis.Responses.Quota;

public sealed class SymptomAnalysisQuotaResponse
{
    public DateOnly BusinessDate { get; set; }

    public int LimitPerDay { get; set; }

    public int UsedToday { get; set; }

    public int ReservedToday { get; set; }

    public int RemainingToday { get; set; }

    public bool IsFreeTier { get; set; }

    public bool HasServiceCredit { get; set; }
}
