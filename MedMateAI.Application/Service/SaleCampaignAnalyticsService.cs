using MedMateAI.Application.DTOs.Sales.Responses;
using MedMateAI.Application.IRepository;
using MedMateAI.Application.IService;
using MedMateAI.Application.Models.Sales;

namespace MedMateAI.Application.Service;

public sealed class SaleCampaignAnalyticsService : ISaleCampaignAnalyticsService
{
    private const string NotStartedStatus = "NotStarted";
    private const string ActiveStatus = "Active";
    private const string EndedStatus = "Ended";

    private readonly ISaleCampaignAnalyticsRepository _repository;
    private readonly TimeProvider _timeProvider;

    public SaleCampaignAnalyticsService(
        ISaleCampaignAnalyticsRepository repository,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _timeProvider = timeProvider;
    }

    public async Task<SaleRevenueImpactResponse?> GetRevenueImpactAsync(
        Guid campaignId,
        CancellationToken cancellationToken = default)
    {
        if (campaignId == Guid.Empty)
        {
            return null;
        }

        var campaign = await _repository.GetCampaignAsync(
            campaignId,
            cancellationToken);
        if (campaign is null)
        {
            return null;
        }

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var startAt = NormalizeUtc(campaign.StartAt);
        var endAt = NormalizeUtc(campaign.EndAt);
        var status = GetCampaignStatus(startAt, endAt, utcNow);
        var windows = BuildWindows(startAt, endAt, utcNow, status);

        var campaignSummary = await _repository.GetCampaignPaidSummaryAsync(
            campaignId,
            cancellationToken);
        var windowSummary = SaleRevenueWindowSummaryData.Empty;
        IReadOnlyList<SaleRevenueDailyData> daily =
            Array.Empty<SaleRevenueDailyData>();

        if (status != NotStartedStatus)
        {
            var queryWindow = new SaleRevenueWindowQuery(
                windows.BeforeStart!.Value,
                windows.BeforeEnd!.Value,
                windows.DuringStart!.Value,
                windows.DuringEnd!.Value,
                windows.AfterStart,
                windows.AfterEnd);
            windowSummary = await _repository.GetPaidRevenueWindowSummaryAsync(
                queryWindow,
                cancellationToken);

            daily = await _repository.GetPaidRevenueDailyAsync(
                campaignId,
                queryWindow,
                cancellationToken);
        }

        return new SaleRevenueImpactResponse
        {
            CampaignId = campaign.CampaignId,
            CampaignName = campaign.CampaignName,
            BadgeText = campaign.BadgeText,
            StartAt = startAt,
            EndAt = endAt,
            CampaignStatus = status,
            Windows = windows,
            Metrics = MapMetrics(windowSummary, campaignSummary),
            Series = status == NotStartedStatus
                ? Array.Empty<SaleRevenueImpactDailyResponse>()
                : BuildSeries(daily, windows)
        };
    }

    private static SaleRevenueImpactWindowResponse BuildWindows(
        DateTime startAt,
        DateTime endAt,
        DateTime utcNow,
        string status)
    {
        if (status == NotStartedStatus)
        {
            return new SaleRevenueImpactWindowResponse
            {
                IsCampaignNotStarted = true
            };
        }

        if (status == ActiveStatus)
        {
            var elapsed = utcNow - startAt;
            return new SaleRevenueImpactWindowResponse
            {
                BeforeStart = startAt - elapsed,
                BeforeEnd = startAt,
                DuringStart = startAt,
                DuringEnd = utcNow,
                IsCampaignActive = true
            };
        }

        var duration = endAt - startAt;
        var configuredAfterEnd = endAt + duration;
        var effectiveAfterEnd = configuredAfterEnd <= utcNow
            ? configuredAfterEnd
            : utcNow;
        return new SaleRevenueImpactWindowResponse
        {
            BeforeStart = startAt - duration,
            BeforeEnd = startAt,
            DuringStart = startAt,
            DuringEnd = endAt,
            AfterStart = endAt,
            AfterEnd = effectiveAfterEnd,
            IsAfterPeriodPartial = effectiveAfterEnd < configuredAfterEnd,
            IsCampaignEnded = true
        };
    }

    private static SaleRevenueImpactMetricsResponse MapMetrics(
        SaleRevenueWindowSummaryData window,
        SaleCampaignPaidSummaryData campaign)
    {
        var revenueChange = window.RevenueDuring - window.RevenueBefore;
        return new SaleRevenueImpactMetricsResponse
        {
            RevenueBefore = window.RevenueBefore,
            RevenueDuring = window.RevenueDuring,
            RevenueAfter = window.RevenueAfter,
            PaidOrdersBefore = window.PaidOrdersBefore,
            PaidOrdersDuring = window.PaidOrdersDuring,
            PaidOrdersAfter = window.PaidOrdersAfter,
            RevenueChangeAmount = revenueChange,
            RevenueChangePercent = window.RevenueBefore == 0
                ? null
                : RoundMoney(revenueChange / window.RevenueBefore * 100),
            CampaignRevenue = campaign.CampaignRevenue,
            CampaignPaidOrders = campaign.CampaignPaidOrders,
            AverageCampaignOrderValue = campaign.CampaignPaidOrders == 0
                ? 0
                : RoundMoney(
                    campaign.CampaignRevenue / campaign.CampaignPaidOrders),
            CampaignDiscountAmount = campaign.CampaignDiscountAmount,
            CampaignBonusCreditGranted = campaign.CampaignBonusCreditGranted
        };
    }

    private static IReadOnlyList<SaleRevenueImpactDailyResponse> BuildSeries(
        IReadOnlyList<SaleRevenueDailyData> aggregates,
        SaleRevenueImpactWindowResponse windows)
    {
        var byPeriodAndDate = aggregates.ToDictionary(item =>
            (item.Period, item.Date));
        var series = new List<SaleRevenueImpactDailyResponse>();
        AddPeriodSeries(
            series,
            byPeriodAndDate,
            windows.BeforeStart!.Value,
            windows.BeforeEnd!.Value,
            SaleRevenuePeriod.Before);
        AddPeriodSeries(
            series,
            byPeriodAndDate,
            windows.DuringStart!.Value,
            windows.DuringEnd!.Value,
            SaleRevenuePeriod.During);

        if (windows.AfterStart.HasValue && windows.AfterEnd.HasValue)
        {
            AddPeriodSeries(
                series,
                byPeriodAndDate,
                windows.AfterStart.Value,
                windows.AfterEnd.Value,
                SaleRevenuePeriod.After);
        }

        return series;
    }

    private static void AddPeriodSeries(
        ICollection<SaleRevenueImpactDailyResponse> series,
        IReadOnlyDictionary<(SaleRevenuePeriod Period, DateOnly Date),
            SaleRevenueDailyData> aggregates,
        DateTime periodStart,
        DateTime periodEnd,
        SaleRevenuePeriod period)
    {
        foreach (var date in EnumerateUtcDates(periodStart, periodEnd))
        {
            aggregates.TryGetValue((period, date), out var aggregate);
            series.Add(new SaleRevenueImpactDailyResponse
            {
                Date = date,
                TotalRevenue = aggregate?.TotalRevenue ?? 0,
                CampaignRevenue = aggregate?.CampaignRevenue ?? 0,
                PaidOrders = aggregate?.PaidOrders ?? 0,
                CampaignOrders = aggregate?.CampaignOrders ?? 0,
                Period = period.ToString()
            });
        }
    }

    private static IEnumerable<DateOnly> EnumerateUtcDates(
        DateTime startInclusive,
        DateTime endExclusive)
    {
        if (endExclusive <= startInclusive)
        {
            yield break;
        }

        var lastDate = DateOnly.FromDateTime(endExclusive.AddTicks(-1));
        for (var date = DateOnly.FromDateTime(startInclusive);
             date <= lastDate;
             date = date.AddDays(1))
        {
            yield return date;
        }
    }

    private static string GetCampaignStatus(
        DateTime startAt,
        DateTime endAt,
        DateTime utcNow)
    {
        if (utcNow < startAt)
        {
            return NotStartedStatus;
        }

        return utcNow < endAt ? ActiveStatus : EndedStatus;
    }

    private static decimal RoundMoney(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);

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
