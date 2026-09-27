using MedMateAI.Domain.Common;
using MedMateAI.Domain.Entities;

namespace MedMateAI.Domain.Repository;

public interface IFreeQuotaUsageRepository
{
    Task AcquireIdempotencyLockAsync(
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<FreeQuotaUsage> GetOrCreateForUpdateAsync(
        Guid userId,
        string feature,
        DateOnly businessDate,
        int limitValue,
        DateTime utcNow,
        CancellationToken cancellationToken = default);

    Task<FreeQuotaUsage?> GetAsync(
        Guid userId,
        string feature,
        DateOnly businessDate,
        CancellationToken cancellationToken = default);

    Task<FreeQuotaMutationResult?> ReserveAsync(
        Guid usageId,
        DateTime utcNow,
        CancellationToken cancellationToken = default);

    Task<FreeQuotaMutationResult?> ConsumeAsync(
        Guid usageId,
        DateTime utcNow,
        CancellationToken cancellationToken = default);

    Task<FreeQuotaMutationResult?> ReleaseAsync(
        Guid usageId,
        DateTime utcNow,
        CancellationToken cancellationToken = default);

    Task<bool> HasLogAsync(
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<bool> TryInsertLogAsync(
        FreeQuotaUsageLog log,
        CancellationToken cancellationToken = default);
}
