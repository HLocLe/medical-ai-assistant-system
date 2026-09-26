namespace MedMateAI.Application.DTOs.SymptomAnalysis.Responses.Session;

public sealed class SessionSymptomResponse
{
    public Guid Id { get; set; }

    public string? SymptomName { get; set; }

    public string? Icd10Code { get; set; }

    public double? ConfidenceScore { get; set; }

    public string? ExtractedText { get; set; }
}
