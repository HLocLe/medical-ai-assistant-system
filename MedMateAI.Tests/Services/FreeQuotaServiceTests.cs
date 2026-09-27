using MedMateAI.Application.Service;
using MedMateAI.Domain.Common;
using MedMateAI.Domain.Entities;
using MedMateAI.Domain.Enums;
using MedMateAI.Domain.Persistence;
using MedMateAI.Domain.Repository;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace MedMateAI.Tests.Services;

[TestFixture]
public class FreeQuotaServiceTests
{
    private const string Feature = "symptom-analysis";
    private const string ReferenceType = "SymptomAnalysisSession";

    private Mock<IUnitOfWork> _unitOfWorkMock = null!;
    private Mock<IFreeQuotaUsageRepository> _usagesMock = null!;
    private FreeQuotaService _service = null!;

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _usageId = Guid.NewGuid();
    private readonly Guid _referenceId = Guid.NewGuid();

    [SetUp]
    public void SetUp()
    {
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _usagesMock = new Mock<IFreeQuotaUsageRepository>();
        _unitOfWorkMock.Setup(u => u.FreeQuotaUsages).Returns(_usagesMock.Object);

        _usagesMock.Setup(r => r.GetOrCreateForUpdateAsync(
                _userId, Feature, It.IsAny<DateOnly>(), 5, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FreeQuotaUsage { Id = _usageId, UserId = _userId, Feature = Feature, LimitValue = 5 });

        _service = new FreeQuotaService(_unitOfWorkMock.Object, Mock.Of<ILogger<FreeQuotaService>>());
    }

    private string Key(string action) => $"{Feature}:free:{action}:{_referenceId:N}";

    private FreeQuotaMutationResult Mutation(int usedBefore, int usedAfter, int reservedBefore, int reservedAfter, Guid? userId = null) =>
        new(_usageId, userId ?? _userId, 5, usedBefore, usedAfter, reservedBefore, reservedAfter);

    [Test]
    public async Task TryReserveAsync_Available_ReservesAndWritesReserveLog()
    {
        _usagesMock.Setup(r => r.ReserveAsync(_usageId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mutation(1, 1, 0, 1));

        var usage = await _service.TryReserveAsync(_userId, Feature, 5, ReferenceType, _referenceId, DateTime.UtcNow);

        Assert.That(usage!.Id, Is.EqualTo(_usageId));
        _usagesMock.Verify(r => r.TryInsertLogAsync(
                It.Is<FreeQuotaUsageLog>(log =>
                    log.ActionType == SubscriptionQuotaActionType.Reserve
                    && log.IdempotencyKey == Key("reserve")
                    && log.ReferenceId == _referenceId
                    && log.ReservedCountBefore == 0
                    && log.ReservedCountAfter == 1),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task TryReserveAsync_Exhausted_ReturnsNullWithoutLog()
    {
        _usagesMock.Setup(r => r.ReserveAsync(_usageId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FreeQuotaMutationResult?)null);

        var usage = await _service.TryReserveAsync(_userId, Feature, 5, ReferenceType, _referenceId, DateTime.UtcNow);

        Assert.That(usage, Is.Null);
        _usagesMock.Verify(r => r.TryInsertLogAsync(It.IsAny<FreeQuotaUsageLog>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task TryReserveAsync_AlreadyReservedForReference_IsIdempotent()
    {
        _usagesMock.Setup(r => r.HasLogAsync(Key("reserve"), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var usage = await _service.TryReserveAsync(_userId, Feature, 5, ReferenceType, _referenceId, DateTime.UtcNow);

        Assert.That(usage!.Id, Is.EqualTo(_usageId));
        _usagesMock.Verify(r => r.ReserveAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestCase(SubscriptionQuotaActionType.Consume, "consume")]
    [TestCase(SubscriptionQuotaActionType.Release, "release")]
    public async Task FinalizeAsync_Reserved_MutatesWritesLogAndCommits(SubscriptionQuotaActionType action, string keyAction)
    {
        _usagesMock.Setup(r => r.ConsumeAsync(_usageId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mutation(1, 2, 1, 0));
        _usagesMock.Setup(r => r.ReleaseAsync(_usageId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mutation(1, 1, 1, 0));

        await _service.FinalizeAsync(_usageId, _userId, Feature, action, ReferenceType, _referenceId);

        _usagesMock.Verify(r => r.AcquireIdempotencyLockAsync(Key("finalize"), It.IsAny<CancellationToken>()), Times.Once);
        _usagesMock.Verify(r => r.TryInsertLogAsync(
                It.Is<FreeQuotaUsageLog>(log => log.ActionType == action && log.IdempotencyKey == Key(keyAction)),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _usagesMock.Verify(r => r.ConsumeAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            action == SubscriptionQuotaActionType.Consume ? Times.Once() : Times.Never());
        _usagesMock.Verify(r => r.ReleaseAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            action == SubscriptionQuotaActionType.Release ? Times.Once() : Times.Never());
        _unitOfWorkMock.Verify(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestCase("consume")]
    [TestCase("release")]
    public async Task FinalizeAsync_AlreadyFinalized_SkipsMutation(string existingAction)
    {
        _usagesMock.Setup(r => r.HasLogAsync(Key(existingAction), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        await _service.FinalizeAsync(_usageId, _userId, Feature, SubscriptionQuotaActionType.Consume, ReferenceType, _referenceId);

        _usagesMock.Verify(r => r.ConsumeAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
        _usagesMock.Verify(r => r.TryInsertLogAsync(It.IsAny<FreeQuotaUsageLog>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWorkMock.Verify(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task FinalizeAsync_NothingReserved_CommitsWithoutLog()
    {
        _usagesMock.Setup(r => r.ReleaseAsync(_usageId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FreeQuotaMutationResult?)null);

        await _service.FinalizeAsync(_usageId, _userId, Feature, SubscriptionQuotaActionType.Release, ReferenceType, _referenceId);

        _usagesMock.Verify(r => r.TryInsertLogAsync(It.IsAny<FreeQuotaUsageLog>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWorkMock.Verify(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public void FinalizeAsync_UsageOfAnotherUser_RollsBackAndThrows()
    {
        _usagesMock.Setup(r => r.ConsumeAsync(_usageId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mutation(0, 1, 1, 0, userId: Guid.NewGuid()));

        Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.FinalizeAsync(_usageId, _userId, Feature, SubscriptionQuotaActionType.Consume, ReferenceType, _referenceId));

        _unitOfWorkMock.Verify(u => u.RollbackTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWorkMock.Verify(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public void FinalizeAsync_UnsupportedAction_Throws()
    {
        Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            _service.FinalizeAsync(_usageId, _userId, Feature, SubscriptionQuotaActionType.Reserve, ReferenceType, _referenceId));
    }
}
