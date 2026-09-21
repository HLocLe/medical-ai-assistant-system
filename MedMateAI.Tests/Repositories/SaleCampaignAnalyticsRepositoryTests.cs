using MedMateAI.Application.Models.Sales;
using MedMateAI.Domain.Entities;
using MedMateAI.Domain.Enums;
using MedMateAI.Infrastructure;
using MedMateAI.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace MedMateAI.Tests.Repositories;

[TestFixture]
public sealed class SaleCampaignAnalyticsRepositoryTests
{
    private ApplicationDbContext _context = null!;
    private SaleCampaignAnalyticsRepository _repository = null!;

    [SetUp]
    public void SetUp()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"sale-revenue-{Guid.NewGuid():N}")
            .Options;
        _context = new ApplicationDbContext(options);
        _repository = new SaleCampaignAnalyticsRepository(_context);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _context.DisposeAsync();
    }

    [Test]
    public async Task GetPaidRevenueWindowSummaryAsync_UsesPaidAtAndExcludesNonPaid()
    {
        var duringPaid = CreatePayment(
            100_000,
            PaymentStatus.Paid,
            createdAt: Utc(2026, 9, 1),
            paidAt: Utc(2026, 9, 11));
        var pending = CreatePayment(
            500_000,
            PaymentStatus.Pending,
            createdAt: Utc(2026, 9, 11),
            paidAt: null);
        var cancelled = CreatePayment(
            400_000,
            PaymentStatus.Cancelled,
            createdAt: Utc(2026, 9, 11),
            paidAt: Utc(2026, 9, 11));
        _context.Payments.AddRange(duringPaid, pending, cancelled);
        await _context.SaveChangesAsync();

        var result = await _repository.GetPaidRevenueWindowSummaryAsync(
            new SaleRevenueWindowQuery(
                Utc(2026, 9, 8),
                Utc(2026, 9, 10),
                Utc(2026, 9, 10),
                Utc(2026, 9, 12),
                Utc(2026, 9, 12),
                Utc(2026, 9, 14)));

        Assert.Multiple(() =>
        {
            Assert.That(result.RevenueBefore, Is.Zero);
            Assert.That(result.PaidOrdersBefore, Is.Zero);
            Assert.That(result.RevenueDuring, Is.EqualTo(100_000));
            Assert.That(result.PaidOrdersDuring, Is.EqualTo(1));
            Assert.That(result.RevenueAfter, Is.Zero);
        });
    }

    [Test]
    public async Task GetCampaignPaidSummaryAsync_UsesImmutableSnapshotsAndCompletedPaidOnly()
    {
        var campaignId = Guid.NewGuid();
        AddRedemption(campaignId, 20_000, 20_000, 4, PaymentStatus.Paid,
            SaleRedemptionStatus.Completed);
        AddRedemption(campaignId, 20_000, 12_000, 0, PaymentStatus.Paid,
            SaleRedemptionStatus.Completed);
        AddRedemption(campaignId, 20_000, 12_000, 3, PaymentStatus.Paid,
            SaleRedemptionStatus.Completed);
        AddRedemption(campaignId, 20_000, 9_000, 5, PaymentStatus.Paid,
            SaleRedemptionStatus.Released);
        AddRedemption(campaignId, 20_000, 10_000, 2, PaymentStatus.Pending,
            SaleRedemptionStatus.Reserved);
        await _context.SaveChangesAsync();

        var result = await _repository.GetCampaignPaidSummaryAsync(campaignId);

        Assert.Multiple(() =>
        {
            Assert.That(result.CampaignRevenue, Is.EqualTo(44_000));
            Assert.That(result.CampaignPaidOrders, Is.EqualTo(3));
            Assert.That(result.CampaignDiscountAmount, Is.EqualTo(16_000));
            Assert.That(result.CampaignBonusCreditGranted, Is.EqualTo(7));
        });
    }

    [Test]
    public async Task PaidAfterCampaignEnd_AppearsInAttributionAndAfterWindow()
    {
        var campaignId = Guid.NewGuid();
        AddRedemption(
            campaignId,
            150_000,
            120_000,
            3,
            PaymentStatus.Paid,
            SaleRedemptionStatus.Completed,
            Utc(2026, 9, 13));
        await _context.SaveChangesAsync();

        var window = await _repository.GetPaidRevenueWindowSummaryAsync(
            new SaleRevenueWindowQuery(
                Utc(2026, 9, 8),
                Utc(2026, 9, 10),
                Utc(2026, 9, 10),
                Utc(2026, 9, 12),
                Utc(2026, 9, 12),
                Utc(2026, 9, 14)));
        var campaign = await _repository.GetCampaignPaidSummaryAsync(campaignId);
        Assert.Multiple(() =>
        {
            Assert.That(window.RevenueDuring, Is.Zero);
            Assert.That(window.RevenueAfter, Is.EqualTo(120_000));
            Assert.That(campaign.CampaignRevenue, Is.EqualTo(120_000));
        });
    }

    [Test]
    public async Task GetPaidRevenueWindowSummaryAsync_MidDayBoundariesUseExactPaidAt()
    {
        _context.Payments.AddRange(
            CreatePayment(
                100_000,
                PaymentStatus.Paid,
                Utc(2026, 9, 20, 8),
                Utc(2026, 9, 20, 9)),
            CreatePayment(
                200_000,
                PaymentStatus.Paid,
                Utc(2026, 9, 20, 8),
                Utc(2026, 9, 20, 18)),
            CreatePayment(
                300_000,
                PaymentStatus.Paid,
                Utc(2026, 9, 22, 8),
                Utc(2026, 9, 22, 10)),
            CreatePayment(
                400_000,
                PaymentStatus.Paid,
                Utc(2026, 9, 22, 8),
                Utc(2026, 9, 22, 20)));
        await _context.SaveChangesAsync();

        var result = await _repository.GetPaidRevenueWindowSummaryAsync(
            new SaleRevenueWindowQuery(
                Utc(2026, 9, 18, 15),
                Utc(2026, 9, 20, 15),
                Utc(2026, 9, 20, 15),
                Utc(2026, 9, 22, 15),
                Utc(2026, 9, 22, 15),
                Utc(2026, 9, 24, 15)));

        Assert.Multiple(() =>
        {
            Assert.That(result.RevenueBefore, Is.EqualTo(100_000));
            Assert.That(result.RevenueDuring, Is.EqualTo(500_000));
            Assert.That(result.RevenueAfter, Is.EqualTo(400_000));
            Assert.That(result.PaidOrdersBefore, Is.EqualTo(1));
            Assert.That(result.PaidOrdersDuring, Is.EqualTo(2));
            Assert.That(result.PaidOrdersAfter, Is.EqualTo(1));
        });
    }

    [Test]
    public void DailyQueries_NpgsqlProvider_TranslateWithoutDatabase()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=translation_probe;"
                + "Username=probe;Password=probe")
            .Options;
        using var context = new ApplicationDbContext(options);
        var repository = new SaleCampaignAnalyticsRepository(context);

        var totalSql = repository.BuildTotalRevenueDailyQuery(
                Utc(2026, 9, 20, 15),
                Utc(2026, 9, 22, 15))
            .ToQueryString();
        var campaignSql = repository.BuildCampaignRevenueDailyQuery(
                Guid.NewGuid(),
                Utc(2026, 9, 20, 15),
                Utc(2026, 9, 22, 15))
            .ToQueryString();

        Assert.Multiple(() =>
        {
            Assert.That(totalSql, Is.Not.Empty);
            Assert.That(totalSql, Does.Contain("GROUP BY"));
            Assert.That(totalSql, Does.Contain("ORDER BY"));
            Assert.That(campaignSql, Is.Not.Empty);
            Assert.That(campaignSql, Does.Contain("GROUP BY"));
            Assert.That(campaignSql, Does.Contain("ORDER BY"));
        });
    }

    private void AddRedemption(
        Guid campaignId,
        decimal originalPrice,
        decimal finalPrice,
        int bonusCredit,
        PaymentStatus paymentStatus,
        SaleRedemptionStatus redemptionStatus,
        DateTime? paidAt = null)
    {
        var payment = CreatePayment(
            finalPrice,
            paymentStatus,
            Utc(2026, 9, 10),
            paidAt ?? (paymentStatus == PaymentStatus.Paid
                ? Utc(2026, 9, 11)
                : null));
        var redemption = new SaleRedemption
        {
            Id = Guid.NewGuid(),
            SaleCampaignId = campaignId,
            SaleCampaignPlanId = Guid.NewGuid(),
            UserId = payment.UserId,
            PlanId = Guid.NewGuid(),
            UserSubscriptionId = payment.UserSubscriptionId,
            PaymentId = payment.Id,
            CampaignNameSnapshot = "Campaign",
            EligibilityTypeSnapshot = SaleCampaignEligibilityType.All,
            OriginalPrice = originalPrice,
            FinalPrice = finalPrice,
            BaseCredit = 2,
            BonusCredit = bonusCredit,
            GrantedCredit = 2 + bonusCredit,
            Status = redemptionStatus,
            ReservedAt = Utc(2026, 9, 10),
            CompletedAt = redemptionStatus == SaleRedemptionStatus.Completed
                ? payment.PaidAt
                : null,
            ReleasedAt = redemptionStatus == SaleRedemptionStatus.Released
                ? Utc(2026, 9, 11)
                : null,
            Payment = payment
        };
        payment.SaleRedemption = redemption;
        _context.Payments.Add(payment);
        _context.SaleRedemptions.Add(redemption);
    }

    private static Payment CreatePayment(
        decimal amount,
        PaymentStatus status,
        DateTime createdAt,
        DateTime? paidAt)
    {
        return new Payment
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            UserSubscriptionId = Guid.NewGuid(),
            Amount = amount,
            Status = status,
            PaidAt = paidAt,
            CreatedAt = createdAt
        };
    }

    private static DateTime Utc(
        int year,
        int month,
        int day,
        int hour = 0) =>
        new(year, month, day, hour, 0, 0, DateTimeKind.Utc);

}
