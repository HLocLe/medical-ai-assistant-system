using MedMateAI.Application.DTOs.SymptomAnalysis.Responses.ClinicalQuestions;
using MedMateAI.Domain.Enums;

namespace MedMateAI.Application.DTOs.SymptomAnalysis.Responses.Session;

public sealed class SymptomAnalysisResponse
{
    public Guid SessionId { get; set; }

    public string? InputText { get; set; }

    public string? SeverityLevel { get; set; }

    public SymptomAnalysisSessionStatus Status { get; set; }

    public IReadOnlyList<SessionSymptomResponse> Symptoms { get; set; } = Array.Empty<SessionSymptomResponse>();

    public IReadOnlyList<ClinicalQuestionAnswerResult> Answers { get; set; } =
        Array.Empty<ClinicalQuestionAnswerResult>();

    public IReadOnlyList<RecommendedDepartmentResponse> RecommendedDepartments { get; set; } =
        Array.Empty<RecommendedDepartmentResponse>();
}
