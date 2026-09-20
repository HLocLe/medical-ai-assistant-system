using MedMateAI.Application.Models.Sales;

namespace MedMateAI.Application.IRepository;

public interface ISaleCampaignAnalyticsRepository
{
    Task<SaleRevenueImpactCampaignData?> GetCampaignAsync(
        Guid campaignId,
        CancellationToken cancellationToken = default);

    Task<SaleRevenueWindowSummaryData> GetPaidRevenueWindowSummaryAsync(
        SaleRevenueWindowQuery window,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SaleRevenueDailyData>> GetPaidRevenueDailyAsync(
        Guid campaignId,
        SaleRevenueWindowQuery window,
        CancellationToken cancellationToken = default);

    Task<SaleCampaignPaidSummaryData> GetCampaignPaidSummaryAsync(
        Guid campaignId,
        CancellationToken cancellationToken = default);
}
