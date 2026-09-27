using MedMateAI.Domain.Enums;

namespace MedMateAI.Application.DTOs.SymptomAnalysis.Responses.ClinicalQuestions;

public sealed class ClinicalQuestionAnswersResponse
{
    public Guid SessionId { get; set; }

    public string UserInput { get; set; } = string.Empty;

    public SymptomAnalysisSessionStatus Status { get; set; }

    public IReadOnlyList<ClinicalQuestionAnswerResult> Answers { get; set; } =
        Array.Empty<ClinicalQuestionAnswerResult>();

    public string MedGemmaPrompt { get; set; } = string.Empty;
}
