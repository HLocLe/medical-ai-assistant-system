namespace MedMateAI.Domain.Entities;

public sealed class SessionSymptom : BaseEntity
{
    public Guid SymptomAnalysisSessionId { get; set; }

    public string? SymptomName { get; set; }

    public string? Icd10Code { get; set; }

    public double? ConfidenceScore { get; set; }

    public string? ExtractedText { get; set; }

    public SymptomAnalysisSession SymptomAnalysisSession { get; set; } = null!;
}
