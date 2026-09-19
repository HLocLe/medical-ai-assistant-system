using MedMateAI.Application.DTOs.Common;
using MedMateAI.Application.DTOs.Sales.Requests;
using MedMateAI.Application.DTOs.Sales.Responses;
using MedMateAI.Application.DTOs.SubscriptionPlanQuotas.Responses;
using MedMateAI.Application.DTOs.SubscriptionPlans.Responses;
using MedMateAI.Application.IService;
using MedMateAI.Application.Models.Sales;
using MedMateAI.Domain.Common;
using MedMateAI.Domain.Entities;
using MedMateAI.Domain.Enums;
using MedMateAI.Domain.Persistence;
using MedMateAI.Domain.Repository;

namespace MedMateAI.Application.Service;

public sealed class SaleCampaignService : ISaleCampaignService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISubscriptionPlanQuotaRepository _quotaRepository;

    public SaleCampaignService(
        IUnitOfWork unitOfWork,
        ISubscriptionPlanQuotaRepository quotaRepository)
    {
        _unitOfWork = unitOfWork;
        _quotaRepository = quotaRepository;
    }

    public async Task<PagedResponse<SaleCampaignResponse>> GetAdminCampaignsAsync(
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var utcNow = DateTime.UtcNow;
        var paged = await _unitOfWork.SaleCampaigns.GetAdminPagedAsync(
            pageNumber,
            pageSize,
            cancellationToken);
        return PagedResponse<SaleCampaignResponse>.From(
            paged,
            campaign => MapCampaign(campaign, utcNow));
    }

    public async Task<SaleCampaignResponse?> GetByIdAsync(
        Guid campaignId,
        CancellationToken cancellationToken = default)
    {
        if (campaignId == Guid.Empty)
        {
            return null;
        }

        var campaign = await _unitOfWork.SaleCampaigns.GetByIdWithDetailsAsync(
            campaignId,
            cancellationToken: cancellationToken);
        return campaign is null ? null : MapCampaign(campaign, DateTime.UtcNow);
    }

    public async Task<SaleCampaignResponse> CreateAsync(
        UpsertSaleCampaignRequest request,
        CancellationToken cancellationToken = default)
    {
        await ValidateRequestAsync(request, cancellationToken);
        var utcNow = DateTime.UtcNow;
        var startAt = NormalizeUtc(request.StartAt);
        var endAt = NormalizeUtc(request.EndAt);
        var campaign = new SaleCampaign
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Description = Normalize(request.Description),
            BadgeText = Normalize(request.BadgeText),
            StartAt = startAt,
            EndAt = endAt,
            EligibilityType = request.EligibilityType,
            MaxRedemptions = request.MaxRedemptions,
            MaxRedemptionsPerUser = request.MaxRedemptionsPerUser,
            Priority = request.Priority,
            IsActive = request.IsActive,
            AnnounceToUsers = request.AnnounceToUsers ?? false,
            CreatedAt = utcNow
        };

        foreach (var item in request.Plans)
        {
            campaign.CampaignPlans.Add(new SaleCampaignPlan
            {
                Id = Guid.NewGuid(),
                PlanId = item.PlanId,
                SalePrice = item.SalePrice,
                BonusCredit = item.BonusCredit,
                IsActive = item.IsActive,
                CreatedAt = utcNow
            });
        }

        _unitOfWork.SaleCampaigns.Add(campaign);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        var createdCampaign = await _unitOfWork.SaleCampaigns.GetByIdWithDetailsAsync(
            campaign.Id,
            cancellationToken: cancellationToken);
        if (createdCampaign is null)
        {
            throw new InvalidOperationException(
                "Created sale campaign could not be reloaded.");
        }

        return MapCampaign(createdCampaign, utcNow);
    }

    public async Task<SaleCampaignResponse?> UpdateAsync(
        Guid campaignId,
        UpsertSaleCampaignRequest request,
        CancellationToken cancellationToken = default)
    {
        if (campaignId == Guid.Empty)
        {
            return null;
        }

        await ValidateRequestAsync(request, cancellationToken);
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var campaign = await _unitOfWork.SaleCampaigns.GetByIdForUpdateAsync(
                campaignId,
                cancellationToken);
            if (campaign is null)
            {
                await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
                return null;
            }

            var occupancy = await _unitOfWork.SaleRedemptions.GetOccupancyAsync(
                new[] { campaignId },
                userId: null,
                cancellationToken);
            occupancy.TryGetValue(campaignId, out var campaignOccupancy);
            var occupied = campaignOccupancy?.OccupiedCount ?? 0;
            if (request.MaxRedemptions.HasValue
                && request.MaxRedemptions.Value < occupied)
            {
                throw new SaleCampaignConflictException(
                    "MaxRedemptions cannot be lower than the occupied redemption count.");
            }

            var highestUserOccupied = await _unitOfWork.SaleRedemptions
                .GetHighestUserOccupiedCountAsync(campaignId, cancellationToken);
            if (request.MaxRedemptionsPerUser.HasValue
                && request.MaxRedemptionsPerUser.Value < highestUserOccupied)
            {
                throw new SaleCampaignConflictException(
                    "MaxRedemptionsPerUser cannot be lower than an existing user's occupied count.");
            }

            var utcNow = DateTime.UtcNow;
            campaign.Name = request.Name.Trim();
            campaign.Description = Normalize(request.Description);
            campaign.BadgeText = Normalize(request.BadgeText);
            campaign.StartAt = NormalizeUtc(request.StartAt);
            campaign.EndAt = NormalizeUtc(request.EndAt);
            campaign.EligibilityType = request.EligibilityType;
            campaign.MaxRedemptions = request.MaxRedemptions;
            campaign.MaxRedemptionsPerUser = request.MaxRedemptionsPerUser;
            campaign.Priority = request.Priority;
            campaign.IsActive = request.IsActive;
            campaign.AnnounceToUsers = request.AnnounceToUsers
                ?? campaign.AnnounceToUsers;
            campaign.UpdatedAt = utcNow;
            SynchronizePlans(campaign, request.Plans, utcNow);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            throw;
        }

        return await GetByIdAsync(campaignId, cancellationToken);
    }

    public async Task<SaleCampaignResponse?> UpdateStatusAsync(
        Guid campaignId,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        if (campaignId == Guid.Empty)
        {
            return null;
        }

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var campaign = await _unitOfWork.SaleCampaigns.GetByIdForUpdateAsync(
                campaignId,
                cancellationToken);
            if (campaign is null)
            {
                await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
                return null;
            }

            campaign.IsActive = isActive;
            campaign.UpdatedAt = DateTime.UtcNow;
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            throw;
        }

        return await GetByIdAsync(campaignId, cancellationToken);
    }

    public async Task<bool> DeleteAsync(
        Guid campaignId,
        CancellationToken cancellationToken = default)
    {
        if (campaignId == Guid.Empty)
        {
            return false;
        }

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var campaign = await _unitOfWork.SaleCampaigns.GetByIdForUpdateAsync(
                campaignId,
                cancellationToken);
            if (campaign is null)
            {
                await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
                return false;
            }

            if (await _unitOfWork.SaleRedemptions.HasHistoryAsync(
                    campaignId,
                    cancellationToken))
            {
                throw new SaleCampaignConflictException(
                    "Campaigns with redemption history cannot be deleted; disable the campaign instead.");
            }

            var utcNow = DateTime.UtcNow;
            campaign.IsDeleted = true;
            campaign.IsActive = false;
            campaign.DeletedAt = utcNow;
            campaign.UpdatedAt = utcNow;
            foreach (var campaignPlan in campaign.CampaignPlans.Where(plan => !plan.IsDeleted))
            {
                campaignPlan.IsDeleted = true;
                campaignPlan.IsActive = false;
                campaignPlan.DeletedAt = utcNow;
                campaignPlan.UpdatedAt = utcNow;
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
            return true;
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<PagedResponse<SaleRedemptionResponse>?> GetRedemptionsAsync(
        Guid campaignId,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var campaign = await _unitOfWork.SaleCampaigns.GetByIdWithDetailsAsync(
            campaignId,
            cancellationToken: cancellationToken);
        if (campaign is null)
        {
            return null;
        }

        var paged = await _unitOfWork.SaleRedemptions.GetPagedByCampaignAsync(
            campaignId,
            pageNumber,
            pageSize,
            cancellationToken);
        return PagedResponse<SaleRedemptionResponse>.From(paged, MapRedemption);
    }

    public async Task<IReadOnlyList<SubscriptionPlanOfferResponse>> GetOffersAsync(
        Guid? userId,
        CancellationToken cancellationToken = default)
    {
        return await GetOffersAtAsync(userId, DateTime.UtcNow, cancellationToken);
    }

    public async Task<IReadOnlyList<SubscriptionPlanOfferResponse>> GetOffersAtAsync(
        Guid? userId,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        utcNow = NormalizeUtc(utcNow);
        var plans = (await _unitOfWork.SubscriptionPlans.GetAllAsync(cancellationToken))
            .Where(plan =>
                !plan.IsDeleted
                && plan.IsActive
                && TryConvertWholeVnd(plan.Price, out _))
            .OrderBy(plan => plan.Price)
            .ThenBy(plan => plan.PlanName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (plans.Count == 0)
        {
            return Array.Empty<SubscriptionPlanOfferResponse>();
        }

        var quotas = await _quotaRepository.ListActivePlanQuotasAsync(
            plans.Select(plan => plan.Id).ToArray(),
            cancellationToken);
        var quotasByPlan = quotas
            .GroupBy(quota => quota.PlanId)
            .ToDictionary(group => group.Key, group => group.ToList());
        var candidates = await _unitOfWork.SaleCampaigns.GetOfferCandidatesAsync(
            plans.Select(plan => plan.Id).ToArray(),
            utcNow,
            cancellationToken);
        var campaignIds = candidates
            .Select(candidate => candidate.SaleCampaignId)
            .Distinct()
            .ToArray();
        var occupancy = await _unitOfWork.SaleRedemptions.GetOccupancyAsync(
            campaignIds,
            userId,
            cancellationToken);
        var hasSuccessfulPurchase = userId.HasValue
            && await _unitOfWork.SaleRedemptions.HasSuccessfulPurchaseAsync(
                userId.Value,
                cancellationToken);
        var hasFirstPurchaseReservation = userId.HasValue
            && await _unitOfWork.SaleRedemptions.HasFirstPurchaseReservationAsync(
                userId.Value,
                cancellationToken);

        var response = new List<SubscriptionPlanOfferResponse>();
        foreach (var plan in plans)
        {
            quotasByPlan.TryGetValue(plan.Id, out var planQuotas);
            planQuotas ??= new List<SubscriptionPlanQuota>();
            var serviceCredit = planQuotas.SingleOrDefault(quota =>
                string.Equals(
                    quota.Quota.Code,
                    IServiceCreditService.QuotaCode,
                    StringComparison.Ordinal));
            if (serviceCredit is null || serviceCredit.LimitValue <= 0)
            {
                continue;
            }

            var baseCredit = serviceCredit.LimitValue;
            var offer = candidates
                .Where(candidate => candidate.PlanId == plan.Id)
                .FirstOrDefault(candidate => IsPreviewEligible(
                    candidate,
                    plan.Price,
                    baseCredit,
                    userId,
                    hasSuccessfulPurchase,
                    hasFirstPurchaseReservation,
                    occupancy));
            var bonusCredit = offer?.BonusCredit ?? 0;
            var grantedCredit = baseCredit + bonusCredit;
            var effectivePrice = offer?.SalePrice ?? plan.Price;
            response.Add(new SubscriptionPlanOfferResponse
            {
                Plan = MapPlan(plan, planQuotas),
                OriginalPrice = plan.Price,
                EffectivePrice = effectivePrice,
                BaseCredit = baseCredit,
                BonusCredit = bonusCredit,
                GrantedCredit = grantedCredit,
                Offer = offer is null
                    ? null
                    : MapOffer(
                        offer,
                        plan.Price,
                        baseCredit,
                        occupancy.GetValueOrDefault(offer.SaleCampaignId))
            });
        }

        return response;
    }

    private async Task<IReadOnlyDictionary<Guid, SubscriptionPlan>> ValidateRequestAsync(
        UpsertSaleCampaignRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ArgumentException("Campaign name is required.");
        }

        if (request.Name.Trim().Length > 150
            || Normalize(request.Description)?.Length > 1000
            || Normalize(request.BadgeText)?.Length > 80)
        {
            throw new ArgumentException("Campaign text exceeds the supported length.");
        }

        if (NormalizeUtc(request.StartAt) >= NormalizeUtc(request.EndAt))
        {
            throw new ArgumentException("EndAt must be later than StartAt.");
        }

        if (!Enum.IsDefined(request.EligibilityType))
        {
            throw new ArgumentException("EligibilityType is invalid.");
        }

        if (request.MaxRedemptions is < 1
            || request.MaxRedemptionsPerUser is < 1
            || (request.MaxRedemptions.HasValue
                && request.MaxRedemptionsPerUser.HasValue
                && request.MaxRedemptionsPerUser.Value > request.MaxRedemptions.Value))
        {
            throw new ArgumentException("Campaign redemption limits are invalid.");
        }

        if (request.Priority is < 0 or > 1000)
        {
            throw new ArgumentException("Priority must be between 0 and 1000.");
        }

        if (request.Plans is null || request.Plans.Count == 0)
        {
            throw new ArgumentException("Campaign must contain at least one plan.");
        }

        var planIds = request.Plans.Select(item => item.PlanId).ToArray();
        if (planIds.Any(id => id == Guid.Empty)
            || planIds.Distinct().Count() != planIds.Length)
        {
            throw new ArgumentException("Campaign plan identifiers are invalid or duplicated.");
        }

        var plans = await _unitOfWork.SubscriptionPlans.GetAllAsync(
            plan => planIds.Contains(plan.Id) && !plan.IsDeleted,
            cancellationToken: cancellationToken);
        var plansById = plans.ToDictionary(plan => plan.Id);
        if (plansById.Count != planIds.Length)
        {
            throw new ArgumentException("One or more subscription plans were not found.");
        }

        foreach (var item in request.Plans)
        {
            var plan = plansById[item.PlanId];
            if (item.BonusCredit < 0
                || (!item.SalePrice.HasValue && item.BonusCredit <= 0))
            {
                throw new ArgumentException(
                    "Each campaign plan must provide a sale price or positive bonus credit.");
            }

            if (item.SalePrice.HasValue
                && (!TryConvertWholeVnd(item.SalePrice.Value, out _)
                    || item.SalePrice.Value >= plan.Price))
            {
                throw new ArgumentException(
                    "SalePrice must be a positive whole VND amount below the current plan price.");
            }

            try
            {
                _ = checked(item.BonusCredit + 1);
            }
            catch (OverflowException)
            {
                throw new ArgumentException("BonusCredit is too large.");
            }
        }

        return plansById;
    }

    private static void SynchronizePlans(
        SaleCampaign campaign,
        IReadOnlyList<SaleCampaignPlanRequest> requestedPlans,
        DateTime utcNow)
    {
        var requestedIds = requestedPlans.Select(item => item.PlanId).ToHashSet();
        foreach (var existing in campaign.CampaignPlans.Where(plan =>
                     !plan.IsDeleted && !requestedIds.Contains(plan.PlanId)))
        {
            existing.IsDeleted = true;
            existing.IsActive = false;
            existing.DeletedAt = utcNow;
            existing.UpdatedAt = utcNow;
        }

        foreach (var item in requestedPlans)
        {
            var existing = campaign.CampaignPlans
                .Where(plan => plan.PlanId == item.PlanId)
                .OrderBy(plan => plan.IsDeleted)
                .ThenByDescending(plan => plan.CreatedAt)
                .FirstOrDefault();
            if (existing is null)
            {
                campaign.CampaignPlans.Add(new SaleCampaignPlan
                {
                    PlanId = item.PlanId,
                    SalePrice = item.SalePrice,
                    BonusCredit = item.BonusCredit,
                    IsActive = item.IsActive,
                    CreatedAt = utcNow
                });
                continue;
            }

            existing.SalePrice = item.SalePrice;
            existing.BonusCredit = item.BonusCredit;
            existing.IsActive = item.IsActive;
            existing.IsDeleted = false;
            existing.DeletedAt = null;
            existing.UpdatedAt = utcNow;
        }
    }

    private static bool IsPreviewEligible(
        SaleCampaignPlan candidate,
        decimal basePrice,
        int baseCredit,
        Guid? userId,
        bool hasSuccessfulPurchase,
        bool hasFirstPurchaseReservation,
        IReadOnlyDictionary<Guid, SaleRedemptionOccupancy> occupancy)
    {
        var campaign = candidate.SaleCampaign;
        if (candidate.BonusCredit < 0
            || (!candidate.SalePrice.HasValue && candidate.BonusCredit <= 0)
            || (candidate.SalePrice.HasValue
                && (candidate.SalePrice.Value >= basePrice
                    || !TryConvertWholeVnd(candidate.SalePrice.Value, out _)))
            || !CanAddCredits(baseCredit, candidate.BonusCredit))
        {
            return false;
        }

        var audienceEligible = campaign.EligibilityType switch
        {
            SaleCampaignEligibilityType.All => true,
            SaleCampaignEligibilityType.FirstPurchase =>
                userId.HasValue && !hasSuccessfulPurchase && !hasFirstPurchaseReservation,
            SaleCampaignEligibilityType.ReturningCustomer =>
                userId.HasValue && hasSuccessfulPurchase,
            _ => false
        };
        if (!audienceEligible)
        {
            return false;
        }

        occupancy.TryGetValue(campaign.Id, out var counts);
        return (!campaign.MaxRedemptions.HasValue
                || (counts?.OccupiedCount ?? 0) < campaign.MaxRedemptions.Value)
            && (!campaign.MaxRedemptionsPerUser.HasValue
                || (counts?.UserOccupiedCount ?? 0) < campaign.MaxRedemptionsPerUser.Value);
    }

    private static SaleCampaignResponse MapCampaign(
        SaleCampaign campaign,
        DateTime utcNow)
    {
        var redemptions = campaign.Redemptions.Where(redemption => !redemption.IsDeleted).ToList();
        var reserved = redemptions.Count(redemption =>
            redemption.Status == SaleRedemptionStatus.Reserved);
        var completed = redemptions.Count(redemption =>
            redemption.Status == SaleRedemptionStatus.Completed);
        var occupied = reserved + completed;
        return new SaleCampaignResponse
        {
            Id = campaign.Id,
            Name = campaign.Name,
            Description = campaign.Description,
            BadgeText = campaign.BadgeText,
            StartAt = campaign.StartAt,
            EndAt = campaign.EndAt,
            EligibilityType = campaign.EligibilityType,
            MaxRedemptions = campaign.MaxRedemptions,
            MaxRedemptionsPerUser = campaign.MaxRedemptionsPerUser,
            Priority = campaign.Priority,
            IsActive = campaign.IsActive,
            AnnounceToUsers = campaign.AnnounceToUsers,
            DisplayStatus = GetDisplayStatus(campaign, occupied, utcNow),
            OccupiedRedemptions = occupied,
            CompletedRedemptions = completed,
            ReservedRedemptions = reserved,
            RemainingRedemptions = campaign.MaxRedemptions.HasValue
                ? Math.Max(0, campaign.MaxRedemptions.Value - occupied)
                : null,
            Plans = campaign.CampaignPlans
                .Where(plan => !plan.IsDeleted)
                .OrderBy(plan => plan.Plan.Price)
                .ThenBy(plan => plan.Id)
                .Select(plan => new SaleCampaignPlanResponse
                {
                    Id = plan.Id,
                    PlanId = plan.PlanId,
                    PlanName = plan.Plan.PlanName,
                    BasePrice = plan.Plan.Price,
                    SalePrice = plan.SalePrice,
                    BonusCredit = plan.BonusCredit,
                    IsActive = plan.IsActive
                })
                .ToList(),
            CreatedAt = campaign.CreatedAt,
            UpdatedAt = campaign.UpdatedAt
        };
    }

    private static SaleOfferResponse MapOffer(
        SaleCampaignPlan offer,
        decimal originalPrice,
        int baseCredit,
        SaleRedemptionOccupancy? occupancy)
    {
        var campaign = offer.SaleCampaign;
        var effectivePrice = offer.SalePrice ?? originalPrice;
        var discountAmount = originalPrice - effectivePrice;
        return new SaleOfferResponse
        {
            OfferId = offer.Id,
            CampaignId = campaign.Id,
            CampaignName = campaign.Name,
            Description = campaign.Description,
            BadgeText = campaign.BadgeText,
            EligibilityType = campaign.EligibilityType,
            OriginalPrice = originalPrice,
            EffectivePrice = effectivePrice,
            DiscountAmount = discountAmount,
            DiscountPercent = originalPrice == 0
                ? 0
                : decimal.Round(discountAmount / originalPrice * 100, 2),
            BaseCredit = baseCredit,
            BonusCredit = offer.BonusCredit,
            GrantedCredit = checked(baseCredit + offer.BonusCredit),
            StartAt = campaign.StartAt,
            EndAt = campaign.EndAt,
            MaxRedemptions = campaign.MaxRedemptions,
            RemainingRedemptions = campaign.MaxRedemptions.HasValue
                ? Math.Max(0, campaign.MaxRedemptions.Value - (occupancy?.OccupiedCount ?? 0))
                : null,
            MaxRedemptionsPerUser = campaign.MaxRedemptionsPerUser
        };
    }

    private static SubscriptionPlanResponse MapPlan(
        SubscriptionPlan plan,
        IReadOnlyList<SubscriptionPlanQuota> quotas)
    {
        return new SubscriptionPlanResponse
        {
            Id = plan.Id,
            PlanName = plan.PlanName,
            Price = plan.Price,
            DurationInDays = plan.DurationInDays,
            FeatureLimitJson = plan.FeatureLimitJson,
            IsActive = plan.IsActive,
            Quotas = quotas.Select(quota => new SubscriptionPlanQuotaResponse
            {
                Id = quota.Id,
                PlanId = quota.PlanId,
                QuotaId = quota.QuotaId,
                QuotaCode = quota.Quota.Code,
                QuotaName = quota.Quota.Name,
                QuotaDescription = quota.Quota.Description,
                Unit = quota.Quota.Unit,
                LimitValue = quota.LimitValue,
                ResetPeriod = quota.ResetPeriod,
                IsActive = quota.IsActive,
                CreatedAt = quota.CreatedAt,
                UpdatedAt = quota.UpdatedAt
            }).ToList(),
            CreatedAt = plan.CreatedAt,
            UpdatedAt = plan.UpdatedAt
        };
    }

    private static SaleRedemptionResponse MapRedemption(SaleRedemption redemption)
    {
        return new SaleRedemptionResponse
        {
            Id = redemption.Id,
            CampaignId = redemption.SaleCampaignId,
            CampaignNameSnapshot = redemption.CampaignNameSnapshot,
            CampaignPlanId = redemption.SaleCampaignPlanId,
            UserId = redemption.UserId,
            PlanId = redemption.PlanId,
            PaymentId = redemption.PaymentId,
            UserSubscriptionId = redemption.UserSubscriptionId,
            OriginalPrice = redemption.OriginalPrice,
            FinalPrice = redemption.FinalPrice,
            BaseCredit = redemption.BaseCredit,
            BonusCredit = redemption.BonusCredit,
            GrantedCredit = redemption.GrantedCredit,
            EligibilityTypeSnapshot = redemption.EligibilityTypeSnapshot,
            Status = redemption.Status,
            ReservedAt = redemption.ReservedAt,
            CompletedAt = redemption.CompletedAt,
            ReleasedAt = redemption.ReleasedAt,
            CreatedAt = redemption.CreatedAt,
            UpdatedAt = redemption.UpdatedAt
        };
    }

    private static string GetDisplayStatus(
        SaleCampaign campaign,
        int occupied,
        DateTime utcNow)
    {
        if (!campaign.IsActive)
        {
            return "Disabled";
        }

        if (utcNow >= campaign.EndAt)
        {
            return "Ended";
        }

        if (utcNow < campaign.StartAt)
        {
            return "Scheduled";
        }

        return campaign.MaxRedemptions.HasValue
               && occupied >= campaign.MaxRedemptions.Value
            ? "SoldOut"
            : "Active";
    }

    private static bool TryConvertWholeVnd(decimal amount, out int value)
    {
        value = 0;
        if (amount <= 0
            || amount != decimal.Truncate(amount)
            || amount > int.MaxValue)
        {
            return false;
        }

        value = decimal.ToInt32(amount);
        return true;
    }

    private static bool CanAddCredits(int baseCredit, int bonusCredit)
    {
        return baseCredit > 0
            && bonusCredit >= 0
            && (long)baseCredit + bonusCredit <= int.MaxValue;
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static DateTime NormalizeUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }
}
