using MedMateAI.Domain.Common;
using MedMateAI.Domain.Entities;
using MedMateAI.Domain.Repository;
using Microsoft.EntityFrameworkCore;

namespace MedMateAI.Infrastructure.Repositories;

public sealed class FreeQuotaUsageRepository : IFreeQuotaUsageRepository
{
    private readonly ApplicationDbContext _context;

    public FreeQuotaUsageRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task AcquireIdempotencyLockAsync(
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        EnsureActiveTransaction();

        await _context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({idempotencyKey}, 0));",
            cancellationToken);
    }

    public async Task<FreeQuotaUsage> GetOrCreateForUpdateAsync(
        Guid userId,
        string feature,
        DateOnly businessDate,
        int limitValue,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        EnsureActiveTransaction();

        var usageId = Guid.NewGuid();

        // Raw SQL keeps get-or-create race-safe without a check-then-insert window.
        await _context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "FreeQuotaUsage"
                ("FreeQuotaUsageId", "UserId", "Feature", "BusinessDate", "LimitValue",
                 "UsedCount", "ReservedCount", "Version", "CreatedAt", "UpdatedAt", "IsDeleted")
            VALUES
                ({usageId}, {userId}, {feature}, {businessDate}, {limitValue},
                 0, 0, 0, {utcNow}, NULL, false)
            ON CONFLICT ("UserId", "Feature", "BusinessDate")
                WHERE "IsDeleted" = false
            DO NOTHING;
            """,
            cancellationToken);

        var usages = await _context.FreeQuotaUsages
            .FromSqlInterpolated($"""
                SELECT *
                FROM "FreeQuotaUsage"
                WHERE "UserId" = {userId}
                  AND "Feature" = {feature}
                  AND "BusinessDate" = {businessDate}
                  AND "IsDeleted" = FALSE
                FOR UPDATE
                """)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return usages.SingleOrDefault()
            ?? throw new InvalidOperationException(
                "The conflict-safe free quota usage insert completed, but the usage row could not be loaded.");
    }

    public Task<FreeQuotaUsage?> GetAsync(
        Guid userId,
        string feature,
        DateOnly businessDate,
        CancellationToken cancellationToken = default)
    {
        return _context.FreeQuotaUsages
            .AsNoTracking()
            .FirstOrDefaultAsync(
                usage =>
                    usage.UserId == userId
                    && usage.Feature == feature
                    && usage.BusinessDate == businessDate
                    && !usage.IsDeleted,
                cancellationToken);
    }

    public async Task<FreeQuotaMutationResult?> ReserveAsync(
        Guid usageId,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        EnsureActiveTransaction();

        var affectedRows = await _context.FreeQuotaUsages
            .Where(usage =>
                usage.Id == usageId
                && !usage.IsDeleted
                && usage.UsedCount + usage.ReservedCount < usage.LimitValue)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(usage => usage.ReservedCount, usage => usage.ReservedCount + 1)
                    .SetProperty(usage => usage.Version, usage => usage.Version + 1)
                    .SetProperty(usage => usage.UpdatedAt, utcNow),
                cancellationToken);

        if (affectedRows == 0)
        {
            return null;
        }

        var state = await GetStateAsync(usageId, cancellationToken);
        return state with
        {
            ReservedCountBefore = state.ReservedCountAfter - 1,
        };
    }

    public async Task<FreeQuotaMutationResult?> ConsumeAsync(
        Guid usageId,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        EnsureActiveTransaction();

        var affectedRows = await _context.FreeQuotaUsages
            .Where(usage =>
                usage.Id == usageId
                && !usage.IsDeleted
                && usage.ReservedCount > 0)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(usage => usage.ReservedCount, usage => usage.ReservedCount - 1)
                    .SetProperty(usage => usage.UsedCount, usage => usage.UsedCount + 1)
                    .SetProperty(usage => usage.Version, usage => usage.Version + 1)
                    .SetProperty(usage => usage.UpdatedAt, utcNow),
                cancellationToken);

        if (affectedRows == 0)
        {
            return null;
        }

        var state = await GetStateAsync(usageId, cancellationToken);
        return state with
        {
            UsedCountBefore = state.UsedCountAfter - 1,
            ReservedCountBefore = state.ReservedCountAfter + 1,
        };
    }

    public async Task<FreeQuotaMutationResult?> ReleaseAsync(
        Guid usageId,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        EnsureActiveTransaction();

        var affectedRows = await _context.FreeQuotaUsages
            .Where(usage =>
                usage.Id == usageId
                && !usage.IsDeleted
                && usage.ReservedCount > 0)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(usage => usage.ReservedCount, usage => usage.ReservedCount - 1)
                    .SetProperty(usage => usage.Version, usage => usage.Version + 1)
                    .SetProperty(usage => usage.UpdatedAt, utcNow),
                cancellationToken);

        if (affectedRows == 0)
        {
            return null;
        }

        var state = await GetStateAsync(usageId, cancellationToken);
        return state with
        {
            ReservedCountBefore = state.ReservedCountAfter + 1,
        };
    }

    public Task<bool> HasLogAsync(
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        return _context.FreeQuotaUsageLogs
            .AsNoTracking()
            .AnyAsync(log => log.IdempotencyKey == idempotencyKey, cancellationToken);
    }

    public async Task<bool> TryInsertLogAsync(
        FreeQuotaUsageLog log,
        CancellationToken cancellationToken = default)
    {
        EnsureActiveTransaction();

        var actionType = log.ActionType.ToString();
        var affectedRows = await _context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "FreeQuotaUsageLog"
                ("FreeQuotaUsageLogId", "FreeQuotaUsageId", "UserId", "ActionType",
                 "UsedCountBefore", "UsedCountAfter", "ReservedCountBefore", "ReservedCountAfter",
                 "ReferenceType", "ReferenceId", "Reason", "IdempotencyKey", "CreatedAt")
            VALUES
                ({log.Id}, {log.FreeQuotaUsageId}, {log.UserId}, {actionType},
                 {log.UsedCountBefore}, {log.UsedCountAfter}, {log.ReservedCountBefore},
                 {log.ReservedCountAfter}, {log.ReferenceType}, {log.ReferenceId}, {log.Reason},
                 {log.IdempotencyKey}, {log.CreatedAt})
            ON CONFLICT ("IdempotencyKey")
            DO NOTHING;
            """,
            cancellationToken);

        return affectedRows == 1;
    }

    private async Task<FreeQuotaMutationResult> GetStateAsync(
        Guid usageId,
        CancellationToken cancellationToken)
    {
        var state = await _context.FreeQuotaUsages
            .AsNoTracking()
            .Where(usage => usage.Id == usageId && !usage.IsDeleted)
            .Select(usage => new FreeQuotaMutationResult(
                usage.Id,
                usage.UserId,
                usage.LimitValue,
                usage.UsedCount,
                usage.UsedCount,
                usage.ReservedCount,
                usage.ReservedCount))
            .SingleOrDefaultAsync(cancellationToken);

        return state ?? throw new InvalidOperationException(
            $"Free quota usage '{usageId}' was updated, but its state could not be loaded.");
    }

    private void EnsureActiveTransaction()
    {
        if (_context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "Free quota usage writes require an active database transaction.");
        }
    }
}
