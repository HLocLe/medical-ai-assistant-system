using MedMateAI.Domain.Enums;

namespace MedMateAI.Domain.Entities;

public sealed class LabTestSession : BaseEntity
{
    public Guid UserId { get; set; }

    public Guid? UserSubscriptionId { get; set; }

    public Guid? UserSubscriptionUsageId { get; set; }

    public string? DocumentUrl { get; set; }

    public string? RawOcrText { get; set; }

    public LabTestSessionStatus Status { get; set; } = LabTestSessionStatus.Processing;

    public Gender? PatientGenderAtTest { get; set; }

    public int? PatientAgeAtTest { get; set; }

    public string? FacilityName { get; set; }

    public string? AiSummary { get; set; }

    public LabTestSummaryStatus? AiSummaryStatus { get; set; }

    public DateTime? ProcessedAt { get; set; }

    public UserSubscription? UserSubscription { get; set; }

    public UserSubscriptionUsage? UserSubscriptionUsage { get; set; }

    public ICollection<LabTestOcrExtract> LabTestOcrExtracts { get; set; } = new List<LabTestOcrExtract>();

    public ICollection<LabTestResultDetail> LabTestResultDetails { get; set; } = new List<LabTestResultDetail>();

    public ICollection<AIAnalysis> AIAnalyses { get; set; } = new List<AIAnalysis>();

    public ICollection<AISystemConfig> AISystemConfigs { get; set; } = new List<AISystemConfig>();

    public ICollection<RecoveryPlan> RecoveryPlans { get; set; } = new List<RecoveryPlan>();
}
