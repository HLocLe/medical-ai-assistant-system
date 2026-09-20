namespace MedMateAI.Application.DTOs.Sales.Responses;

public sealed class SaleRevenueImpactResponse
{
    public Guid CampaignId { get; set; }

    public string CampaignName { get; set; } = string.Empty;

    public string? BadgeText { get; set; }

    public DateTime StartAt { get; set; }

    public DateTime EndAt { get; set; }

    public string CampaignStatus { get; set; } = string.Empty;

    public SaleRevenueImpactWindowResponse Windows { get; set; } = new();

    public SaleRevenueImpactMetricsResponse Metrics { get; set; } = new();

    public IReadOnlyList<SaleRevenueImpactDailyResponse> Series { get; set; } =
        Array.Empty<SaleRevenueImpactDailyResponse>();
}

public sealed class SaleRevenueImpactWindowResponse
{
    public DateTime? BeforeStart { get; set; }

    public DateTime? BeforeEnd { get; set; }

    public DateTime? DuringStart { get; set; }

    public DateTime? DuringEnd { get; set; }

    public DateTime? AfterStart { get; set; }

    public DateTime? AfterEnd { get; set; }

    public bool IsAfterPeriodPartial { get; set; }

    public bool IsCampaignActive { get; set; }

    public bool IsCampaignEnded { get; set; }

    public bool IsCampaignNotStarted { get; set; }
}

public sealed class SaleRevenueImpactMetricsResponse
{
    public decimal RevenueBefore { get; set; }

    public decimal RevenueDuring { get; set; }

    public decimal RevenueAfter { get; set; }

    public long PaidOrdersBefore { get; set; }

    public long PaidOrdersDuring { get; set; }

    public long PaidOrdersAfter { get; set; }

    public decimal RevenueChangeAmount { get; set; }

    public decimal? RevenueChangePercent { get; set; }

    public decimal CampaignRevenue { get; set; }

    public long CampaignPaidOrders { get; set; }

    public decimal AverageCampaignOrderValue { get; set; }

    public decimal CampaignDiscountAmount { get; set; }

    public long CampaignBonusCreditGranted { get; set; }
}

public sealed class SaleRevenueImpactDailyResponse
{
    public DateOnly Date { get; set; }

    public decimal TotalRevenue { get; set; }

    public decimal CampaignRevenue { get; set; }

    public long PaidOrders { get; set; }

    public long CampaignOrders { get; set; }

    public string Period { get; set; } = string.Empty;
}
