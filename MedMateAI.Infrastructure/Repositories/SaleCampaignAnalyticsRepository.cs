using MedMateAI.Application.IRepository;
using MedMateAI.Application.Models.Sales;
using MedMateAI.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MedMateAI.Infrastructure.Repositories;

public sealed class SaleCampaignAnalyticsRepository
    : ISaleCampaignAnalyticsRepository
{
    private readonly ApplicationDbContext _context;

    public SaleCampaignAnalyticsRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public Task<SaleRevenueImpactCampaignData?> GetCampaignAsync(
        Guid campaignId,
        CancellationToken cancellationToken = default)
    {
        return _context.SaleCampaigns
            .AsNoTracking()
            .Where(campaign =>
                campaign.Id == campaignId
                && !campaign.IsDeleted)
            .Select(campaign => new SaleRevenueImpactCampaignData(
                campaign.Id,
                campaign.Name,
                campaign.BadgeText,
                campaign.StartAt,
                campaign.EndAt))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<SaleRevenueWindowSummaryData> GetPaidRevenueWindowSummaryAsync(
        SaleRevenueWindowQuery window,
        CancellationToken cancellationToken = default)
    {
        var afterStart = window.AfterStart ?? window.DuringEnd;
        var afterEnd = window.AfterEnd ?? window.DuringEnd;
        var rangeEnd = window.AfterEnd ?? window.DuringEnd;

        var aggregate = await _context.Payments
            .AsNoTracking()
            .Where(payment =>
                !payment.IsDeleted
                && payment.Status == PaymentStatus.Paid
                && payment.PaidAt.HasValue
                && payment.PaidAt.Value >= window.BeforeStart
                && payment.PaidAt.Value < rangeEnd)
            .GroupBy(_ => 1)
            .Select(group => new SaleRevenueWindowSummaryData(
                group.Sum(payment =>
                    payment.PaidAt!.Value >= window.BeforeStart
                    && payment.PaidAt.Value < window.BeforeEnd
                        ? payment.Amount
                        : 0),
                group.Sum(payment =>
                    payment.PaidAt!.Value >= window.DuringStart
                    && payment.PaidAt.Value < window.DuringEnd
                        ? payment.Amount
                        : 0),
                group.Sum(payment =>
                    payment.PaidAt!.Value >= afterStart
                    && payment.PaidAt.Value < afterEnd
                        ? payment.Amount
                        : 0),
                group.LongCount(payment =>
                    payment.PaidAt!.Value >= window.BeforeStart
                    && payment.PaidAt.Value < window.BeforeEnd),
                group.LongCount(payment =>
                    payment.PaidAt!.Value >= window.DuringStart
                    && payment.PaidAt.Value < window.DuringEnd),
                group.LongCount(payment =>
                    payment.PaidAt!.Value >= afterStart
                    && payment.PaidAt.Value < afterEnd)))
            .SingleOrDefaultAsync(cancellationToken);

        return aggregate ?? SaleRevenueWindowSummaryData.Empty;
    }

    public async Task<IReadOnlyList<SaleRevenueDailyData>> GetPaidRevenueDailyAsync(
        Guid campaignId,
        SaleRevenueWindowQuery window,
        CancellationToken cancellationToken = default)
    {
        var daily = new List<SaleRevenueDailyData>();
        daily.AddRange(await GetPaidRevenueDailyForPeriodAsync(
            campaignId,
            window.BeforeStart,
            window.BeforeEnd,
            SaleRevenuePeriod.Before,
            cancellationToken));
        daily.AddRange(await GetPaidRevenueDailyForPeriodAsync(
            campaignId,
            window.DuringStart,
            window.DuringEnd,
            SaleRevenuePeriod.During,
            cancellationToken));

        if (window.AfterStart.HasValue && window.AfterEnd.HasValue)
        {
            daily.AddRange(await GetPaidRevenueDailyForPeriodAsync(
                campaignId,
                window.AfterStart.Value,
                window.AfterEnd.Value,
                SaleRevenuePeriod.After,
                cancellationToken));
        }

        return daily;
    }

    private async Task<IReadOnlyList<SaleRevenueDailyData>>
        GetPaidRevenueDailyForPeriodAsync(
            Guid campaignId,
            DateTime periodStart,
            DateTime periodEnd,
            SaleRevenuePeriod period,
            CancellationToken cancellationToken)
    {
        if (periodEnd <= periodStart)
        {
            return Array.Empty<SaleRevenueDailyData>();
        }

        var totals = await _context.Payments
            .AsNoTracking()
            .Where(payment =>
                !payment.IsDeleted
                && payment.Status == PaymentStatus.Paid
                && payment.PaidAt.HasValue
                && payment.PaidAt.Value >= periodStart
                && payment.PaidAt.Value < periodEnd)
            .GroupBy(payment => payment.PaidAt!.Value.Date)
            .Select(group => new TotalRevenueDailyProjection(
                group.Key,
                group.Sum(payment => payment.Amount),
                group.LongCount()))
            .OrderBy(item => item.Date)
            .ToListAsync(cancellationToken);
        var campaignTotals = await _context.SaleRedemptions
            .AsNoTracking()
            .Where(redemption =>
                !redemption.IsDeleted
                && redemption.Status == SaleRedemptionStatus.Completed
                && redemption.SaleCampaignId == campaignId
                && !redemption.Payment.IsDeleted
                && redemption.Payment.Status == PaymentStatus.Paid
                && redemption.Payment.PaidAt.HasValue
                && redemption.Payment.PaidAt.Value >= periodStart
                && redemption.Payment.PaidAt.Value < periodEnd)
            .GroupBy(redemption => redemption.Payment.PaidAt!.Value.Date)
            .Select(group => new CampaignRevenueDailyProjection(
                group.Key,
                group.Sum(redemption => redemption.Payment.Amount),
                group.LongCount()))
            .OrderBy(item => item.Date)
            .ToListAsync(cancellationToken);
        var campaignByDate = campaignTotals.ToDictionary(item => item.Date);

        return totals
            .Select(total =>
            {
                campaignByDate.TryGetValue(total.Date, out var campaign);
                return new SaleRevenueDailyData(
                    DateOnly.FromDateTime(total.Date),
                    total.TotalRevenue,
                    campaign?.CampaignRevenue ?? 0,
                    total.PaidOrders,
                    campaign?.CampaignOrders ?? 0,
                    period);
            })
            .ToList();
    }

    public async Task<SaleCampaignPaidSummaryData> GetCampaignPaidSummaryAsync(
        Guid campaignId,
        CancellationToken cancellationToken = default)
    {
        var aggregate = await _context.SaleRedemptions
            .AsNoTracking()
            .Where(redemption =>
                !redemption.IsDeleted
                && redemption.SaleCampaignId == campaignId
                && redemption.Status == SaleRedemptionStatus.Completed
                && !redemption.Payment.IsDeleted
                && redemption.Payment.Status == PaymentStatus.Paid
                && redemption.Payment.PaidAt.HasValue)
            .GroupBy(_ => 1)
            .Select(group => new SaleCampaignPaidSummaryData(
                group.Sum(redemption => redemption.Payment.Amount),
                group.LongCount(),
                group.Sum(redemption =>
                    redemption.OriginalPrice > redemption.FinalPrice
                        ? redemption.OriginalPrice - redemption.FinalPrice
                        : 0),
                group.Sum(redemption => (long)redemption.BonusCredit)))
            .SingleOrDefaultAsync(cancellationToken);

        return aggregate ?? SaleCampaignPaidSummaryData.Empty;
    }

    private sealed record TotalRevenueDailyProjection(
        DateTime Date,
        decimal TotalRevenue,
        long PaidOrders);

    private sealed record CampaignRevenueDailyProjection(
        DateTime Date,
        decimal CampaignRevenue,
        long CampaignOrders);
}
