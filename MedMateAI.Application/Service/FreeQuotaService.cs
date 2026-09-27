using MedMateAI.Application.Common.Time;
using MedMateAI.Application.IService;
using MedMateAI.Domain.Common;
using MedMateAI.Domain.Entities;
using MedMateAI.Domain.Enums;
using MedMateAI.Domain.Persistence;
using Microsoft.Extensions.Logging;

namespace MedMateAI.Application.Service;

public sealed class FreeQuotaService : IFreeQuotaService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<FreeQuotaService> _logger;

    public FreeQuotaService(IUnitOfWork unitOfWork, ILogger<FreeQuotaService> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<FreeQuotaUsage?> TryReserveAsync(
        Guid userId,
        string feature,
        int dailyLimit,
        string referenceType,
        Guid referenceId,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        var businessDate = VietnamBusinessDate.GetToday(utcNow);
        var usage = await _unitOfWork.FreeQuotaUsages.GetOrCreateForUpdateAsync(
            userId,
            feature,
            businessDate,
            dailyLimit,
            utcNow,
            cancellationToken);

        var idempotencyKey = BuildIdempotencyKey(feature, "reserve", referenceId);
        if (await _unitOfWork.FreeQuotaUsages.HasLogAsync(idempotencyKey, cancellationToken))
        {
            return usage;
        }

        var result = await _unitOfWork.FreeQuotaUsages.ReserveAsync(usage.Id, utcNow, cancellationToken);
        if (result is null)
        {
            return null;
        }

        await _unitOfWork.FreeQuotaUsages.TryInsertLogAsync(
            BuildLog(result, SubscriptionQuotaActionType.Reserve, referenceType, referenceId, idempotencyKey, utcNow),
            cancellationToken);

        return usage;
    }

    public async Task FinalizeAsync(
        Guid usageId,
        Guid userId,
        string feature,
        SubscriptionQuotaActionType actionType,
        string referenceType,
        Guid referenceId,
        CancellationToken cancellationToken = default)
    {
        if (actionType is not (SubscriptionQuotaActionType.Consume or SubscriptionQuotaActionType.Release))
        {
            throw new ArgumentOutOfRangeException(nameof(actionType), actionType, "Only Consume or Release is supported.");
        }

        var consumeKey = BuildIdempotencyKey(feature, "consume", referenceId);
        var releaseKey = BuildIdempotencyKey(feature, "release", referenceId);
        var idempotencyKey = actionType == SubscriptionQuotaActionType.Consume ? consumeKey : releaseKey;

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            // One lock per reference so a consume and a release can never both apply.
            await _unitOfWork.FreeQuotaUsages.AcquireIdempotencyLockAsync(
                BuildIdempotencyKey(feature, "finalize", referenceId),
                cancellationToken);

            if (await _unitOfWork.FreeQuotaUsages.HasLogAsync(consumeKey, cancellationToken)
                || await _unitOfWork.FreeQuotaUsages.HasLogAsync(releaseKey, cancellationToken))
            {
                await _unitOfWork.CommitTransactionAsync(cancellationToken);
                return;
            }

            var utcNow = DateTime.UtcNow;
            var result = actionType == SubscriptionQuotaActionType.Consume
                ? await _unitOfWork.FreeQuotaUsages.ConsumeAsync(usageId, utcNow, cancellationToken)
                : await _unitOfWork.FreeQuotaUsages.ReleaseAsync(usageId, utcNow, cancellationToken);

            if (result is null)
            {
                _logger.LogWarning(
                    "Free quota {ActionType} skipped for {ReferenceType} {ReferenceId}: usage {UsageId} has no reserved unit.",
                    actionType,
                    referenceType,
                    referenceId,
                    usageId);
                await _unitOfWork.CommitTransactionAsync(cancellationToken);
                return;
            }

            if (result.UserId != userId)
            {
                throw new InvalidOperationException(
                    $"Free quota usage '{usageId}' does not belong to user '{userId}'.");
            }

            await _unitOfWork.FreeQuotaUsages.TryInsertLogAsync(
                BuildLog(result, actionType, referenceType, referenceId, idempotencyKey, utcNow),
                cancellationToken);

            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            _logger.LogError(
                ex,
                "Free quota {ActionType} failed for {ReferenceType} {ReferenceId}.",
                actionType,
                referenceType,
                referenceId);
            throw;
        }
    }

    public Task<FreeQuotaUsage?> GetAsync(
        Guid userId,
        string feature,
        DateOnly businessDate,
        CancellationToken cancellationToken = default)
    {
        return _unitOfWork.FreeQuotaUsages.GetAsync(userId, feature, businessDate, cancellationToken);
    }

    private static FreeQuotaUsageLog BuildLog(
        FreeQuotaMutationResult result,
        SubscriptionQuotaActionType actionType,
        string referenceType,
        Guid referenceId,
        string idempotencyKey,
        DateTime utcNow) =>
        new()
        {
            Id = Guid.NewGuid(),
            FreeQuotaUsageId = result.UsageId,
            UserId = result.UserId,
            ActionType = actionType,
            UsedCountBefore = result.UsedCountBefore,
            UsedCountAfter = result.UsedCountAfter,
            ReservedCountBefore = result.ReservedCountBefore,
            ReservedCountAfter = result.ReservedCountAfter,
            ReferenceType = referenceType,
            ReferenceId = referenceId,
            Reason = $"Free daily quota {actionType.ToString().ToLowerInvariant()}.",
            IdempotencyKey = idempotencyKey,
            CreatedAt = utcNow,
        };

    private static string BuildIdempotencyKey(string feature, string action, Guid referenceId) =>
        $"{feature}:free:{action}:{referenceId:N}";
}
