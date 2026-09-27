namespace MedMateAI.Domain.Entities;

public sealed class FreeQuotaUsage : BaseEntity
{
    public Guid UserId { get; set; }
    public string Feature { get; set; } = string.Empty;
    public DateOnly BusinessDate { get; set; }
    public int LimitValue { get; set; }
    public int UsedCount { get; set; }
    public int ReservedCount { get; set; }
    public int Version { get; set; }
    public ICollection<FreeQuotaUsageLog> Logs { get; set; } = new List<FreeQuotaUsageLog>();
}
