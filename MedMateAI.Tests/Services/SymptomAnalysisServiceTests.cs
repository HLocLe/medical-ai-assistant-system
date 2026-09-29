using AutoMapper;
using MedMateAI.Application.DTOs.SymptomAnalysis.Requests;
using MedMateAI.Application.DTOs.SymptomAnalysis.Responses.ClinicalQuestions;
using MedMateAI.Application.DTOs.SymptomAnalysis.Responses.Quota;
using MedMateAI.Application.DTOs.SymptomAnalysis.Responses.Session;
using MedMateAI.Application.DTOs.Users.Responses;
using MedMateAI.Application.IService;
using MedMateAI.Application.Models.Payments;
using MedMateAI.Application.Models.ServiceCredits;
using MedMateAI.Application.Options;
using MedMateAI.Application.Service;
using MedMateAI.Domain.Common;
using MedMateAI.Domain.Entities;
using MedMateAI.Domain.Enums;
using MedMateAI.Domain.Persistence;
using MedMateAI.Domain.Repository;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
    private Mock<IFreeQuotaService> _freeQuotaServiceMock = null!;
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
        _freeQuotaServiceMock = new Mock<IFreeQuotaService>();
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
        SetupFreeReserve(new FreeQuotaUsage { Id = Guid.NewGuid(), UserId = _userId, LimitValue = 5 });
        _quotaUsagesMock.Setup(r => r.GetEligibleByUserAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<UserSubscriptionUsage>());

        _userServiceMock.Setup(s => s.GetCurrentUserAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApplicationUserResponse { Id = _userId });

        _service = new SymptomAnalysisService(
            _unitOfWorkMock.Object,
            _userServiceMock.Object,
            _translationServiceMock.Object,
            _medGemmaMock.Object,
            _icdLookupMock.Object,
            _quotaServiceMock.Object,
            _freeQuotaServiceMock.Object,
            _jobSchedulerMock.Object,
            _mapperMock.Object,
            _loggerMock.Object,
            Options.Create(new SymptomAnalysisOptions()));
    }

    private void SetupFreeReserve(FreeQuotaUsage? usage)
    {
        _freeQuotaServiceMock.Setup(f => f.TryReserveAsync(
                It.IsAny<Guid>(),
                SymptomAnalysisQuotaService.FreeQuotaFeature,
                It.IsAny<int>(),
                SymptomAnalysisQuotaService.ReferenceType,
                It.IsAny<Guid>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(usage);
    }

    private void SetupSuggestChapterAndQuestion()
    {
        var chapterId = Guid.NewGuid();
        _chaptersMock.Setup(r => r.GetActiveChaptersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<IcdChapter>
            {
                new()
                {
                    Id = chapterId,
                    ChapterCode = "C1",
                    KeywordWeights = new Dictionary<string, int> { ["Đau"] = 5 }
                }
            });
        _questionsMock.Setup(r => r.GetQuestionsByChapterIdsAsync(It.IsAny<List<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ClinicalQuestion>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    QuestionVi = "Bạn có bị đau đầu nhiều không?",
                    ChapterId = chapterId,
                    Answers = new Dictionary<string, string> { ["có"] = "headache" }
                }
            });
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
    public async Task GetQuotaAsync_NoServiceCredit_UsesFreeQuotaUsage()
    {
        _freeQuotaServiceMock.Setup(f => f.GetAsync(
                _userId,
                SymptomAnalysisQuotaService.FreeQuotaFeature,
                It.IsAny<DateOnly>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FreeQuotaUsage
            {
                Id = Guid.NewGuid(),
                UserId = _userId,
                LimitValue = 5,
                UsedCount = 2,
                ReservedCount = 1,
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
        Assert.That(result.ReservedToday, Is.EqualTo(1));
        Assert.That(result.RemainingToday, Is.EqualTo(2));
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
        _sessionsMock.Setup(r => r.TryMarkSubmittedAsync(_sessionId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

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
            Assert.That(session.QuotaSource, Is.EqualTo(QuotaSource.Free));
        });
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWorkMock.Verify(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        _jobSchedulerMock.Verify(s => s.EnqueueAnalyze(_sessionId), Times.Once);
        _quotaServiceMock.Verify(
            q => q.FinalizeAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _medGemmaMock.Verify(
            m => m.GenerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private SessionClinicalQuestionAnswer SetupSubmittableSession(SymptomAnalysisSession session)
    {
        var questionId = Guid.NewGuid();
        var answer = new SessionClinicalQuestionAnswer
        {
            Id = Guid.NewGuid(),
            SymptomAnalysisSessionId = session.Id,
            ClinicalQuestionId = questionId,
            AnswerValues = new Dictionary<string, bool> { ["yes"] = false },
            ClinicalQuestion = new ClinicalQuestion
            {
                Id = questionId,
                QuestionVi = "Có sốt?",
                Answers = new Dictionary<string, string> { ["yes"] = "fever" },
            },
        };

        _sessionsMock.Setup(r => r.GetByIdAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        _sessionAnswersMock.Setup(r => r.GetTrackedBySessionIdAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SessionClinicalQuestionAnswer> { answer });
        _translationServiceMock.Setup(t => t.TranslateToEnglishAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("dry cough");
        _mapperMock.Setup(m => m.Map<List<ClinicalQuestionAnswerResult>>(It.IsAny<object>()))
            .Returns(new List<ClinicalQuestionAnswerResult>());
        return answer;
    }

    [Test]
    [Category("A")]
    public async Task SubmitClinicalQuestionAnswersAsync_AlreadySubmitted_ReturnsCurrentStatusWithoutEnqueue()
    {
        var session = new SymptomAnalysisSession
        {
            Id = _sessionId,
            UserId = _userId,
            InputText = "ho khan",
            Status = SymptomAnalysisSessionStatus.Completed,
            SubmittedAt = DateTime.UtcNow.AddMinutes(-1),
        };
        var answer = SetupSubmittableSession(session);

        var result = await _service.SubmitClinicalQuestionAnswersAsync(
            new SubmitClinicalQuestionAnswersRequest
            {
                SessionId = _sessionId,
                Answers =
                [
                    new ClinicalQuestionAnswerItem
                    {
                        QuestionId = answer.ClinicalQuestionId,
                        Answers = new Dictionary<string, bool> { ["yes"] = true },
                    },
                ],
            },
            CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(SymptomAnalysisSessionStatus.Completed));
        Assert.That(answer.AnswerValues["yes"], Is.False);
        _sessionsMock.Verify(
            r => r.TryMarkSubmittedAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _jobSchedulerMock.Verify(s => s.EnqueueAnalyze(It.IsAny<Guid>()), Times.Never);
    }

    [Test]
    [Category("A")]
    public void SubmitClinicalQuestionAnswersAsync_MarkSubmittedLostRace_ThrowsExpired()
    {
        var session = new SymptomAnalysisSession
        {
            Id = _sessionId,
            UserId = _userId,
            InputText = "ho khan",
            Status = SymptomAnalysisSessionStatus.Processing,
        };
        SetupSubmittableSession(session);
        _sessionsMock.Setup(r => r.TryMarkSubmittedAsync(_sessionId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        Assert.ThrowsAsync<InvalidOperationException>(() => _service.SubmitClinicalQuestionAnswersAsync(
            new SubmitClinicalQuestionAnswersRequest { SessionId = _sessionId, Answers = [] },
            CancellationToken.None));
        _jobSchedulerMock.Verify(s => s.EnqueueAnalyze(It.IsAny<Guid>()), Times.Never);
        _unitOfWorkMock.Verify(u => u.RollbackTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        _freeQuotaServiceMock.Verify(f => f.TryReserveAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(),
                It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    [Category("A")]
    public void SubmitClinicalQuestionAnswersAsync_FailedAndNotSubmitted_ThrowsExpired()
    {
        var session = new SymptomAnalysisSession
        {
            Id = _sessionId,
            UserId = _userId,
            InputText = "ho khan",
            Status = SymptomAnalysisSessionStatus.Failed,
        };
        SetupSubmittableSession(session);

        Assert.ThrowsAsync<InvalidOperationException>(() => _service.SubmitClinicalQuestionAnswersAsync(
            new SubmitClinicalQuestionAnswersRequest { SessionId = _sessionId, Answers = [] },
            CancellationToken.None));
    }

    [Test]
    [Category("A")]
    public void SubmitClinicalQuestionAnswersAsync_SessionOfAnotherUser_ThrowsNotFound()
    {
        var session = new SymptomAnalysisSession
        {
            Id = _sessionId,
            UserId = Guid.NewGuid(),
            InputText = "ho khan",
            Status = SymptomAnalysisSessionStatus.Processing,
        };
        SetupSubmittableSession(session);

        Assert.ThrowsAsync<ArgumentException>(() => _service.SubmitClinicalQuestionAnswersAsync(
            new SubmitClinicalQuestionAnswersRequest { SessionId = _sessionId, Answers = [] },
            CancellationToken.None));
    }

    [Test]
    [Category("A")]
    public void SuggestClinicalQuestionAsync_Unauthenticated_Throws()
    {
        SetupSuggestChapterAndQuestion();
        _userServiceMock.Setup(s => s.GetCurrentUserAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((ApplicationUserResponse?)null);

        Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.SuggestClinicalQuestionAsync(new SuggestClinicalQuestionRequest { UserInput = "Đau đầu" }));
        _sessionsMock.Verify(r => r.Add(It.IsAny<SymptomAnalysisSession>()), Times.Never);
    }

    [Test]
    [Category("N")]
    public async Task SuggestClinicalQuestionAsync_DoesNotReserveQuota()
    {
        SetupSuggestChapterAndQuestion();
        SymptomAnalysisSession? added = null;
        _sessionsMock.Setup(r => r.Add(It.IsAny<SymptomAnalysisSession>()))
            .Callback<SymptomAnalysisSession>(s => added = s);

        await _service.SuggestClinicalQuestionAsync(new SuggestClinicalQuestionRequest { UserInput = "Đau đầu" });

        Assert.That(added, Is.Not.Null);
        Assert.That(added!.QuotaSource, Is.EqualTo(QuotaSource.None));
        Assert.That(added.FreeQuotaUsageId, Is.Null);
        Assert.That(added.UserSubscriptionUsageId, Is.Null);
        _freeQuotaServiceMock.Verify(f => f.TryReserveAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(),
                It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _quotaServiceMock.Verify(q => q.ReserveAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    [Category("A")]
    public void SuggestClinicalQuestionAsync_NoFreeLeftAndNoCredit_ThrowsWithoutCreatingSession()
    {
        SetupSuggestChapterAndQuestion();
        _freeQuotaServiceMock.Setup(f => f.GetAsync(
                _userId, SymptomAnalysisQuotaService.FreeQuotaFeature, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FreeQuotaUsage { Id = Guid.NewGuid(), UserId = _userId, LimitValue = 5, UsedCount = 5 });

        Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.SuggestClinicalQuestionAsync(new SuggestClinicalQuestionRequest { UserInput = "Đau đầu" }));
        _sessionsMock.Verify(r => r.Add(It.IsAny<SymptomAnalysisSession>()), Times.Never);
    }

    [Test]
    [Category("N")]
    public async Task SuggestClinicalQuestionAsync_NoFreeLeftButHasCredit_CreatesSession()
    {
        SetupSuggestChapterAndQuestion();
        _freeQuotaServiceMock.Setup(f => f.GetAsync(
                _userId, SymptomAnalysisQuotaService.FreeQuotaFeature, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FreeQuotaUsage { Id = Guid.NewGuid(), UserId = _userId, LimitValue = 5, UsedCount = 5 });
        _quotaUsagesMock.Setup(r => r.GetEligibleByUserAsync(
                _userId, IServiceCreditService.QuotaCode, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UserSubscriptionUsage> { new() { Id = Guid.NewGuid(), LimitValue = 10 } });

        await _service.SuggestClinicalQuestionAsync(new SuggestClinicalQuestionRequest { UserInput = "Đau đầu" });

        _sessionsMock.Verify(r => r.Add(It.IsAny<SymptomAnalysisSession>()), Times.Once);
    }

    private SymptomAnalysisSession CreateUnsubmittedSession()
    {
        var session = new SymptomAnalysisSession
        {
            Id = _sessionId,
            UserId = _userId,
            InputText = "ho khan",
            Status = SymptomAnalysisSessionStatus.Processing,
        };
        SetupSubmittableSession(session);
        _sessionsMock.Setup(r => r.TryMarkSubmittedAsync(_sessionId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        return session;
    }

    [Test]
    [Category("N")]
    public async Task SubmitClinicalQuestionAnswersAsync_FreeAvailable_ReservesFreeAndSkipsCredit()
    {
        var session = CreateUnsubmittedSession();
        var freeUsageId = Guid.NewGuid();
        SetupFreeReserve(new FreeQuotaUsage { Id = freeUsageId, UserId = _userId, LimitValue = 5 });

        await _service.SubmitClinicalQuestionAnswersAsync(
            new SubmitClinicalQuestionAnswersRequest { SessionId = _sessionId, Answers = [] },
            CancellationToken.None);

        Assert.That(session.QuotaSource, Is.EqualTo(QuotaSource.Free));
        Assert.That(session.FreeQuotaUsageId, Is.EqualTo(freeUsageId));
        Assert.That(session.UserSubscriptionId, Is.Null);
        _freeQuotaServiceMock.Verify(f => f.TryReserveAsync(
                _userId, SymptomAnalysisQuotaService.FreeQuotaFeature, It.IsAny<int>(),
                SymptomAnalysisQuotaService.ReferenceType, _sessionId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _quotaServiceMock.Verify(q => q.ReserveAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    [Category("N")]
    public async Task SubmitClinicalQuestionAnswersAsync_FreeExhausted_FallsBackToServiceCredit()
    {
        var session = CreateUnsubmittedSession();
        SetupFreeReserve(null);
        var subscriptionId = Guid.NewGuid();
        var usageId = Guid.NewGuid();
        _quotaServiceMock.Setup(q => q.ReserveAsync(
                _userId, _sessionId, _userId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceCreditOperationResult<UserSubscriptionUsage>.Ok(new UserSubscriptionUsage
            {
                Id = usageId,
                UserSubscriptionId = subscriptionId,
            }));

        await _service.SubmitClinicalQuestionAnswersAsync(
            new SubmitClinicalQuestionAnswersRequest { SessionId = _sessionId, Answers = [] },
            CancellationToken.None);

        Assert.That(session.QuotaSource, Is.EqualTo(QuotaSource.ServiceCredit));
        Assert.That(session.FreeQuotaUsageId, Is.Null);
        Assert.That(session.UserSubscriptionId, Is.EqualTo(subscriptionId));
        Assert.That(session.UserSubscriptionUsageId, Is.EqualTo(usageId));
    }

    [Test]
    [Category("A")]
    public void SubmitClinicalQuestionAnswersAsync_FreeAndCreditExhausted_ThrowsRollsBackAndDoesNotEnqueue()
    {
        CreateUnsubmittedSession();
        SetupFreeReserve(null);
        _quotaServiceMock.Setup(q => q.ReserveAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceCreditOperationResult<UserSubscriptionUsage>.Fail(ServiceCreditErrorCode.NoCreditPackage));

        Assert.ThrowsAsync<InvalidOperationException>(() => _service.SubmitClinicalQuestionAnswersAsync(
            new SubmitClinicalQuestionAnswersRequest { SessionId = _sessionId, Answers = [] },
            CancellationToken.None));
        _unitOfWorkMock.Verify(u => u.RollbackTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWorkMock.Verify(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
        _jobSchedulerMock.Verify(s => s.EnqueueAnalyze(It.IsAny<Guid>()), Times.Never);
    }

    private (Guid FeverChapterId, Guid HeadacheChapterId, List<List<Guid>> RequestedChapterIds) SetupFeverAndHeadacheChapters()
    {
        var feverChapterId = Guid.NewGuid();
        var headacheChapterId = Guid.NewGuid();
        var requestedChapterIds = new List<List<Guid>>();

        _chaptersMock.Setup(r => r.GetActiveChaptersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<IcdChapter>
            {
                new()
                {
                    Id = feverChapterId,
                    ChapterCode = "A",
                    KeywordWeights = new Dictionary<string, int> { ["sốt"] = 5 }
                },
                new()
                {
                    Id = headacheChapterId,
                    ChapterCode = "G",
                    KeywordWeights = new Dictionary<string, int> { ["đau đầu"] = 3 }
                },
            });
        _questionsMock.Setup(r => r.GetQuestionsByChapterIdsAsync(It.IsAny<List<Guid>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<Guid>, CancellationToken>((ids, _) => requestedChapterIds.Add(ids.ToList()))
            .ReturnsAsync((IReadOnlyList<Guid> ids, CancellationToken _) => ids
                .Select(id => new ClinicalQuestion
                {
                    Id = Guid.NewGuid(),
                    QuestionVi = "Câu hỏi",
                    ChapterId = id,
                    Answers = new Dictionary<string, string> { ["có"] = "yes" }
                })
                .ToList());

        return (feverChapterId, headacheChapterId, requestedChapterIds);
    }

    [TestCase("Tôi không sốt")]
    [TestCase("Tôi không bị sốt")]
    [TestCase("Tôi không còn sốt")]
    [TestCase("Chưa thấy sốt")]
    [TestCase("Đã hết sốt rồi")]
    [TestCase("ko sốt")]
    [Category("A")]
    public void SuggestClinicalQuestionAsync_OnlyNegatedSymptom_ThrowsUnsupported(string userInput)
    {
        SetupFeverAndHeadacheChapters();

        Assert.ThrowsAsync<ArgumentException>(() =>
            _service.SuggestClinicalQuestionAsync(new SuggestClinicalQuestionRequest { UserInput = userInput }));
        _freeQuotaServiceMock.Verify(f => f.TryReserveAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(),
                It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestCase("Tôi không sốt nhưng đau đầu")]
    [TestCase("Không sốt, đau đầu nhiều")]
    [TestCase("không sốt và bị đau đầu")]
    [Category("N")]
    public async Task SuggestClinicalQuestionAsync_NegatedSymptomDoesNotScore_PicksAffirmedChapter(string userInput)
    {
        var (_, headacheChapterId, requestedChapterIds) = SetupFeverAndHeadacheChapters();

        await _service.SuggestClinicalQuestionAsync(new SuggestClinicalQuestionRequest { UserInput = userInput });

        Assert.That(requestedChapterIds.Single(), Is.EqualTo(new List<Guid> { headacheChapterId }));
    }

    [TestCase("Tôi bị sốt cao")]
    [TestCase("sốt không hạ được")]
    [TestCase("Không biết sao, tôi bị sốt")]
    [TestCase("Không ăn uống được mấy hôm nay bị sốt")]
    [Category("N")]
    public async Task SuggestClinicalQuestionAsync_NegationOutsideWindowOrAfterKeyword_StillScores(string userInput)
    {
        var (feverChapterId, _, requestedChapterIds) = SetupFeverAndHeadacheChapters();

        await _service.SuggestClinicalQuestionAsync(new SuggestClinicalQuestionRequest { UserInput = userInput });

        Assert.That(requestedChapterIds.Single(), Is.EqualTo(new List<Guid> { feverChapterId }));
    }

    [Test]
    [Category("N")]
    public async Task SuggestClinicalQuestionAsync_KeywordNegatedOnceButAffirmedLater_StillScores()
    {
        var (feverChapterId, _, requestedChapterIds) = SetupFeverAndHeadacheChapters();

        await _service.SuggestClinicalQuestionAsync(new SuggestClinicalQuestionRequest
        {
            UserInput = "Hôm qua không sốt. Hôm nay lại sốt"
        });

        Assert.That(requestedChapterIds.Single(), Is.EqualTo(new List<Guid> { feverChapterId }));
    }

    [Test]
    [Category("N")]
    public async Task ExpireAbandonedSessionsAsync_MarksFailedAndEnqueuesFinalizeOnlyForWonRaces()
    {
        var expiredId = Guid.NewGuid();
        var submittedMeanwhileId = Guid.NewGuid();
        _sessionsMock.Setup(r => r.GetAbandonedSessionIdsAsync(It.IsAny<DateTime>(), 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { expiredId, submittedMeanwhileId });
        _sessionsMock.Setup(r => r.TryMarkAbandonedAsFailedAsync(expiredId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _sessionsMock.Setup(r => r.TryMarkAbandonedAsFailedAsync(submittedMeanwhileId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var before = DateTime.UtcNow;
        var count = await _service.ExpireAbandonedSessionsAsync(CancellationToken.None);

        Assert.That(count, Is.EqualTo(1));
        _jobSchedulerMock.Verify(s => s.EnqueueQuotaFinalize(expiredId), Times.Once);
        _jobSchedulerMock.Verify(s => s.EnqueueQuotaFinalize(submittedMeanwhileId), Times.Never);
        _sessionsMock.Verify(r => r.GetAbandonedSessionIdsAsync(
                It.Is<DateTime>(d => d <= before.AddMinutes(-30).AddSeconds(5) && d >= before.AddMinutes(-30).AddSeconds(-5)),
                100,
                It.IsAny<CancellationToken>()),
            Times.Once);
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
