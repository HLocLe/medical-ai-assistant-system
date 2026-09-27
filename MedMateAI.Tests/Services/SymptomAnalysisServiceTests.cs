using AutoMapper;
using MedMateAI.Application.DTOs.SymptomAnalysis.Requests;
using MedMateAI.Application.DTOs.SymptomAnalysis.Responses.ClinicalQuestions;
using MedMateAI.Application.DTOs.SymptomAnalysis.Responses.Quota;
using MedMateAI.Application.DTOs.SymptomAnalysis.Responses.Session;
using MedMateAI.Application.DTOs.Users.Responses;
using MedMateAI.Application.IService;
using MedMateAI.Application.Models.Payments;
using MedMateAI.Application.Models.ServiceCredits;
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
public class SymptomAnalysisServiceTests
{
    private Mock<IUnitOfWork> _unitOfWorkMock = null!;
    private Mock<ISymptomAnalysisSessionRepository> _sessionsMock = null!;
    private Mock<IIcdChapterRepository> _chaptersMock = null!;
    private Mock<IClinicalQuestionRepository> _questionsMock = null!;
    private Mock<ISessionClinicalQuestionAnswerRepository> _sessionAnswersMock = null!;
    private Mock<IDepartmentRecommendationRepository> _recommendationsMock = null!;
    private Mock<ISessionSymptomRepository> _sessionSymptomsMock = null!;
    private Mock<IMedicalDepartmentRepository> _medicalDeptsMock = null!;
    private Mock<IMedicalFacilityRepository> _facilitiesMock = null!;

    private Mock<IUserService> _userServiceMock = null!;
    private Mock<ITranslationService> _translationServiceMock = null!;
    private Mock<IMedGemmaChatService> _medGemmaMock = null!;
    private Mock<IIcdLookupService> _icdLookupMock = null!;
    private Mock<ISymptomAnalysisQuotaService> _quotaServiceMock = null!;
    private Mock<ISymptomAnalysisJobScheduler> _jobSchedulerMock = null!;
    private Mock<IQuotaUsageRepository> _quotaUsagesMock = null!;
    private Mock<IMapper> _mapperMock = null!;
    private Mock<ILogger<SymptomAnalysisService>> _loggerMock = null!;

    private SymptomAnalysisService _service = null!;
    private readonly Guid _sessionId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    [SetUp]
    public void SetUp()
    {
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _sessionsMock = new Mock<ISymptomAnalysisSessionRepository>();
        _chaptersMock = new Mock<IIcdChapterRepository>();
        _questionsMock = new Mock<IClinicalQuestionRepository>();
        _sessionAnswersMock = new Mock<ISessionClinicalQuestionAnswerRepository>();
        _recommendationsMock = new Mock<IDepartmentRecommendationRepository>();
        _sessionSymptomsMock = new Mock<ISessionSymptomRepository>();
        _medicalDeptsMock = new Mock<IMedicalDepartmentRepository>();
        _facilitiesMock = new Mock<IMedicalFacilityRepository>();

        _userServiceMock = new Mock<IUserService>();
        _translationServiceMock = new Mock<ITranslationService>();
        _medGemmaMock = new Mock<IMedGemmaChatService>();
        _icdLookupMock = new Mock<IIcdLookupService>();
        _quotaServiceMock = new Mock<ISymptomAnalysisQuotaService>();
        _jobSchedulerMock = new Mock<ISymptomAnalysisJobScheduler>();
        _quotaUsagesMock = new Mock<IQuotaUsageRepository>();
        _mapperMock = new Mock<IMapper>();
        _loggerMock = new Mock<ILogger<SymptomAnalysisService>>();

        _unitOfWorkMock.Setup(u => u.SymptomAnalysisSessions).Returns(_sessionsMock.Object);
        _unitOfWorkMock.Setup(u => u.IcdChapters).Returns(_chaptersMock.Object);
        _unitOfWorkMock.Setup(u => u.ClinicalQuestions).Returns(_questionsMock.Object);
        _unitOfWorkMock.Setup(u => u.SessionClinicalQuestionAnswers).Returns(_sessionAnswersMock.Object);
        _unitOfWorkMock.Setup(u => u.DepartmentRecommendations).Returns(_recommendationsMock.Object);
        _unitOfWorkMock.Setup(u => u.SessionSymptoms).Returns(_sessionSymptomsMock.Object);
        _unitOfWorkMock.Setup(u => u.MedicalDepartments).Returns(_medicalDeptsMock.Object);
        _unitOfWorkMock.Setup(u => u.MedicalFacilities).Returns(_facilitiesMock.Object);
        _unitOfWorkMock.Setup(u => u.QuotaUsages).Returns(_quotaUsagesMock.Object);

        _unitOfWorkMock.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _unitOfWorkMock.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _unitOfWorkMock.Setup(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _unitOfWorkMock.Setup(u => u.RollbackTransactionAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _quotaServiceMock.Setup(q => q.ReserveAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceCreditOperationResult<UserSubscriptionUsage>.Ok(new UserSubscriptionUsage
            {
                Id = Guid.NewGuid(),
                UserSubscriptionId = Guid.NewGuid(),
            }));

        _service = new SymptomAnalysisService(
            _unitOfWorkMock.Object,
            _userServiceMock.Object,
            _translationServiceMock.Object,
            _medGemmaMock.Object,
            _icdLookupMock.Object,
            _quotaServiceMock.Object,
            _jobSchedulerMock.Object,
            _mapperMock.Object,
            _loggerMock.Object);
    }

    // ── GetSessionByIdAsync ──────────────────────────────────────────────────

    [Test]
    [Category("B")]
    public async Task GetSessionByIdAsync_EmptyId_ReturnsNull()
    {
        Assert.That(await _service.GetSessionByIdAsync(Guid.Empty), Is.Null);
    }

    [Test]
    [Category("A")]
    public async Task GetSessionByIdAsync_SessionNotFound_ReturnsNull()
    {
        _sessionsMock.Setup(r => r.GetByIdAsync(_sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SymptomAnalysisSession?)null);

        Assert.That(await _service.GetSessionByIdAsync(_sessionId), Is.Null);
    }

    [Test]
    [Category("N")]
    public async Task GetSessionByIdAsync_ValidSession_ReturnsMappedResponse()
    {
        // Arrange
        var session = new SymptomAnalysisSession { Id = _sessionId, UserId = _userId, Status = SymptomAnalysisSessionStatus.Completed };
        _userServiceMock.Setup(service => service.GetCurrentUserAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApplicationUserResponse { Id = _userId });
        _userServiceMock.Setup(service => service.IsInRoleAsync(
                _userId,
                "Admin",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _sessionsMock.Setup(r => r.GetByIdAsync(_sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        var symptomsPaged = new PagedResult<SessionSymptom> { Items = new List<SessionSymptom>() };
        _sessionSymptomsMock.Setup(r => r.GetPagedAsync(1, 100, It.IsAny<System.Linq.Expressions.Expression<Func<SessionSymptom, bool>>>(), It.IsAny<Func<IQueryable<SessionSymptom>, IOrderedQueryable<SessionSymptom>>>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(symptomsPaged);

        var answers = new List<SessionClinicalQuestionAnswer>();
        _sessionAnswersMock.Setup(r => r.GetBySessionIdAsync(_sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(answers);

        var recsPaged = new PagedResult<DepartmentRecommendation> { Items = new List<DepartmentRecommendation>() };
        _recommendationsMock.Setup(r => r.GetPagedAsync(1, 50, It.IsAny<System.Linq.Expressions.Expression<Func<DepartmentRecommendation, bool>>>(), It.IsAny<Func<IQueryable<DepartmentRecommendation>, IOrderedQueryable<DepartmentRecommendation>>>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(recsPaged);

        _mapperMock.Setup(m => m.Map<SymptomAnalysisResponse>(session))
            .Returns(new SymptomAnalysisResponse { SessionId = _sessionId });

        // Act
        var result = await _service.GetSessionByIdAsync(_sessionId);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.SessionId, Is.EqualTo(_sessionId));
    }

    // ── GetSessionsByUserIdAsync ─────────────────────────────────────────────

    [Test]
    [Category("B")]
    public async Task GetSessionsByUserIdAsync_EmptyId_ReturnsEmptyResponse()
    {
        var result = await _service.GetSessionsByUserIdAsync(Guid.Empty, null, 1, 10);
        Assert.That(result.Items, Is.Empty);
    }

    // ── SuggestClinicalQuestionAsync ─────────────────────────────────────────

    [Test]
    [Category("B")]
    public void SuggestClinicalQuestionAsync_EmptyInput_ThrowsArgumentException()
    {
        var req = new SuggestClinicalQuestionRequest { UserInput = "  " };
        var exception = Assert.ThrowsAsync<ArgumentException>(() =>
            _service.SuggestClinicalQuestionAsync(req));

        Assert.That(exception!.Message, Is.EqualTo("Nội dung triệu chứng là bắt buộc"));
    }

    [Test]
    [Category("B")]
    public async Task SuggestClinicalQuestionAsync_InputTooLong_ThrowsArgumentException()
    {
        var req = new SuggestClinicalQuestionRequest { UserInput = new string('x', 2001) };
        Assert.ThrowsAsync<ArgumentException>(() => _service.SuggestClinicalQuestionAsync(req));
    }

    [Test]
    [Category("N")]
    public async Task SuggestClinicalQuestionAsync_ValidInput_MatchesChaptersAndSuggestsQuestions()
    {
        // Arrange
        var req = new SuggestClinicalQuestionRequest { UserInput = "Đau đầu" };
        var chapters = new List<IcdChapter>
        {
            new()
            {
                Id = Guid.NewGuid(),
                ChapterCode = "C1",
                KeywordWeights = new Dictionary<string, int> { ["Đau"] = 5 }
            }
        };

        _chaptersMock.Setup(r => r.GetActiveChaptersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(chapters);

        var questions = new List<ClinicalQuestion>
        {
            new()
            {
                Id = Guid.NewGuid(),
                QuestionVi = "Bạn có bị đau đầu nhiều không?",
                ChapterId = chapters[0].Id,
                Answers = new Dictionary<string, string> { ["có"] = "headache" }
            }
        };

        _questionsMock.Setup(r => r.GetQuestionsByChapterIdsAsync(It.IsAny<List<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(questions);

        // Act
        var result = await _service.SuggestClinicalQuestionAsync(req);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Questions, Has.Count.EqualTo(1));
        Assert.That(result.Questions[0].QuestionVi, Is.EqualTo("Bạn có bị đau đầu nhiều không?"));

        _sessionAnswersMock.Verify(r => r.Add(It.IsAny<SessionClinicalQuestionAnswer>()), Times.Once);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWorkMock.Verify(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    [Category("A")]
    public async Task GetQuotaAsync_Unauthenticated_ReturnsNull()
    {
        _userServiceMock.Setup(s => s.GetCurrentUserAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((ApplicationUserResponse?)null);

        Assert.That(await _service.GetQuotaAsync(), Is.Null);
    }

    [Test]
    [Category("N")]
    public async Task GetQuotaAsync_WithServiceCredit_IsFreeTierFalse()
    {
        _userServiceMock.Setup(s => s.GetCurrentUserAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApplicationUserResponse { Id = _userId });

        _sessionsMock.Setup(r => r.GetAllAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<SymptomAnalysisSession, bool>>>(),
                It.IsAny<Func<IQueryable<SymptomAnalysisSession>, IOrderedQueryable<SymptomAnalysisSession>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<SymptomAnalysisSession>());

        _quotaUsagesMock.Setup(r => r.GetEligibleByUserAsync(
                _userId,
                IServiceCreditService.QuotaCode,
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UserSubscriptionUsage>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    LimitValue = 10,
                    UsedCount = 1,
                    ReservedCount = 0,
                },
            });

        var result = await _service.GetQuotaAsync();

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.HasServiceCredit, Is.True);
        Assert.That(result.IsFreeTier, Is.False);
        Assert.That(result.LimitPerDay, Is.EqualTo(5));
        Assert.That(result.UsedToday, Is.EqualTo(0));
        Assert.That(result.RemainingToday, Is.EqualTo(5));
    }

    [Test]
    [Category("N")]
    public async Task GetQuotaAsync_NoServiceCredit_CountsFreeCompletedToday()
    {
        _userServiceMock.Setup(s => s.GetCurrentUserAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApplicationUserResponse { Id = _userId });

        var todayUtc = DateTime.UtcNow;
        _sessionsMock.Setup(r => r.GetAllAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<SymptomAnalysisSession, bool>>>(),
                It.IsAny<Func<IQueryable<SymptomAnalysisSession>, IOrderedQueryable<SymptomAnalysisSession>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SymptomAnalysisSession>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    UserId = _userId,
                    Status = SymptomAnalysisSessionStatus.Completed,
                    CompletedAt = todayUtc,
                    UserSubscriptionId = null,
                },
                new()
                {
                    Id = Guid.NewGuid(),
                    UserId = _userId,
                    Status = SymptomAnalysisSessionStatus.Completed,
                    CompletedAt = todayUtc,
                    UserSubscriptionId = null,
                },
            });

        _quotaUsagesMock.Setup(r => r.GetEligibleByUserAsync(
                _userId,
                IServiceCreditService.QuotaCode,
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<UserSubscriptionUsage>());

        var result = await _service.GetQuotaAsync();

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.HasServiceCredit, Is.False);
        Assert.That(result.IsFreeTier, Is.True);
        Assert.That(result.UsedToday, Is.EqualTo(2));
        Assert.That(result.RemainingToday, Is.EqualTo(3));
    }

    [Test]
    [Category("N")]
    public async Task SubmitClinicalQuestionAnswersAsync_PersistsAnswersAndEnqueuesAnalyzeJob()
    {
        var questionId = Guid.NewGuid();
        var session = new SymptomAnalysisSession
        {
            Id = _sessionId,
            UserId = _userId,
            InputText = "ho khan",
            Status = SymptomAnalysisSessionStatus.Processing,
            UserSubscriptionId = Guid.NewGuid(),
        };
        var answer = new SessionClinicalQuestionAnswer
        {
            Id = Guid.NewGuid(),
            SymptomAnalysisSessionId = _sessionId,
            ClinicalQuestionId = questionId,
            AnswerValues = new Dictionary<string, bool> { ["yes"] = false },
            ClinicalQuestion = new ClinicalQuestion
            {
                Id = questionId,
                QuestionVi = "Có sốt?",
                Answers = new Dictionary<string, string> { ["yes"] = "fever", ["no"] = "no fever" },
            },
        };

        _sessionsMock.Setup(r => r.GetByIdAsync(_sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        _sessionAnswersMock.Setup(r => r.GetTrackedBySessionIdAsync(_sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SessionClinicalQuestionAnswer> { answer });
        _translationServiceMock.Setup(t => t.TranslateToEnglishAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("dry cough");
        _mapperMock.Setup(m => m.Map<List<ClinicalQuestionAnswerResult>>(It.IsAny<object>()))
            .Returns(new List<ClinicalQuestionAnswerResult>());

        var result = await _service.SubmitClinicalQuestionAnswersAsync(
            new SubmitClinicalQuestionAnswersRequest
            {
                SessionId = _sessionId,
                Answers =
                [
                    new ClinicalQuestionAnswerItem
                    {
                        QuestionId = questionId,
                        Answers = new Dictionary<string, bool> { ["yes"] = true },
                    },
                ],
            },
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.SessionId, Is.EqualTo(_sessionId));
            Assert.That(result.Status, Is.EqualTo(SymptomAnalysisSessionStatus.Processing));
            Assert.That(answer.AnswerValues["yes"], Is.True);
        });
        _jobSchedulerMock.Verify(s => s.EnqueueAnalyze(_sessionId), Times.Once);
        _quotaServiceMock.Verify(
            q => q.FinalizeAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _medGemmaMock.Verify(
            m => m.GenerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    [Category("A")]
    public async Task ProcessAnalyzeAsync_SessionNotProcessing_DoesNothing()
    {
        _sessionsMock.Setup(r => r.GetByIdAsync(_sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SymptomAnalysisSession
            {
                Id = _sessionId,
                Status = SymptomAnalysisSessionStatus.Completed,
                InputText = "ho",
            });

        await _service.ProcessAnalyzeAsync(_sessionId, isFinalAttempt: false, CancellationToken.None);

        _medGemmaMock.Verify(
            m => m.GenerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
