using MedMateAI.Application.DTOs.Sales.Responses;
using MedMateAI.Application.IRepository;
using MedMateAI.Application.Models.Sales;
using MedMateAI.Application.Service;
using Moq;

namespace MedMateAI.Tests.Services;

[TestFixture]
public sealed class SaleCampaignAnalyticsServiceTests
{
    private static readonly DateTime Now =
        new(2026, 9, 15, 0, 0, 0, DateTimeKind.Utc);

    private Mock<ISaleCampaignAnalyticsRepository> _repository = null!;
    private SaleCampaignAnalyticsService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _repository = new Mock<ISaleCampaignAnalyticsRepository>();
        _service = new SaleCampaignAnalyticsService(
            _repository.Object,
            new FixedTimeProvider(Now));
    }

    [Test]
    public async Task GetRevenueImpactAsync_NoPaidPayments_ReturnsZerosAndFilledDays()
    {
        SetupCompletedCampaign();
        SetupSummaries(
            SaleRevenueWindowSummaryData.Empty,
            SaleCampaignPaidSummaryData.Empty,
            Array.Empty<SaleRevenueDailyData>());

        var result = await _service.GetRevenueImpactAsync(Guid.Parse(
            "10000000-0000-0000-0000-000000000001"));

        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result!.Metrics.RevenueBefore, Is.Zero);
            Assert.That(result.Metrics.RevenueDuring, Is.Zero);
            Assert.That(result.Metrics.CampaignRevenue, Is.Zero);
            Assert.That(result.Metrics.CampaignPaidOrders, Is.Zero);
            Assert.That(result.Metrics.CampaignDiscountAmount, Is.Zero);
            Assert.That(result.Metrics.CampaignBonusCreditGranted, Is.Zero);
            Assert.That(result.Series, Has.Count.EqualTo(6));
            Assert.That(result.Series.All(day =>
                day.TotalRevenue == 0
                && day.CampaignRevenue == 0
                && day.PaidOrders == 0
                && day.CampaignOrders == 0), Is.True);
        });
    }

    [Test]
    public async Task GetRevenueImpactAsync_BeforeAndDuring_ComputesRevenueChange()
    {
        SetupCompletedCampaign();
        SetupSummaries(
            new SaleRevenueWindowSummaryData(300_000, 600_000, 0, 2, 2, 0),
            SaleCampaignPaidSummaryData.Empty,
            Array.Empty<SaleRevenueDailyData>());

        var result = await _service.GetRevenueImpactAsync(Guid.Parse(
            "10000000-0000-0000-0000-000000000001"));

        Assert.Multiple(() =>
        {
            Assert.That(result!.Metrics.RevenueBefore, Is.EqualTo(300_000));
            Assert.That(result.Metrics.RevenueDuring, Is.EqualTo(600_000));
            Assert.That(result.Metrics.RevenueChangeAmount, Is.EqualTo(300_000));
            Assert.That(result.Metrics.RevenueChangePercent, Is.EqualTo(100));
        });
    }

    [Test]
    public async Task GetRevenueImpactAsync_CampaignPaidAfterEnd_SeparatesAttributionFromDuring()
    {
        SetupCompletedCampaign();
        SetupSummaries(
            new SaleRevenueWindowSummaryData(0, 0, 120_000, 0, 0, 1),
            new SaleCampaignPaidSummaryData(120_000, 1, 30_000, 3),
            new[]
            {
                new SaleRevenueDailyData(
                    new DateOnly(2026, 9, 13),
                    120_000,
                    120_000,
                    1,
                    1,
                    SaleRevenuePeriod.After)
            });

        var result = await _service.GetRevenueImpactAsync(Guid.Parse(
            "10000000-0000-0000-0000-000000000001"));
        var delayedConversion = result!.Series.Single(day =>
            day.Date == new DateOnly(2026, 9, 13));

        Assert.Multiple(() =>
        {
            Assert.That(result.Metrics.RevenueDuring, Is.Zero);
            Assert.That(result.Metrics.RevenueAfter, Is.EqualTo(120_000));
            Assert.That(result.Metrics.CampaignRevenue, Is.EqualTo(120_000));
            Assert.That(result.Metrics.AverageCampaignOrderValue, Is.EqualTo(120_000));
            Assert.That(delayedConversion.Period, Is.EqualTo("After"));
            Assert.That(delayedConversion.CampaignRevenue, Is.EqualTo(120_000));
        });
    }

    [Test]
    public async Task GetRevenueImpactAsync_RevenueBeforeIsZero_ReturnsNullPercentage()
    {
        SetupCompletedCampaign();
        SetupSummaries(
            new SaleRevenueWindowSummaryData(0, 80_000, 0, 0, 1, 0),
            SaleCampaignPaidSummaryData.Empty,
            Array.Empty<SaleRevenueDailyData>());

        var result = await _service.GetRevenueImpactAsync(Guid.Parse(
            "10000000-0000-0000-0000-000000000001"));

        Assert.Multiple(() =>
        {
            Assert.That(result!.Metrics.RevenueChangeAmount, Is.EqualTo(80_000));
            Assert.That(result.Metrics.RevenueChangePercent, Is.Null);
        });
    }

    [Test]
    public async Task GetRevenueImpactAsync_ActiveCampaign_UsesEqualElapsedWindows()
    {
        var campaignId = Guid.NewGuid();
        var start = Now.AddDays(-2);
        SetupCampaign(campaignId, start, Now.AddDays(5));
        SetupSummaries(
            SaleRevenueWindowSummaryData.Empty,
            SaleCampaignPaidSummaryData.Empty,
            Array.Empty<SaleRevenueDailyData>());
        SaleRevenueWindowQuery? capturedWindow = null;
        _repository.Setup(repository => repository.GetPaidRevenueWindowSummaryAsync(
                It.IsAny<SaleRevenueWindowQuery>(),
                It.IsAny<CancellationToken>()))
            .Callback<SaleRevenueWindowQuery, CancellationToken>((window, _) =>
                capturedWindow = window)
            .ReturnsAsync(SaleRevenueWindowSummaryData.Empty);

        var result = await _service.GetRevenueImpactAsync(campaignId);

        Assert.Multiple(() =>
        {
            Assert.That(result!.CampaignStatus, Is.EqualTo("Active"));
            Assert.That(result.Windows.BeforeStart, Is.EqualTo(Now.AddDays(-4)));
            Assert.That(result.Windows.BeforeEnd, Is.EqualTo(start));
            Assert.That(result.Windows.DuringStart, Is.EqualTo(start));
            Assert.That(result.Windows.DuringEnd, Is.EqualTo(Now));
            Assert.That(result.Windows.AfterStart, Is.Null);
            Assert.That(capturedWindow!.DuringEnd - capturedWindow.DuringStart,
                Is.EqualTo(capturedWindow.BeforeEnd - capturedWindow.BeforeStart));
        });
    }

    [Test]
    public async Task GetRevenueImpactAsync_CompletedCampaignWithPartialAfter_ClipsAtNow()
    {
        var campaignId = Guid.NewGuid();
        SetupCampaign(campaignId, Now.AddDays(-10), Now.AddDays(-3));
        SetupSummaries(
            SaleRevenueWindowSummaryData.Empty,
            SaleCampaignPaidSummaryData.Empty,
            Array.Empty<SaleRevenueDailyData>());

        var result = await _service.GetRevenueImpactAsync(campaignId);

        Assert.Multiple(() =>
        {
            Assert.That(result!.CampaignStatus, Is.EqualTo("Ended"));
            Assert.That(result.Windows.AfterStart, Is.EqualTo(Now.AddDays(-3)));
            Assert.That(result.Windows.AfterEnd, Is.EqualTo(Now));
            Assert.That(result.Windows.IsAfterPeriodPartial, Is.True);
        });
    }

    [Test]
    public async Task GetRevenueImpactAsync_MissingDailyAggregate_ZeroFillsCalendarDate()
    {
        SetupCompletedCampaign();
        SetupSummaries(
            SaleRevenueWindowSummaryData.Empty,
            SaleCampaignPaidSummaryData.Empty,
            new[]
            {
                new SaleRevenueDailyData(
                    new DateOnly(2026, 9, 10),
                    50_000,
                    25_000,
                    2,
                    1,
                    SaleRevenuePeriod.During)
            });

        var result = await _service.GetRevenueImpactAsync(Guid.Parse(
            "10000000-0000-0000-0000-000000000001"));
        var missingDate = result!.Series.Single(day =>
            day.Date == new DateOnly(2026, 9, 11));

        Assert.Multiple(() =>
        {
            Assert.That(missingDate.TotalRevenue, Is.Zero);
            Assert.That(missingDate.CampaignRevenue, Is.Zero);
            Assert.That(missingDate.PaidOrders, Is.Zero);
            Assert.That(missingDate.CampaignOrders, Is.Zero);
            Assert.That(missingDate.Period, Is.EqualTo("During"));
        });
    }

    [Test]
    public async Task GetRevenueImpactAsync_MidDayStart_SplitsSameDateBeforeAndDuring()
    {
        UseNow(Utc(2026, 9, 30));
        var campaignId = Guid.NewGuid();
        SetupCampaign(
            campaignId,
            Utc(2026, 9, 20, 15),
            Utc(2026, 9, 22, 15));
        SetupSummaries(
            new SaleRevenueWindowSummaryData(100_000, 200_000, 0, 1, 1, 0),
            SaleCampaignPaidSummaryData.Empty,
            new[]
            {
                Daily(2026, 9, 20, 100_000, 1, SaleRevenuePeriod.Before),
                Daily(2026, 9, 20, 200_000, 1, SaleRevenuePeriod.During)
            });

        var result = await _service.GetRevenueImpactAsync(campaignId);
        var boundaryRows = result!.Series
            .Where(row => row.Date == new DateOnly(2026, 9, 20))
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(boundaryRows, Has.Count.EqualTo(2));
            Assert.That(Row(boundaryRows, "Before").TotalRevenue,
                Is.EqualTo(100_000));
            Assert.That(Row(boundaryRows, "During").TotalRevenue,
                Is.EqualTo(200_000));
        });
    }

    [Test]
    public async Task GetRevenueImpactAsync_MidDayEnd_SplitsSameDateDuringAndAfter()
    {
        UseNow(Utc(2026, 9, 30));
        var campaignId = Guid.NewGuid();
        SetupCampaign(
            campaignId,
            Utc(2026, 9, 20, 15),
            Utc(2026, 9, 22, 15));
        SetupSummaries(
            new SaleRevenueWindowSummaryData(0, 300_000, 400_000, 0, 1, 1),
            SaleCampaignPaidSummaryData.Empty,
            new[]
            {
                Daily(2026, 9, 22, 300_000, 1, SaleRevenuePeriod.During),
                Daily(2026, 9, 22, 400_000, 1, SaleRevenuePeriod.After)
            });

        var result = await _service.GetRevenueImpactAsync(campaignId);
        var boundaryRows = result!.Series
            .Where(row => row.Date == new DateOnly(2026, 9, 22))
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(boundaryRows, Has.Count.EqualTo(2));
            Assert.That(Row(boundaryRows, "During").TotalRevenue,
                Is.EqualTo(300_000));
            Assert.That(Row(boundaryRows, "After").TotalRevenue,
                Is.EqualTo(400_000));
        });
    }

    [Test]
    public async Task GetRevenueImpactAsync_SameDayCampaign_ReturnsThreePeriodRows()
    {
        UseNow(Utc(2026, 9, 22));
        var campaignId = Guid.NewGuid();
        SetupCampaign(
            campaignId,
            Utc(2026, 9, 20, 10),
            Utc(2026, 9, 20, 18));
        SetupSummaries(
            new SaleRevenueWindowSummaryData(
                100_000,
                200_000,
                300_000,
                1,
                1,
                1),
            SaleCampaignPaidSummaryData.Empty,
            new[]
            {
                Daily(2026, 9, 20, 100_000, 1, SaleRevenuePeriod.Before),
                Daily(2026, 9, 20, 200_000, 1, SaleRevenuePeriod.During),
                Daily(2026, 9, 20, 300_000, 1, SaleRevenuePeriod.After)
            });

        var result = await _service.GetRevenueImpactAsync(campaignId);
        var sameDateRows = result!.Series
            .Where(row => row.Date == new DateOnly(2026, 9, 20))
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(sameDateRows, Has.Count.EqualTo(3));
            Assert.That(Row(sameDateRows, "Before").TotalRevenue,
                Is.EqualTo(100_000));
            Assert.That(Row(sameDateRows, "During").TotalRevenue,
                Is.EqualTo(200_000));
            Assert.That(Row(sameDateRows, "After").TotalRevenue,
                Is.EqualTo(300_000));
        });
    }

    [Test]
    public async Task GetRevenueImpactAsync_MidnightEnd_DoesNotAddEndDateToDuring()
    {
        UseNow(Utc(2026, 9, 30));
        var campaignId = Guid.NewGuid();
        SetupCampaign(
            campaignId,
            Utc(2026, 9, 20),
            Utc(2026, 9, 22));
        SetupSummaries(
            SaleRevenueWindowSummaryData.Empty,
            SaleCampaignPaidSummaryData.Empty,
            Array.Empty<SaleRevenueDailyData>());

        var result = await _service.GetRevenueImpactAsync(campaignId);

        Assert.Multiple(() =>
        {
            Assert.That(result!.Series.Any(row =>
                row.Date == new DateOnly(2026, 9, 22)
                && row.Period == "During"), Is.False);
            Assert.That(result.Series.Any(row =>
                row.Date == new DateOnly(2026, 9, 22)
                && row.Period == "After"), Is.True);
            Assert.That(result.Series.Count(row => row.Period == "During"),
                Is.EqualTo(2));
        });
    }

    [Test]
    public async Task GetRevenueImpactAsync_MidDayBoundary_ZeroFillsEachPeriodDate()
    {
        UseNow(Utc(2026, 9, 30));
        var campaignId = Guid.NewGuid();
        SetupCampaign(
            campaignId,
            Utc(2026, 9, 20, 15),
            Utc(2026, 9, 22, 15));
        SetupSummaries(
            new SaleRevenueWindowSummaryData(100_000, 0, 0, 1, 0, 0),
            SaleCampaignPaidSummaryData.Empty,
            new[]
            {
                Daily(2026, 9, 20, 100_000, 1, SaleRevenuePeriod.Before)
            });

        var result = await _service.GetRevenueImpactAsync(campaignId);
        var boundaryRows = result!.Series
            .Where(row => row.Date == new DateOnly(2026, 9, 20))
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(Row(boundaryRows, "Before").TotalRevenue,
                Is.EqualTo(100_000));
            Assert.That(Row(boundaryRows, "During").TotalRevenue, Is.Zero);
            Assert.That(Row(boundaryRows, "During").PaidOrders, Is.Zero);
        });
    }

    [Test]
    public async Task GetRevenueImpactAsync_PeriodSeriesSumsMatchKpis()
    {
        UseNow(Utc(2026, 9, 30));
        var campaignId = Guid.NewGuid();
        SetupCampaign(
            campaignId,
            Utc(2026, 9, 20, 15),
            Utc(2026, 9, 22, 15));
        SetupSummaries(
            new SaleRevenueWindowSummaryData(
                150_000,
                500_000,
                400_000,
                2,
                3,
                1),
            SaleCampaignPaidSummaryData.Empty,
            new[]
            {
                Daily(2026, 9, 19, 50_000, 1, SaleRevenuePeriod.Before),
                Daily(2026, 9, 20, 100_000, 1, SaleRevenuePeriod.Before),
                Daily(2026, 9, 20, 200_000, 1, SaleRevenuePeriod.During),
                Daily(2026, 9, 21, 300_000, 2, SaleRevenuePeriod.During),
                Daily(2026, 9, 22, 400_000, 1, SaleRevenuePeriod.After)
            });

        var result = await _service.GetRevenueImpactAsync(campaignId);

        AssertPeriodMatches(
            result!,
            "Before",
            result!.Metrics.RevenueBefore,
            result.Metrics.PaidOrdersBefore);
        AssertPeriodMatches(
            result,
            "During",
            result.Metrics.RevenueDuring,
            result.Metrics.PaidOrdersDuring);
        AssertPeriodMatches(
            result,
            "After",
            result.Metrics.RevenueAfter,
            result.Metrics.PaidOrdersAfter);
    }

    [Test]
    public async Task GetRevenueImpactAsync_CampaignRevenueUsesExactPeriodBoundaries()
    {
        UseNow(Utc(2026, 9, 30));
        var campaignId = Guid.NewGuid();
        SetupCampaign(
            campaignId,
            Utc(2026, 9, 20, 15),
            Utc(2026, 9, 22, 15));
        SetupSummaries(
            new SaleRevenueWindowSummaryData(0, 200_000, 400_000, 0, 1, 1),
            new SaleCampaignPaidSummaryData(600_000, 2, 0, 0),
            new[]
            {
                new SaleRevenueDailyData(
                    new DateOnly(2026, 9, 20),
                    200_000,
                    200_000,
                    1,
                    1,
                    SaleRevenuePeriod.During),
                new SaleRevenueDailyData(
                    new DateOnly(2026, 9, 22),
                    400_000,
                    400_000,
                    1,
                    1,
                    SaleRevenuePeriod.After)
            });

        var result = await _service.GetRevenueImpactAsync(campaignId);

        Assert.Multiple(() =>
        {
            Assert.That(result!.Metrics.CampaignRevenue, Is.EqualTo(600_000));
            Assert.That(Row(result.Series, new DateOnly(2026, 9, 20), "During")
                .CampaignRevenue, Is.EqualTo(200_000));
            Assert.That(Row(result.Series, new DateOnly(2026, 9, 22), "After")
                .CampaignRevenue, Is.EqualTo(400_000));
        });
    }

    [Test]
    public async Task GetRevenueImpactAsync_FutureCampaign_ReturnsMetadataWithoutComparison()
    {
        var campaignId = Guid.NewGuid();
        SetupCampaign(campaignId, Now.AddDays(2), Now.AddDays(4));
        _repository.Setup(repository => repository.GetCampaignPaidSummaryAsync(
                campaignId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SaleCampaignPaidSummaryData(10_000, 1, 0, 0));

        var result = await _service.GetRevenueImpactAsync(campaignId);

        Assert.Multiple(() =>
        {
            Assert.That(result!.CampaignStatus, Is.EqualTo("NotStarted"));
            Assert.That(result.Windows.IsCampaignNotStarted, Is.True);
            Assert.That(result.Windows.DuringStart, Is.Null);
            Assert.That(result.Series, Is.Empty);
            Assert.That(result.Metrics.CampaignRevenue, Is.EqualTo(10_000));
        });
        _repository.Verify(repository => repository.GetPaidRevenueWindowSummaryAsync(
            It.IsAny<SaleRevenueWindowQuery>(),
            It.IsAny<CancellationToken>()), Times.Never);
        _repository.Verify(repository => repository.GetPaidRevenueDailyAsync(
            It.IsAny<Guid>(),
            It.IsAny<SaleRevenueWindowQuery>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task GetRevenueImpactAsync_CampaignNotFound_ReturnsNull()
    {
        _repository.Setup(repository => repository.GetCampaignAsync(
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((SaleRevenueImpactCampaignData?)null);

        var result = await _service.GetRevenueImpactAsync(Guid.NewGuid());

        Assert.That(result, Is.Null);
        _repository.Verify(repository => repository.GetCampaignPaidSummaryAsync(
            It.IsAny<Guid>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    private void SetupCompletedCampaign()
    {
        SetupCampaign(
            Guid.Parse("10000000-0000-0000-0000-000000000001"),
            new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 9, 12, 0, 0, 0, DateTimeKind.Utc));
    }

    private void UseNow(DateTime utcNow)
    {
        _service = new SaleCampaignAnalyticsService(
            _repository.Object,
            new FixedTimeProvider(utcNow));
    }

    private void SetupCampaign(Guid campaignId, DateTime startAt, DateTime endAt)
    {
        _repository.Setup(repository => repository.GetCampaignAsync(
                campaignId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SaleRevenueImpactCampaignData(
                campaignId,
                "Campaign",
                "SALE",
                startAt,
                endAt));
    }

    private void SetupSummaries(
        SaleRevenueWindowSummaryData window,
        SaleCampaignPaidSummaryData campaign,
        IReadOnlyList<SaleRevenueDailyData> daily)
    {
        _repository.Setup(repository => repository.GetPaidRevenueWindowSummaryAsync(
                It.IsAny<SaleRevenueWindowQuery>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(window);
        _repository.Setup(repository => repository.GetCampaignPaidSummaryAsync(
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(campaign);
        _repository.Setup(repository => repository.GetPaidRevenueDailyAsync(
                It.IsAny<Guid>(),
                It.IsAny<SaleRevenueWindowQuery>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(daily);
    }

    private static SaleRevenueDailyData Daily(
        int year,
        int month,
        int day,
        decimal revenue,
        long paidOrders,
        SaleRevenuePeriod period) =>
        new(
            new DateOnly(year, month, day),
            revenue,
            0,
            paidOrders,
            0,
            period);

    private static SaleRevenueImpactDailyResponse Row(
        IEnumerable<SaleRevenueImpactDailyResponse> rows,
        string period) => rows.Single(row => row.Period == period);

    private static SaleRevenueImpactDailyResponse Row(
        IEnumerable<SaleRevenueImpactDailyResponse> rows,
        DateOnly date,
        string period) => rows.Single(row =>
            row.Date == date && row.Period == period);

    private static void AssertPeriodMatches(
        SaleRevenueImpactResponse result,
        string period,
        decimal expectedRevenue,
        long expectedOrders)
    {
        var rows = result.Series.Where(row => row.Period == period).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(rows.Sum(row => row.TotalRevenue),
                Is.EqualTo(expectedRevenue));
            Assert.That(rows.Sum(row => row.PaidOrders),
                Is.EqualTo(expectedOrders));
        });
    }

    private static DateTime Utc(
        int year,
        int month,
        int day,
        int hour = 0) =>
        new(year, month, day, hour, 0, 0, DateTimeKind.Utc);

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTime utcNow)
        {
            _utcNow = new DateTimeOffset(utcNow);
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
