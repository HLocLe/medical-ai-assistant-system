using MedMateAI.Domain.Enums;

namespace MedMateAI.Application.DTOs.LabTests.Responses;

public sealed class LabTestUploadResponse
{
    public Guid SessionId { get; set; }

    public string DocumentUrl { get; set; } = string.Empty;

    public LabTestSessionStatus Status { get; set; }

    public string RawOcrText { get; set; } = string.Empty;

    public Gender? PatientGenderAtTest { get; set; }

    public int? PatientAgeAtTest { get; set; }

    public DateTime? ProcessedAt { get; set; }

    public string? AiSummary { get; set; }

    public LabTestSummaryStatus? AiSummaryStatus { get; set; }

    public IList<LabTestResultItemResponse> Results { get; set; } = new List<LabTestResultItemResponse>();
}
