using MedMateAI.Domain.Enums;

namespace MedMateAI.Application.DTOs.LabTests.Responses;

public sealed class LabTestSummaryResponse
{
    public Guid SessionId { get; set; }

    public LabTestSummaryStatus Status { get; set; }

    public string? AiSummary { get; set; }
}
