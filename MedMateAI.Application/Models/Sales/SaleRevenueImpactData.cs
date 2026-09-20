namespace MedMateAI.Application.Models.Sales;

public sealed record SaleRevenueImpactCampaignData(
    Guid CampaignId,
    string CampaignName,
    string? BadgeText,
    DateTime StartAt,
    DateTime EndAt);

public sealed record SaleRevenueWindowQuery(
    DateTime BeforeStart,
    DateTime BeforeEnd,
    DateTime DuringStart,
    DateTime DuringEnd,
    DateTime? AfterStart,
    DateTime? AfterEnd);

public sealed record SaleRevenueWindowSummaryData(
    decimal RevenueBefore,
    decimal RevenueDuring,
    decimal RevenueAfter,
    long PaidOrdersBefore,
    long PaidOrdersDuring,
    long PaidOrdersAfter)
{
    public static SaleRevenueWindowSummaryData Empty { get; } =
        new(0, 0, 0, 0, 0, 0);
}

public sealed record SaleRevenueDailyData(
    DateOnly Date,
    decimal TotalRevenue,
    decimal CampaignRevenue,
    long PaidOrders,
    long CampaignOrders,
    SaleRevenuePeriod Period);

public enum SaleRevenuePeriod
{
    Before = 0,
    During = 1,
    After = 2
}

public sealed record SaleCampaignPaidSummaryData(
    decimal CampaignRevenue,
    long CampaignPaidOrders,
    decimal CampaignDiscountAmount,
    long CampaignBonusCreditGranted)
{
    public static SaleCampaignPaidSummaryData Empty { get; } =
        new(0, 0, 0, 0);
}
