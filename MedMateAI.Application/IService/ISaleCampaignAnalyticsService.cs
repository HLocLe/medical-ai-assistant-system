using MedMateAI.Application.DTOs.Sales.Responses;

namespace MedMateAI.Application.IService;

public interface ISaleCampaignAnalyticsService
{
    Task<SaleRevenueImpactResponse?> GetRevenueImpactAsync(
        Guid campaignId,
        CancellationToken cancellationToken = default);
}
