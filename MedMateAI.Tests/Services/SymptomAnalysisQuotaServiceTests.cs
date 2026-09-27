using System.Linq.Expressions;
using MedMateAI.Application.IService;
using MedMateAI.Application.Models;
using MedMateAI.Application.Models.ServiceCredits;
using MedMateAI.Application.Service;
using MedMateAI.Domain.Entities;
using MedMateAI.Domain.Enums;
using MedMateAI.Domain.Persistence;
using MedMateAI.Domain.Repository;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace MedMateAI.Tests.Services;

[TestFixture]
public class SymptomAnalysisQuotaServiceTests
{
    private Mock<IServiceCreditService> _serviceCreditServiceMock = null!;
    private Mock<IGenericRepository<SymptomAnalysisSession>> _sessionsMock = null!;
    private Mock<IUnitOfWork> _unitOfWorkMock = null!;
    private Mock<IQuotaUsageRepository> _quotaUsageRepositoryMock = null!;
    private Mock<ILogger<SymptomAnalysisQuotaService>> _loggerMock = null!;
    private Mock<IFreeQuotaService> _freeQuotaServiceMock = null!;
    private SymptomAnalysisQuotaService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _serviceCreditServiceMock = new Mock<IServiceCreditService>();
        _sessionsMock = new Mock<IGenericRepository<SymptomAnalysisSession>>();
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _quotaUsageRepositoryMock = new Mock<IQuotaUsageRepository>();
        _loggerMock = new Mock<ILogger<SymptomAnalysisQuotaService>>();

        _unitOfWorkMock.Setup(unitOfWork => unitOfWork.QuotaUsages).Returns(_quotaUsageRepositoryMock.Object);

        _freeQuotaServiceMock = new Mock<IFreeQuotaService>();

        _service = new SymptomAnalysisQuotaService(
            _serviceCreditServiceMock.Object,
            _freeQuotaServiceMock.Object,
            _sessionsMock.Object,
            _unitOfWorkMock.Object,
            _loggerMock.Object);
    }

    [Test]
    public async Task ReserveAsync_DelegatesToServiceCreditServiceWithSymptomAnalysisReferenceAndIdempotencyKey()
    {
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var actorUserId = Guid.NewGuid();
        var utcNow = new DateTime(2026, 8, 24, 9, 0, 0, DateTimeKind.Utc);
        var usage = new UserSubscriptionUsage { Id = Guid.NewGuid() };

        _serviceCreditServiceMock.Setup(service => service.ReserveAsync(
                userId,
                "SymptomAnalysisSession",
                sessionId,
                actorUserId,
                $"symptom-analysis:reserve:{sessionId:N}",
                "Symptom analysis session service credit reserved.",
                utcNow,
                CancellationToken.None))
            .ReturnsAsync(ServiceCreditOperationResult<UserSubscriptionUsage>.Ok(usage));

        var result = await _service.ReserveAsync(userId, sessionId, actorUserId, utcNow, CancellationToken.None);

        Assert.That(result.Data, Is.SameAs(usage));
        _serviceCreditServiceMock.VerifyAll();
    }

    [Test]
    public async Task FinalizeAsync_CompletedSession_ConsumesUsage()
    {
        var session = MakeSession(SymptomAnalysisSessionStatus.Completed);
        var usage = new UserSubscriptionUsage
        {
            Id = session.UserSubscriptionUsageId!.Value,
            UserSubscriptionId = session.UserSubscriptionId!.Value,
            QuotaId = Guid.NewGuid()
        };
        SetupSession(session);
        _quotaUsageRepositoryMock.Setup(repository => repository.GetByIdAsync(usage.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(usage);
        _serviceCreditServiceMock.Setup(service => service.MutateAsync(
                usage.Id,
                usage.UserSubscriptionId,
                usage.QuotaId,
                SubscriptionQuotaActionType.Consume,
                "SymptomAnalysisSession",
                session.Id,
                session.UserId!.Value,
                $"symptom-analysis:consume:{session.Id:N}",
                "Symptom analysis session service credit consumed.",
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(QuotaMutationStatus.Applied);

        await _service.FinalizeAsync(session.Id, CancellationToken.None);

        _unitOfWorkMock.Verify(unitOfWork => unitOfWork.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        _serviceCreditServiceMock.VerifyAll();
    }

    [Test]
    public async Task FinalizeAsync_FailedSession_ReleasesUsage()
    {
        var session = MakeSession(SymptomAnalysisSessionStatus.Failed);
        var usage = new UserSubscriptionUsage
        {
            Id = session.UserSubscriptionUsageId!.Value,
            UserSubscriptionId = session.UserSubscriptionId!.Value,
            QuotaId = Guid.NewGuid()
        };
        SetupSession(session);
        _quotaUsageRepositoryMock.Setup(repository => repository.GetByIdAsync(usage.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(usage);
        _serviceCreditServiceMock.Setup(service => service.MutateAsync(
                usage.Id,
                usage.UserSubscriptionId,
                usage.QuotaId,
                SubscriptionQuotaActionType.Release,
                "SymptomAnalysisSession",
                session.Id,
                session.UserId!.Value,
                $"symptom-analysis:release:{session.Id:N}",
                "Symptom analysis session service credit released.",
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(QuotaMutationStatus.Applied);

        await _service.FinalizeAsync(session.Id, CancellationToken.None);

        _unitOfWorkMock.Verify(unitOfWork => unitOfWork.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        _serviceCreditServiceMock.VerifyAll();
    }

    [TestCase(SymptomAnalysisSessionStatus.Completed, SubscriptionQuotaActionType.Consume)]
    [TestCase(SymptomAnalysisSessionStatus.Failed, SubscriptionQuotaActionType.Release)]
    public async Task FinalizeAsync_FreeSession_FinalizesFreeQuotaAndSkipsServiceCredit(
        SymptomAnalysisSessionStatus status,
        SubscriptionQuotaActionType expectedAction)
    {
        var session = MakeFreeSession(status);
        SetupSession(session);

        await _service.FinalizeAsync(session.Id, CancellationToken.None);

        _freeQuotaServiceMock.Verify(service => service.FinalizeAsync(
                session.FreeQuotaUsageId!.Value,
                session.UserId!.Value,
                SymptomAnalysisQuotaService.FreeQuotaFeature,
                expectedAction,
                SymptomAnalysisQuotaService.ReferenceType,
                session.Id,
                It.IsAny<CancellationToken>()),
            Times.Once);
        _serviceCreditServiceMock.VerifyNoOtherCalls();
    }

    [Test]
    public async Task FinalizeAsync_FreeSessionStillProcessing_DoesNothing()
    {
        var session = MakeFreeSession(SymptomAnalysisSessionStatus.Processing);
        SetupSession(session);

        await _service.FinalizeAsync(session.Id, CancellationToken.None);

        _freeQuotaServiceMock.VerifyNoOtherCalls();
        _serviceCreditServiceMock.VerifyNoOtherCalls();
    }

    private static SymptomAnalysisSession MakeFreeSession(SymptomAnalysisSessionStatus status) =>
        new()
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            QuotaSource = QuotaSource.Free,
            FreeQuotaUsageId = Guid.NewGuid(),
            Status = status
        };

    private void SetupSession(SymptomAnalysisSession? session)
    {
        _sessionsMock.Setup(repository => repository.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<SymptomAnalysisSession, bool>>>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
    }

    private static SymptomAnalysisSession MakeSession(SymptomAnalysisSessionStatus status) =>
        new()
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            UserSubscriptionId = Guid.NewGuid(),
            UserSubscriptionUsageId = Guid.NewGuid(),
            Status = status
        };
}
