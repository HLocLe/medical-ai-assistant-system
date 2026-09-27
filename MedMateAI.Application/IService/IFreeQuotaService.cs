using MedMateAI.Domain.Entities;
using MedMateAI.Domain.Enums;

namespace MedMateAI.Application.IService;

public interface IFreeQuotaService
{
    /// <summary>
    /// Reserves one free daily unit inside the caller's active transaction.
    /// Returns null when the daily free limit is exhausted.
    /// </summary>
    Task<FreeQuotaUsage?> TryReserveAsync(
        Guid userId,
        string feature,
        int dailyLimit,
        string referenceType,
        Guid referenceId,
        DateTime utcNow,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Consumes or releases a reserved free unit in its own transaction. Safe to call repeatedly.
    /// </summary>
    Task FinalizeAsync(
        Guid usageId,
        Guid userId,
        string feature,
        SubscriptionQuotaActionType actionType,
        string referenceType,
        Guid referenceId,
        CancellationToken cancellationToken = default);

    Task<FreeQuotaUsage?> GetAsync(
        Guid userId,
        string feature,
        DateOnly businessDate,
        CancellationToken cancellationToken = default);
}
