using MedMateAI.Domain.Enums;

namespace MedMateAI.Domain.Entities;

public sealed class SymptomAnalysisSession : BaseEntity
{
    public Guid? UserId { get; set; }

    public Guid? UserSubscriptionId { get; set; }

    public Guid? UserSubscriptionUsageId { get; set; }

    public QuotaSource QuotaSource { get; set; } = QuotaSource.None;

    public Guid? FreeQuotaUsageId { get; set; }

    public DateTime? SubmittedAt { get; set; }

    public string? InputText { get; set; }

    public string? SeverityLevel { get; set; }

    public SymptomAnalysisSessionStatus Status { get; set; } = SymptomAnalysisSessionStatus.Processing;

    public SymptomAnalysisSessionType SessionType { get; set; } = SymptomAnalysisSessionType.None;

    public bool DisclaimerShown { get; set; }

    public DateTime? CompletedAt { get; set; }

    public UserSubscription? UserSubscription { get; set; }

    public UserSubscriptionUsage? UserSubscriptionUsage { get; set; }

    public FreeQuotaUsage? FreeQuotaUsage { get; set; }

    public ICollection<SessionSymptom> SessionSymptoms { get; set; } = new List<SessionSymptom>();

    public ICollection<SessionClinicalQuestionAnswer> ClinicalQuestionAnswers { get; set; } =
        new List<SessionClinicalQuestionAnswer>();

    public ICollection<DepartmentRecommendation> DepartmentRecommendations { get; set; } = new List<DepartmentRecommendation>();

    public ICollection<AISystemConfig> AISystemConfigs { get; set; } = new List<AISystemConfig>();

    public ICollection<AIAnalysis> AIAnalyses { get; set; } = new List<AIAnalysis>();

    public ICollection<RecoveryPlan> RecoveryPlans { get; set; } = new List<RecoveryPlan>();
}
