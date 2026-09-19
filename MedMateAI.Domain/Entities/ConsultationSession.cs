using MedMateAI.Domain.Enums;

namespace MedMateAI.Domain.Entities;

public sealed class ConsultationSession : BaseEntity
{
    public Guid UserId { get; set; }

    public Guid? UserSubscriptionId { get; set; }

    public Guid? UserSubscriptionUsageId { get; set; }

    public Guid DepartmentId { get; set; }

    public Guid? FacilityId { get; set; }

    public DateTime? AppointmentTime { get; set; }

    public string? UserSymptoms { get; set; }

    public ConsultationSessionStatus Status { get; set; } = ConsultationSessionStatus.Processing;

    public bool IsReminderEnabled { get; set; }

    /// <summary>When the Hangfire reminder job was scheduled/enqueued.</summary>
    public DateTime? ReminderScheduledAt { get; set; }

    /// <summary>When the reminder email channel succeeded (skip on Hangfire retry).</summary>
    public DateTime? ReminderEmailSentAt { get; set; }

    /// <summary>When the reminder push channel succeeded (skip on Hangfire retry).</summary>
    public DateTime? ReminderPushSentAt { get; set; }

    /// <summary>When all required reminder channels succeeded.</summary>
    public DateTime? ReminderSmsSentAt { get; set; }

    public MedicalDepartment Department { get; set; } = null!;

    public MedicalFacility? Facility { get; set; }

    public UserSubscription? UserSubscription { get; set; }

    public UserSubscriptionUsage? UserSubscriptionUsage { get; set; }

    public ICollection<ConsultationQuestion> ConsultationQuestions { get; set; } = new List<ConsultationQuestion>();

    public ICollection<AISystemConfig> AISystemConfigs { get; set; } = new List<AISystemConfig>();

    public ICollection<AIAnalysis> AIAnalyses { get; set; } = new List<AIAnalysis>();
}
