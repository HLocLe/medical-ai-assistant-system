using System.Linq.Expressions;
using MedMateAI.Application.Common;
using MedMateAI.Application.DTOs.AIConfigs.Responses;
using MedMateAI.Application.DTOs.Common;
using MedMateAI.Application.DTOs.LabTests.Requests;
using MedMateAI.Application.DTOs.LabTests.Responses;
using MedMateAI.Application.DTOs.WebChatbot.Requests;
using MedMateAI.Application.DTOs.WebChatbot.Responses;
using MedMateAI.Application.IService;
using MedMateAI.Application.Models.ServiceCredits;
using MedMateAI.Application.Service;
using MedMateAI.Domain.Common;
using MedMateAI.Domain.Entities;
using MedMateAI.Domain.Enums;
using MedMateAI.Domain.Persistence;
using MedMateAI.Domain.Repository;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

namespace MedMateAI.Tests.Services;

[TestFixture]
public class LabTestServiceTests
{
    private Mock<IUnitOfWork> _unitOfWorkMock = null!;
    private Mock<IGenericRepository<LabTestSession>> _sessionsMock = null!;
    private Mock<ILabTestSessionRepository> _sessionDetailsMock = null!;
    private Mock<IGenericRepository<LabTestOcrExtract>> _ocrExtractsMock = null!;
    private Mock<ILabTestJobScheduler> _schedulerMock = null!;
    private Mock<ILabTestResultAnalyzer> _analyzerMock = null!;
    private Mock<ILabTestQuotaService> _quotaServiceMock = null!;
    private Mock<IAIConfigService> _aiConfigServiceMock = null!;
    private Mock<IAIChatProvider> _aiChatProviderMock = null!;
    private Mock<IRecoveryPlanRequestRepository> _recoveryPlanRequestsMock = null!;
    private LabTestService _service = null!;
    private readonly Guid _userId = Guid.NewGuid();

    [SetUp]
    public void SetUp()
    {
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _sessionsMock = new Mock<IGenericRepository<LabTestSession>>();
        _sessionDetailsMock = new Mock<ILabTestSessionRepository>();
        _ocrExtractsMock = new Mock<IGenericRepository<LabTestOcrExtract>>();
        _schedulerMock = new Mock<ILabTestJobScheduler>();
        _analyzerMock = new Mock<ILabTestResultAnalyzer>();
        _quotaServiceMock = new Mock<ILabTestQuotaService>();
        _aiConfigServiceMock = new Mock<IAIConfigService>();
        _aiChatProviderMock = new Mock<IAIChatProvider>();
        _recoveryPlanRequestsMock = new Mock<IRecoveryPlanRequestRepository>();

        _unitOfWorkMock.Setup(u => u.RecoveryPlanRequests).Returns(_recoveryPlanRequestsMock.Object);
        _unitOfWorkMock.Setup(u => u.LabTestSessions).Returns(_sessionsMock.Object);
        _unitOfWorkMock.Setup(u => u.LabTestSessionDetails).Returns(_sessionDetailsMock.Object);
        _unitOfWorkMock.Setup(u => u.LabTestOcrExtracts).Returns(_ocrExtractsMock.Object);

        _unitOfWorkMock.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _service = new LabTestService(
            _unitOfWorkMock.Object,
            _schedulerMock.Object,
            _analyzerMock.Object,
            _quotaServiceMock.Object,
            _aiConfigServiceMock.Object,
            _aiChatProviderMock.Object,
            NullLogger<LabTestService>.Instance);
    }

    // â”€â”€ AnalyzeFromDocumentUrlAsync â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Test]
    [Category("B")]
    public async Task AnalyzeFromDocumentUrlAsync_EmptyUserId_ReturnsError()
    {
        // Arrange
        var req = new LabTestAnalyzeRequest { DocumentUrl = "http://example.com/test.png" };

        // Act
        var result = await _service.AnalyzeFromDocumentUrlAsync(Guid.Empty, req);

        // Assert
        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.Errors, Contains.Item("Id người dùng là bắt buộc"));
    }

    [TestCase("http://example.com/test.txt")]
    [TestCase("http://example.com/test")]
    [TestCase("   ")]
    [Category("A")]
    public async Task AnalyzeFromDocumentUrlAsync_InvalidDocumentUrl_ReturnsErrors(string url)
    {
        // Arrange
        var req = new LabTestAnalyzeRequest { DocumentUrl = url };

        // Act
        var result = await _service.AnalyzeFromDocumentUrlAsync(_userId, req);

        // Assert
        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.Errors, Is.Not.Empty);
    }

    [TestCase(-1)]
    [TestCase(151)]
    [Category("B")]
    public async Task AnalyzeFromDocumentUrlAsync_InvalidPatientAge_ReturnsError(int age)
    {
        // Arrange
        var req = new LabTestAnalyzeRequest
        {
            DocumentUrl = "http://example.com/test.png",
            PatientAgeAtTest = age
        };

        // Act
        var result = await _service.AnalyzeFromDocumentUrlAsync(_userId, req);

        // Assert
        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.Errors, Contains.Item("PatientAgeAtTest không hợp lệ"));
    }

    [Test]
    [Category("N")]
    public async Task AnalyzeFromDocumentUrlAsync_ValidRequest_CreatesSessionAndSchedulesJob()
    {
        // Arrange
        var req = new LabTestAnalyzeRequest
        {
            DocumentUrl = "http://example.com/test.png",
            PatientAgeAtTest = 25,
            PatientGenderAtTest = Gender.Male
        };

        LabTestSession? savedSession = null;
        _sessionsMock.Setup(r => r.Add(It.IsAny<LabTestSession>()))
            .Callback<LabTestSession>(s => savedSession = s);
        _quotaServiceMock.Setup(q => q.ReserveAsync(
                _userId,
                It.IsAny<Guid>(),
                _userId,
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceCreditOperationResult<UserSubscriptionUsage>.Ok(
                new UserSubscriptionUsage
                {
                    Id = Guid.NewGuid(),
                    UserSubscriptionId = Guid.NewGuid(),
                    QuotaId = Guid.NewGuid()
                }));

        // Act
        var result = await _service.AnalyzeFromDocumentUrlAsync(_userId, req);

        // Assert
        Assert.That(result.Succeeded, Is.True);
        Assert.That(result.Data, Is.Not.Null);
        Assert.That(savedSession, Is.Not.Null);
        Assert.That(savedSession.UserId, Is.EqualTo(_userId));
        Assert.That(savedSession.DocumentUrl, Is.EqualTo("http://example.com/test.png"));
        Assert.That(savedSession.Status, Is.EqualTo(LabTestSessionStatus.Processing));
        Assert.That(savedSession.PatientAgeAtTest, Is.EqualTo(25));
        Assert.That(savedSession.PatientGenderAtTest, Is.EqualTo(Gender.Male));

        _schedulerMock.Verify(s => s.EnqueueOcr(savedSession.Id), Times.Once);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // â”€â”€ GetSessionAsync â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Test]
    [Category("B")]
    public async Task GetSessionAsync_EmptyIds_ReturnsNull()
    {
        // Act & Assert
        Assert.That(await _service.GetSessionAsync(Guid.Empty, Guid.NewGuid()), Is.Null);
        Assert.That(await _service.GetSessionAsync(Guid.NewGuid(), Guid.Empty), Is.Null);
    }

    [Test]
    [Category("A")]
    public async Task GetSessionAsync_SessionNotFound_ReturnsNull()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        _sessionDetailsMock.Setup(r => r.GetByIdWithResultsAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((LabTestSession?)null);

        // Act
        var result = await _service.GetSessionAsync(_userId, sessionId);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    [Category("A")]
    public async Task GetSessionAsync_UserIdMismatch_ReturnsNull()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var session = new LabTestSession { Id = sessionId, UserId = Guid.NewGuid() };
        _sessionDetailsMock.Setup(r => r.GetByIdWithResultsAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        // Act
        var result = await _service.GetSessionAsync(_userId, sessionId);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    [Category("N")]
    public async Task GetSessionAsync_Admin_ReturnsSessionOfOtherUser()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var session = new LabTestSession { Id = sessionId, UserId = Guid.NewGuid(), AiSummary = "Tóm tắt" };
        _sessionDetailsMock.Setup(r => r.GetByIdWithResultsAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        // Act
        var result = await _service.GetSessionAsync(_userId, sessionId, isAdmin: true);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.AiSummary, Is.EqualTo("Tóm tắt"));
    }

    [Test]
    [Category("N")]
    public async Task GetSessionAsync_AssignedDoctor_ReturnsSession()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var doctor = new Doctor { Id = Guid.NewGuid(), UserId = _userId, IsActive = true };
        var session = new LabTestSession { Id = sessionId, UserId = Guid.NewGuid(), AiSummary = "Tóm tắt" };
        _sessionDetailsMock.Setup(r => r.GetByIdWithResultsAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        _recoveryPlanRequestsMock.Setup(r => r.GetDoctorByUserIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(doctor);
        _recoveryPlanRequestsMock.Setup(r => r.IsLabSessionAssignedToDoctorAsync(
                sessionId,
                doctor.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _service.GetSessionAsync(_userId, sessionId);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.AiSummary, Is.EqualTo("Tóm tắt"));
    }

    [Test]
    [Category("A")]
    public async Task GetSessionAsync_DoctorNotAssigned_ReturnsNull()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var doctor = new Doctor { Id = Guid.NewGuid(), UserId = _userId, IsActive = true };
        var session = new LabTestSession { Id = sessionId, UserId = Guid.NewGuid() };
        _sessionDetailsMock.Setup(r => r.GetByIdWithResultsAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        _recoveryPlanRequestsMock.Setup(r => r.GetDoctorByUserIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(doctor);
        _recoveryPlanRequestsMock.Setup(r => r.IsLabSessionAssignedToDoctorAsync(
                sessionId,
                doctor.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _service.GetSessionAsync(_userId, sessionId);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    [Category("A")]
    public async Task GetSessionAsync_InactiveDoctor_ReturnsNull()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var doctor = new Doctor { Id = Guid.NewGuid(), UserId = _userId, IsActive = false };
        var session = new LabTestSession { Id = sessionId, UserId = Guid.NewGuid() };
        _sessionDetailsMock.Setup(r => r.GetByIdWithResultsAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        _recoveryPlanRequestsMock.Setup(r => r.GetDoctorByUserIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(doctor);

        // Act
        var result = await _service.GetSessionAsync(_userId, sessionId);

        // Assert
        Assert.That(result, Is.Null);
        _recoveryPlanRequestsMock.Verify(r => r.IsLabSessionAssignedToDoctorAsync(
            It.IsAny<Guid>(),
            It.IsAny<Guid>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    [Category("B")]
    public async Task GetSessionAsync_CompletedSessionWithNoResults_TriggersAnalysis()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var sessionWithoutResults = new LabTestSession
        {
            Id = sessionId,
            UserId = _userId,
            Status = LabTestSessionStatus.Completed,
            RawOcrText = "some raw text",
            LabTestResultDetails = new List<LabTestResultDetail>() // Empty results
        };

        var sessionWithResults = new LabTestSession
        {
            Id = sessionId,
            UserId = _userId,
            Status = LabTestSessionStatus.Completed,
            RawOcrText = "some raw text",
            LabTestResultDetails = new List<LabTestResultDetail>
            {
                new() { Id = Guid.NewGuid(), RawExtractedName = "HGB", UserValue = 14.5 }
            }
        };

        _sessionDetailsMock.SetupSequence(r => r.GetByIdWithResultsAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessionWithoutResults)
            .ReturnsAsync(sessionWithResults);

        // Act
        var result = await _service.GetSessionAsync(_userId, sessionId);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Results, Has.Count.EqualTo(1));
        _analyzerMock.Verify(a => a.AnalyzeAndPersistAsync(sessionId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    [Category("N")]
    public async Task GetSessionAsync_ValidSession_ReturnsMappedResponse()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var session = new LabTestSession
        {
            Id = sessionId,
            UserId = _userId,
            Status = LabTestSessionStatus.Completed,
            DocumentUrl = "http://example.com/test.png",
            PatientGenderAtTest = Gender.Female,
            PatientAgeAtTest = 30,
            LabTestResultDetails = new List<LabTestResultDetail>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    RawExtractedName = "Glucose",
                    UserValue = 5.5,
                    Status = LabResultStatus.Normal,
                    IsMatched = true,
                    MatchConfidence = 0.95,
                    ReferenceMinUsed = 4.0,
                    ReferenceMaxUsed = 6.0,
                    ReferenceUnitUsed = "mmol/L"
                }
            }
        };

        _sessionDetailsMock.Setup(r => r.GetByIdWithResultsAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        // Act
        var result = await _service.GetSessionAsync(_userId, sessionId);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.SessionId, Is.EqualTo(sessionId));
        Assert.That(result.DocumentUrl, Is.EqualTo(session.DocumentUrl));
        Assert.That(result.PatientGenderAtTest, Is.EqualTo(Gender.Female));
        Assert.That(result.Results, Has.Count.EqualTo(1));
        var item = result.Results[0];
        Assert.That(item.RawExtractedName, Is.EqualTo("Glucose"));
        Assert.That(item.UserValue, Is.EqualTo(5.5));
        Assert.That(item.ReferenceMinUsed, Is.EqualTo(4.0));
        Assert.That(item.ReferenceMaxUsed, Is.EqualTo(6.0));
        Assert.That(item.ReferenceUnitUsed, Is.EqualTo("mmol/L"));
        Assert.That(item.ComparisonTypeUsed, Is.EqualTo(ReferenceComparisonType.Between));
    }

    // â”€â”€ GetSessionsByUserIdAsync â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Test]
    [Category("B")]
    public async Task GetSessionsByUserIdAsync_EmptyUserId_ReturnsEmptyResponse()
    {
        // Act
        var result = await _service.GetSessionsByUserIdAsync(Guid.Empty, null, 1, 10);

        // Assert
        Assert.That(result.Items, Is.Empty);
    }

    [Test]
    [Category("N")]
    public async Task GetSessionsByUserIdAsync_ValidUserId_ReturnsPagedResponse()
    {
        // Arrange
        var pagedResult = new PagedResult<LabTestSession>
        {
            PageNumber = 1,
            PageSize = 10,
            TotalCount = 1,
            TotalPages = 1,
            Items = new List<LabTestSession>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    UserId = _userId,
                    DocumentUrl = "http://example.com/test.png",
                    Status = LabTestSessionStatus.Completed,
                    PatientGenderAtTest = Gender.Male,
                    PatientAgeAtTest = 45,
                    CreatedAt = DateTime.UtcNow
                }
            }
        };

        _sessionDetailsMock.Setup(r => r.GetPagedByUserIdAsync(_userId, null, 1, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pagedResult);

        // Act
        var result = await _service.GetSessionsByUserIdAsync(_userId, null, 1, 10);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.PageNumber, Is.EqualTo(1));
        Assert.That(result.Items, Has.Count.EqualTo(1));
        Assert.That(result.Items[0].SessionId, Is.EqualTo(pagedResult.Items[0].Id));
    }

    // â”€â”€ GetOcrExtractsBySessionIdAsync â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Test]
    [Category("B")]
    public async Task GetOcrExtractsBySessionIdAsync_EmptyIds_ReturnsNull()
    {
        Assert.That(await _service.GetOcrExtractsBySessionIdAsync(Guid.Empty, Guid.NewGuid()), Is.Null);
        Assert.That(await _service.GetOcrExtractsBySessionIdAsync(Guid.NewGuid(), Guid.Empty), Is.Null);
    }

    [Test]
    [Category("A")]
    public async Task GetOcrExtractsBySessionIdAsync_SessionNotFound_ReturnsNull()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        _sessionsMock.Setup(r => r.GetByIdAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((LabTestSession?)null);

        // Act
        var result = await _service.GetOcrExtractsBySessionIdAsync(_userId, sessionId);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    [Category("A")]
    public async Task GetOcrExtractsBySessionIdAsync_SessionUserIdMismatch_ReturnsNull()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var session = new LabTestSession { Id = sessionId, UserId = Guid.NewGuid() };
        _sessionsMock.Setup(r => r.GetByIdAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        // Act
        var result = await _service.GetOcrExtractsBySessionIdAsync(_userId, sessionId);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    [Category("N")]
    public async Task GetOcrExtractsBySessionIdAsync_ValidSession_ReturnsExtracts()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var session = new LabTestSession { Id = sessionId, UserId = _userId };
        var extracts = new List<LabTestOcrExtract>
        {
            new() { Id = Guid.NewGuid(), TestSessionId = sessionId, RowIndex = 1, ExtractedTestName = "HGB", ExtractedValue = "14.5" }
        };

        _sessionsMock.Setup(r => r.GetByIdAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        _ocrExtractsMock.Setup(r => r.GetAllAsync(
                It.IsAny<Expression<Func<LabTestOcrExtract, bool>>>(),
                It.IsAny<Func<IQueryable<LabTestOcrExtract>, IOrderedQueryable<LabTestOcrExtract>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(extracts);

        // Act
        var result = await _service.GetOcrExtractsBySessionIdAsync(_userId, sessionId);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].ExtractedTestName, Is.EqualTo("HGB"));
        Assert.That(result[0].ExtractedValue, Is.EqualTo("14.5"));
    }

    // SummarizeSessionAsync / ProcessSummaryAsync

    [Test]
    [Category("N")]
    public async Task SummarizeSessionAsync_NoSummary_MarksProcessingAndEnqueues()
    {
        var session = CreateCompletedSessionWithResults();
        SetupSessionLookups(session);

        var result = await _service.SummarizeSessionAsync(_userId, session.Id);

        Assert.That(result.Succeeded, Is.True);
        Assert.That(result.Data!.Status, Is.EqualTo(LabTestSummaryStatus.Processing));
        Assert.That(session.AiSummaryStatus, Is.EqualTo(LabTestSummaryStatus.Processing));
        _schedulerMock.Verify(s => s.EnqueueSummary(session.Id), Times.Once);
        _aiChatProviderMock.Verify(p => p.GenerateAsync(It.IsAny<AIProviderChatRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    [Category("N")]
    public async Task SummarizeSessionAsync_ExistingSummary_ReturnsCompletedWithoutEnqueue()
    {
        var session = CreateCompletedSessionWithResults();
        session.AiSummary = "Tóm tắt";
        session.AiSummaryStatus = LabTestSummaryStatus.Completed;
        SetupSessionLookups(session);

        var result = await _service.SummarizeSessionAsync(_userId, session.Id);

        Assert.That(result.Data!.Status, Is.EqualTo(LabTestSummaryStatus.Completed));
        Assert.That(result.Data.AiSummary, Is.EqualTo("Tóm tắt"));
        _schedulerMock.Verify(s => s.EnqueueSummary(It.IsAny<Guid>()), Times.Never);
    }

    [Test]
    [Category("A")]
    public async Task SummarizeSessionAsync_AlreadyProcessing_DoesNotEnqueueAgain()
    {
        var session = CreateCompletedSessionWithResults();
        session.AiSummaryStatus = LabTestSummaryStatus.Processing;
        SetupSessionLookups(session);

        var result = await _service.SummarizeSessionAsync(_userId, session.Id);

        Assert.That(result.Data!.Status, Is.EqualTo(LabTestSummaryStatus.Processing));
        _schedulerMock.Verify(s => s.EnqueueSummary(It.IsAny<Guid>()), Times.Never);
    }

    [Test]
    [Category("N")]
    public async Task ProcessSummaryAsync_Success_SavesSummaryAndCompletes()
    {
        var session = CreateCompletedSessionWithResults();
        session.AiSummaryStatus = LabTestSummaryStatus.Processing;
        SetupSessionLookups(session);
        SetupSummaryAiConfig();
        _aiChatProviderMock.Setup(p => p.GenerateAsync(It.IsAny<AIProviderChatRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AIProviderChatResult { Content = "  Kết quả tóm tắt  " });

        await _service.ProcessSummaryAsync(session.Id, isFinalAttempt: false);

        Assert.That(session.AiSummary, Is.EqualTo("Kết quả tóm tắt"));
        Assert.That(session.AiSummaryStatus, Is.EqualTo(LabTestSummaryStatus.Completed));
    }

    [Test]
    [Category("A")]
    public void ProcessSummaryAsync_TransientFailureNotFinal_RethrowsForRetry()
    {
        var session = CreateCompletedSessionWithResults();
        session.AiSummaryStatus = LabTestSummaryStatus.Processing;
        SetupSessionLookups(session);
        SetupSummaryAiConfig();
        _aiChatProviderMock.Setup(p => p.GenerateAsync(It.IsAny<AIProviderChatRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TransientRemoteCallException("503", 503));

        Assert.ThrowsAsync<TransientRemoteCallException>(
            () => _service.ProcessSummaryAsync(session.Id, isFinalAttempt: false));
        Assert.That(session.AiSummaryStatus, Is.EqualTo(LabTestSummaryStatus.Processing));
    }

    [Test]
    [Category("A")]
    public async Task ProcessSummaryAsync_TransientFailureFinalAttempt_MarksFailed()
    {
        var session = CreateCompletedSessionWithResults();
        session.AiSummaryStatus = LabTestSummaryStatus.Processing;
        SetupSessionLookups(session);
        SetupSummaryAiConfig();
        _aiChatProviderMock.Setup(p => p.GenerateAsync(It.IsAny<AIProviderChatRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TransientRemoteCallException("503", 503));

        await _service.ProcessSummaryAsync(session.Id, isFinalAttempt: true);

        Assert.That(session.AiSummaryStatus, Is.EqualTo(LabTestSummaryStatus.Failed));
        Assert.That(session.AiSummary, Is.Null);
    }

    [Test]
    [Category("A")]
    public async Task ProcessSummaryAsync_NonTransientFailure_MarksFailedWithoutRethrow()
    {
        var session = CreateCompletedSessionWithResults();
        session.AiSummaryStatus = LabTestSummaryStatus.Processing;
        SetupSessionLookups(session);
        SetupSummaryAiConfig();
        _aiChatProviderMock.Setup(p => p.GenerateAsync(It.IsAny<AIProviderChatRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("bad response"));

        await _service.ProcessSummaryAsync(session.Id, isFinalAttempt: false);

        Assert.That(session.AiSummaryStatus, Is.EqualTo(LabTestSummaryStatus.Failed));
    }

    [Test]
    [Category("B")]
    public async Task ProcessSummaryAsync_NotProcessing_Skips()
    {
        var session = CreateCompletedSessionWithResults();
        session.AiSummaryStatus = LabTestSummaryStatus.Failed;
        SetupSessionLookups(session);

        await _service.ProcessSummaryAsync(session.Id, isFinalAttempt: false);

        Assert.That(session.AiSummaryStatus, Is.EqualTo(LabTestSummaryStatus.Failed));
        _aiChatProviderMock.Verify(p => p.GenerateAsync(It.IsAny<AIProviderChatRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private LabTestSession CreateCompletedSessionWithResults()
    {
        var sessionId = Guid.NewGuid();
        return new LabTestSession
        {
            Id = sessionId,
            UserId = _userId,
            Status = LabTestSessionStatus.Completed,
            LabTestResultDetails = new List<LabTestResultDetail>
            {
                new() { Id = Guid.NewGuid(), TestSessionId = sessionId, RawExtractedName = "HGB", RawExtractedValue = "14.5" },
            },
        };
    }

    private void SetupSessionLookups(LabTestSession session)
    {
        _sessionDetailsMock.Setup(r => r.GetByIdWithResultsAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        _sessionsMock.Setup(r => r.GetByIdAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
    }

    private void SetupSummaryAiConfig()
    {
        _aiConfigServiceMock.Setup(s => s.GetActiveAIConfigByTaskTypeAsync("LabTestSummary", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AIConfigResponse { TaskType = "LabTestSummary", SystemPrompt = "prompt" });
    }
}
