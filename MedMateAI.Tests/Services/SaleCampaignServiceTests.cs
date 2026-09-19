using System.Linq.Expressions;
using MedMateAI.Application.DTOs.Sales.Requests;
using MedMateAI.Application.Service;
using MedMateAI.Domain.Common;
using MedMateAI.Domain.Entities;
using MedMateAI.Domain.Enums;
using MedMateAI.Domain.Persistence;
using MedMateAI.Domain.Repository;
using MedMateAI.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace MedMateAI.Tests.Services;

[TestFixture]
public sealed class SaleCampaignServiceTests
{
    [Test]
    public async Task UpdateAsync_ExistingCampaignAddsPlan_TracksNewPlanAsAdded()
    {
        var campaignId = Guid.NewGuid();
        var planA = CreatePlan("Plan A", 100_000m);
        var planB = CreatePlan("Plan B", 200_000m);
        var existingPlanId = Guid.NewGuid();
        var existingCampaignPlan = new SaleCampaignPlan
        {
            Id = existingPlanId,
            SaleCampaignId = campaignId,
            PlanId = planA.Id,
            Plan = planA,
            SalePrice = 90_000m,
            IsActive = true,
            CreatedAt = DateTime.UtcNow.AddDays(-1)
        };
        var campaign = new SaleCampaign
        {
            Id = campaignId,
            Name = "Campaign",
            StartAt = DateTime.UtcNow.AddDays(-1),
            EndAt = DateTime.UtcNow.AddDays(5),
            EligibilityType = SaleCampaignEligibilityType.All,
            IsActive = true,
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            CampaignPlans = new List<SaleCampaignPlan> { existingCampaignPlan }
        };

        var contextOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=state_probe;Username=probe;Password=probe")
            .Options;
        await using var context = new ApplicationDbContext(contextOptions);
        context.Attach(campaign);

        var campaignRepository = new Mock<ISaleCampaignRepository>();
        var redemptionRepository = new Mock<ISaleRedemptionRepository>();
        var planRepository = new Mock<IGenericRepository<SubscriptionPlan>>();
        var unitOfWork = new Mock<IUnitOfWork>();
        var quotaRepository = new Mock<ISubscriptionPlanQuotaRepository>();

        unitOfWork.Setup(current => current.SaleCampaigns)
            .Returns(campaignRepository.Object);
        unitOfWork.Setup(current => current.SaleRedemptions)
            .Returns(redemptionRepository.Object);
        unitOfWork.Setup(current => current.SubscriptionPlans)
            .Returns(planRepository.Object);
        unitOfWork.Setup(current => current.BeginTransactionAsync(
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        unitOfWork.Setup(current => current.CommitTransactionAsync(
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        unitOfWork.Setup(current => current.RollbackTransactionAsync(
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        planRepository.Setup(repository => repository.GetAllAsync(
                It.IsAny<Expression<Func<SubscriptionPlan, bool>>>(),
                It.IsAny<Func<IQueryable<SubscriptionPlan>, IOrderedQueryable<SubscriptionPlan>>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { planA, planB });
        campaignRepository.Setup(repository => repository.GetByIdForUpdateAsync(
                campaignId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(campaign);
        campaignRepository.Setup(repository => repository.GetByIdWithDetailsAsync(
                campaignId,
                true,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(campaign);
        redemptionRepository.Setup(repository => repository.GetOccupancyAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, SaleRedemptionOccupancy>());
        redemptionRepository.Setup(repository => repository.GetHighestUserOccupiedCountAsync(
                campaignId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        EntityState? newPlanStateAtSave = null;
        bool? newPlanKeySetAtSave = null;
        Guid newPlanIdAtSave = Guid.Empty;
        unitOfWork.Setup(current => current.SaveChangesAsync(
                It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                var newPlan = campaign.CampaignPlans.Single(plan => plan.PlanId == planB.Id);
                context.ChangeTracker.DetectChanges();
                var entry = context.Entry(newPlan);
                newPlanStateAtSave = entry.State;
                newPlanKeySetAtSave = entry.IsKeySet;
                newPlanIdAtSave = newPlan.Id;
                newPlan.Plan = planB;
            })
            .ReturnsAsync(1);

        var request = new UpsertSaleCampaignRequest
        {
            Name = "Campaign updated",
            StartAt = DateTime.UtcNow.AddDays(-1),
            EndAt = DateTime.UtcNow.AddDays(5),
            EligibilityType = SaleCampaignEligibilityType.All,
            Priority = 1,
            IsActive = true,
            Plans = new[]
            {
                new SaleCampaignPlanRequest
                {
                    PlanId = planA.Id,
                    SalePrice = 80_000m,
                    IsActive = true
                },
                new SaleCampaignPlanRequest
                {
                    PlanId = planB.Id,
                    SalePrice = 150_000m,
                    IsActive = true
                }
            }
        };

        var service = new SaleCampaignService(
            unitOfWork.Object,
            quotaRepository.Object);
        var result = await service.UpdateAsync(campaignId, request);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(newPlanStateAtSave, Is.EqualTo(EntityState.Added));
            Assert.That(newPlanKeySetAtSave, Is.True);
            Assert.That(newPlanIdAtSave, Is.Not.EqualTo(Guid.Empty));
            Assert.That(existingCampaignPlan.Id, Is.EqualTo(existingPlanId));
            Assert.That(existingCampaignPlan.SalePrice, Is.EqualTo(80_000m));
            Assert.That(
                campaign.CampaignPlans.Count(plan => plan.PlanId == planB.Id),
                Is.EqualTo(1));
        });
        unitOfWork.Verify(current => current.SaveChangesAsync(
            It.IsAny<CancellationToken>()), Times.Once);
        unitOfWork.Verify(current => current.CommitTransactionAsync(
            It.IsAny<CancellationToken>()), Times.Once);
        unitOfWork.Verify(current => current.RollbackTransactionAsync(
            It.IsAny<CancellationToken>()), Times.Never);
    }

    private static SubscriptionPlan CreatePlan(string name, decimal price)
    {
        return new SubscriptionPlan
        {
            Id = Guid.NewGuid(),
            PlanName = name,
            Price = price,
            IsActive = true,
            CreatedAt = DateTime.UtcNow.AddDays(-2)
        };
    }
}
