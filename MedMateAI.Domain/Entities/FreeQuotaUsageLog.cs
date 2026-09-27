using MedMateAI.Domain.Enums;

namespace MedMateAI.Domain.Entities;

public sealed class FreeQuotaUsageLog
{
    public Guid Id { get; set; }
    public Guid FreeQuotaUsageId { get; set; }
    public Guid UserId { get; set; }
    public SubscriptionQuotaActionType ActionType { get; set; }
    public int UsedCountBefore { get; set; }
    public int UsedCountAfter { get; set; }
    public int ReservedCountBefore { get; set; }
    public int ReservedCountAfter { get; set; }
    public string? ReferenceType { get; set; }
    public Guid? ReferenceId { get; set; }
    public string? Reason { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public FreeQuotaUsage FreeQuotaUsage { get; set; } = null!;
}
