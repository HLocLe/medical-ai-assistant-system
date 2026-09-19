using System.Linq.Expressions;
using MedMateAI.Application.Common;
using MedMateAI.Application.Common.Time;
using MedMateAI.Application.DTOs.ChecklistItems.Responses;
using MedMateAI.Application.DTOs.ConsultationSessions.Requests;
using MedMateAI.Application.DTOs.MedicalDepartments.Responses;
using MedMateAI.Application.DTOs.MedicalFacilities.Responses;
using MedMateAI.Application.DTOs.Users.Responses;
using MedMateAI.Application.IService;
using MedMateAI.Application.Models.Notifications;
using MedMateAI.Application.Service;
using MedMateAI.Domain.Common;
using MedMateAI.Domain.Entities;
using MedMateAI.Domain.Enums;
using MedMateAI.Domain.Persistence;
using MedMateAI.Domain.Repository;
using Moq;
using NUnit.Framework;

namespace MedMateAI.Tests.Services;

[TestFixture]
public class ConsultationSessionServiceTests
{
    private Mock<IMedicalDepartmentService> _medicalDepartmentServiceMock = null!;
    private Mock<IMedicalFacilityService> _medicalFacilityServiceMock = null!;
    private Mock<IAIConfigService> _aiConfigServiceMock = null!;
    private Mock<IAIChatProvider> _aiChatProviderMock = null!;
    private Mock<IGenericRepository<ConsultationSession>> _sessionsRepoMock = null!;
    private Mock<IGenericRepository<ConsultationQuestion>> _questionsRepoMock = null!;
    private Mock<IUnitOfWork> _unitOfWorkMock = null!;
    private Mock<IUserService> _userServiceMock = null!;
    private Mock<IChecklistItemService> _checklistItemServiceMock = null!;
    private Mock<IEmailSender> _emailSenderMock = null!;
    private Mock<IConsultationSessionJobScheduler> _jobSchedulerMock = null!;
    private Mock<IConsultationSessionQuotaService> _quotaServiceMock = null!;
    private Mock<IPushNotificationGateway> _pushGatewayMock = null!;
    private Mock<IUserPushDeviceRepository> _pushDeviceRepositoryMock = null!;
    private ConsultationSessionService _service = null!;
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _departmentId = Guid.NewGuid();
    private readonly Guid _sessionId = Guid.NewGuid();

    [SetUp]
    public void SetUp()
    {
        _medicalDepartmentServiceMock = new Mock<IMedicalDepartmentService>();
        _medicalFacilityServiceMock = new Mock<IMedicalFacilityService>();
        _aiConfigServiceMock = new Mock<IAIConfigService>();
        _aiChatProviderMock = new Mock<IAIChatProvider>();
        _sessionsRepoMock = new Mock<IGenericRepository<ConsultationSession>>();
        _questionsRepoMock = new Mock<IGenericRepository<ConsultationQuestion>>();
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _userServiceMock = new Mock<IUserService>();
        _checklistItemServiceMock = new Mock<IChecklistItemService>();
        _emailSenderMock = new Mock<IEmailSender>();
        _jobSchedulerMock = new Mock<IConsultationSessionJobScheduler>();
        _quotaServiceMock = new Mock<IConsultationSessionQuotaService>();
        _pushGatewayMock = new Mock<IPushNotificationGateway>();
        _pushDeviceRepositoryMock = new Mock<IUserPushDeviceRepository>();

        _pushDeviceRepositoryMock
            .Setup(r => r.GetActiveByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<UserPushDeviceData>());

        _service = new ConsultationSessionService(
            _medicalDepartmentServiceMock.Object,
            _medicalFacilityServiceMock.Object,
            _aiConfigServiceMock.Object,
            _aiChatProviderMock.Object,
            _sessionsRepoMock.Object,
            _questionsRepoMock.Object,
            _unitOfWorkMock.Object,
            _userServiceMock.Object,
            _checklistItemServiceMock.Object,
            _emailSenderMock.Object,
            _jobSchedulerMock.Object,
            _quotaServiceMock.Object,
            _pushGatewayMock.Object,
            _pushDeviceRepositoryMock.Object);
    }

    private ConsultationSession MakeSession(
        ConsultationSessionStatus status = ConsultationSessionStatus.Completed,
        Guid? userId = null) =>
        new()
        {
            Id = _sessionId,
            UserId = userId ?? _userId,
            DepartmentId = _departmentId,
            UserSymptoms = "Fever and cough",
            Status = status,
            CreatedAt = DateTime.UtcNow
        };

    // ── GetMyCompletedSessionsAsync ──────────────────────────────────────────

    [Test]
    [Category("B")]
    public async Task GetMyCompletedSessionsAsync_EmptyUserId_ReturnsEmptyPagedResponse()
    {
        var result = await _service.GetMyCompletedSessionsAsync(Guid.Empty, 1, 10, CancellationToken.None);

        Assert.That(result.Items, Is.Empty);
        _sessionsRepoMock.Verify(r => r.GetPagedAsync(
            It.IsAny<int>(), It.IsAny<int>(),
            It.IsAny<Expression<Func<ConsultationSession, bool>>>(),
            It.IsAny<Func<IQueryable<ConsultationSession>, IOrderedQueryable<ConsultationSession>>>(),
            It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    [Category("N")]
    public async Task GetMyCompletedSessionsAsync_ValidRequest_ReturnsMappedSessions()
    {
        var session = MakeSession();
        _sessionsRepoMock.Setup(r => r.GetPagedAsync(
                1, 10,
                It.IsAny<Expression<Func<ConsultationSession, bool>>>(),
                It.IsAny<Func<IQueryable<ConsultationSession>, IOrderedQueryable<ConsultationSession>>>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<ConsultationSession>
            {
                PageNumber = 1,
                PageSize = 10,
                TotalCount = 1,
                TotalPages = 1,
                Items = new List<ConsultationSession> { session }
            });
        _medicalDepartmentServiceMock.Setup(m => m.GetMedicalDepartmentByIdAsync(_departmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MedicalDepartmentResponse { Id = _departmentId, DepartmentName = "Cardiology" });

        var result = await _service.GetMyCompletedSessionsAsync(_userId, 1, 10, CancellationToken.None);

        Assert.That(result.Items, Has.Count.EqualTo(1));
        Assert.That(result.Items[0].SessionId, Is.EqualTo(_sessionId));
        Assert.That(result.Items[0].DepartmentName, Is.EqualTo("Cardiology"));
    }

    // ── GetConsultationSessionByIdAsync ──────────────────────────────────────

    [Test]
    [Category("B")]
    public async Task GetConsultationSessionByIdAsync_EmptyIds_ReturnsNotFound()
    {
        var (notFound, data) = await _service.GetConsultationSessionByIdAsync(
            Guid.Empty, Guid.Empty, CancellationToken.None);

        Assert.That(notFound, Is.True);
        Assert.That(data, Is.Null);
    }

    [Test]
    [Category("A")]
    public async Task GetConsultationSessionByIdAsync_SessionNotFound_ReturnsNotFound()
    {
        _sessionsRepoMock.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<ConsultationSession, bool>>>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ConsultationSession?)null);

        var (notFound, data) = await _service.GetConsultationSessionByIdAsync(
            _userId, _sessionId, CancellationToken.None);

        Assert.That(notFound, Is.True);
        Assert.That(data, Is.Null);
    }

    [Test]
    [Category("N")]
    public async Task GetConsultationSessionByIdAsync_ValidRequest_ReturnsDetailWithQuestions()
    {
        var session = MakeSession();
        _sessionsRepoMock.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<ConsultationSession, bool>>>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        _medicalDepartmentServiceMock.Setup(m => m.GetMedicalDepartmentByIdAsync(_departmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MedicalDepartmentResponse { Id = _departmentId, DepartmentName = "Cardiology" });
        var question = new ConsultationQuestion
        {
            Id = Guid.NewGuid(),
            ConsultationSessionId = _sessionId,
            QuestionText = "Any chest pain?",
            Category = "Symptom",
            Priority = 0
        };
        _questionsRepoMock.Setup(r => r.GetPagedAsync(
                1, 100,
                It.IsAny<Expression<Func<ConsultationQuestion, bool>>>(),
                It.IsAny<Func<IQueryable<ConsultationQuestion>, IOrderedQueryable<ConsultationQuestion>>>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<ConsultationQuestion>
            {
                PageNumber = 1,
                PageSize = 100,
                TotalCount = 1,
                TotalPages = 1,
                Items = new List<ConsultationQuestion> { question }
            });

        var (notFound, data) = await _service.GetConsultationSessionByIdAsync(
            _userId, _sessionId, CancellationToken.None);

        Assert.That(notFound, Is.False);
        Assert.That(data!.SessionId, Is.EqualTo(_sessionId));
        Assert.That(data.DepartmentName, Is.EqualTo("Cardiology"));
        Assert.That(data.Questions, Has.Count.EqualTo(1));
        Assert.That(data.Questions[0].QuestionText, Is.EqualTo("Any chest pain?"));
    }

    // ── GenerateDoctorQuestionsAsync appointment validation ───────────────────

    [Test]
    [Category("B")]
    public async Task GenerateDoctorQuestionsAsync_AppointmentInPast_ReturnsValidationError()
    {
        var pastVn = DateTime.SpecifyKind(
            VietnamBusinessDate.ConvertUtcToVietnamLocal(DateTime.UtcNow).AddDays(-1),
            DateTimeKind.Unspecified);

        var (succeeded, errors, data) = await _service.GenerateDoctorQuestionsAsync(
            _userId, _departmentId, "fever", appointmentTime: pastVn);

        Assert.Multiple(() =>
        {
            Assert.That(succeeded, Is.False);
            Assert.That(data, Is.Null);
            Assert.That(errors.Single(), Does.Contain("tương lai"));
        });
        _medicalDepartmentServiceMock.Verify(
            s => s.GetMedicalDepartmentByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    [Category("B")]
    public async Task GenerateDoctorQuestionsAsync_AppointmentMoreThanOneMonthAhead_ReturnsValidationError()
    {
        var farVn = DateTime.SpecifyKind(
            VietnamBusinessDate.ConvertUtcToVietnamLocal(DateTime.UtcNow).AddMonths(1).AddDays(2),
            DateTimeKind.Unspecified);

        // Keep within current year when possible; otherwise year rule may fire first.
        if (farVn.Year != VietnamBusinessDate.GetToday(DateTimeOffset.UtcNow).Year)
        {
            Assert.Ignore("Cannot assert +1 month rule near year boundary.");
        }

        var (succeeded, errors, data) = await _service.GenerateDoctorQuestionsAsync(
            _userId, _departmentId, "fever", appointmentTime: farVn);

        Assert.Multiple(() =>
        {
            Assert.That(succeeded, Is.False);
            Assert.That(data, Is.Null);
            Assert.That(errors.Single(), Does.Contain("1 tháng"));
        });
    }

    [Test]
    [Category("B")]
    public async Task GenerateDoctorQuestionsAsync_AppointmentNextYear_ReturnsValidationError()
    {
        var nextYearVn = new DateTime(
            VietnamBusinessDate.GetToday(DateTimeOffset.UtcNow).Year + 1,
            1,
            15,
            9,
            0,
            0,
            DateTimeKind.Unspecified);

        var (succeeded, errors, data) = await _service.GenerateDoctorQuestionsAsync(
            _userId, _departmentId, "fever", appointmentTime: nextYearVn);

        Assert.Multiple(() =>
        {
            Assert.That(succeeded, Is.False);
            Assert.That(data, Is.Null);
            Assert.That(errors.Single(), Does.Contain("năm hiện tại"));
        });
    }

    // ── RegisterReminderAsync ─────────────────────────────────────────────────

    [Test]
    [Category("B")]
    public async Task RegisterReminderAsync_EmptyIds_ReturnsNotFound()
    {
        var (succeeded, notFound, errors) = await _service.RegisterReminderAsync(
            Guid.Empty, Guid.Empty, new RegisterConsultationReminderRequest(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(succeeded, Is.False);
            Assert.That(notFound, Is.True);
            Assert.That(errors, Is.Empty);
        });
    }

    [Test]
    [Category("B")]
    public async Task RegisterReminderAsync_NullRequest_ReturnsValidationError()
    {
        var (succeeded, notFound, errors) = await _service.RegisterReminderAsync(
            _userId, _sessionId, null!, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(succeeded, Is.False);
            Assert.That(notFound, Is.False);
            Assert.That(errors, Is.Not.Empty);
        });
    }

    [Test]
    [Category("A")]
    public async Task RegisterReminderAsync_SessionNotFound_ReturnsNotFound()
    {
        SetupSessionLookup(null);

        var (succeeded, notFound, _) = await _service.RegisterReminderAsync(
            _userId, _sessionId, new RegisterConsultationReminderRequest(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(succeeded, Is.False);
            Assert.That(notFound, Is.True);
        });
    }

    [Test]
    [Category("A")]
    public async Task RegisterReminderAsync_SessionNotCompleted_ReturnsValidationError()
    {
        SetupSessionLookup(MakeSession(status: ConsultationSessionStatus.Processing));

        var (succeeded, notFound, errors) = await _service.RegisterReminderAsync(
            _userId, _sessionId, new RegisterConsultationReminderRequest(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(succeeded, Is.False);
            Assert.That(notFound, Is.False);
            Assert.That(errors, Is.Not.Empty);
        });
    }

    [Test]
    [Category("N")]
    public async Task RegisterReminderAsync_DisableReminder_UpdatesSessionAndSaves()
    {
        var session = MakeSession();
        session.IsReminderEnabled = true;
        SetupSessionLookup(session);

        var (succeeded, notFound, errors) = await _service.RegisterReminderAsync(
            _userId, _sessionId, new RegisterConsultationReminderRequest { EnableReminder = false }, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(succeeded, Is.True);
            Assert.That(notFound, Is.False);
            Assert.That(errors, Is.Empty);
            Assert.That(session.IsReminderEnabled, Is.False);
        });
        _sessionsRepoMock.Verify(r => r.Update(session), Times.Once);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    [Category("A")]
    public async Task RegisterReminderAsync_EnableReminderNoEmailOnFile_ReturnsValidationError()
    {
        SetupSessionLookup(MakeSession());
        _userServiceMock.Setup(u => u.GetUserByIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApplicationUserResponse { Id = _userId, Email = null });

        var (succeeded, notFound, errors) = await _service.RegisterReminderAsync(
            _userId, _sessionId,
            new RegisterConsultationReminderRequest { EnableReminder = true },
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(succeeded, Is.False);
            Assert.That(notFound, Is.False);
            Assert.That(errors, Is.Not.Empty);
        });
    }

    [Test]
    [Category("N")]
    public async Task RegisterReminderAsync_EnableReminderWithPushDeviceOnly_EnablesReminderAndSaves()
    {
        var session = MakeSession();
        SetupSessionLookup(session);
        _userServiceMock.Setup(u => u.GetUserByIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApplicationUserResponse { Id = _userId, Email = null });
        SetupPushDevices(new UserPushDeviceData(
            Guid.NewGuid(),
            _userId,
            "ExponentPushToken[test]",
            1,
            "android",
            true));

        var (succeeded, notFound, errors) = await _service.RegisterReminderAsync(
            _userId, _sessionId,
            new RegisterConsultationReminderRequest { EnableReminder = true },
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(succeeded, Is.True);
            Assert.That(notFound, Is.False);
            Assert.That(errors, Is.Empty);
            Assert.That(session.IsReminderEnabled, Is.True);
        });
    }

    [Test]
    [Category("N")]
    public async Task RegisterReminderAsync_EnableReminderWithEmail_EnablesReminderAndSaves()
    {
        var session = MakeSession();
        SetupSessionLookup(session);
        _userServiceMock.Setup(u => u.GetUserByIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApplicationUserResponse { Id = _userId, Email = "user@gmail.com" });

        var (succeeded, notFound, errors) = await _service.RegisterReminderAsync(
            _userId, _sessionId,
            new RegisterConsultationReminderRequest { EnableReminder = true },
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(succeeded, Is.True);
            Assert.That(notFound, Is.False);
            Assert.That(errors, Is.Empty);
            Assert.That(session.IsReminderEnabled, Is.True);
        });
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── GetSummaryAsync ──────────────────────────────────────────────────────

    [Test]
    [Category("B")]
    public async Task GetSummaryAsync_EmptyIds_ReturnsNotFound()
    {
        var (notFound, data) = await _service.GetSummaryAsync(Guid.Empty, Guid.Empty, CancellationToken.None);

        Assert.That(notFound, Is.True);
        Assert.That(data, Is.Null);
    }

    [Test]
    [Category("A")]
    public async Task GetSummaryAsync_SessionNotCompleted_ReturnsNotFound()
    {
        SetupSessionLookup(MakeSession(status: ConsultationSessionStatus.Processing));

        var (notFound, data) = await _service.GetSummaryAsync(_userId, _sessionId, CancellationToken.None);

        Assert.That(notFound, Is.True);
        Assert.That(data, Is.Null);
    }

    [Test]
    [Category("N")]
    public async Task GetSummaryAsync_ValidSession_ReturnsSummaryWithoutSendingReminder()
    {
        var appointment = DateTime.UtcNow.AddHours(5);
        var session = MakeSession();
        session.IsReminderEnabled = true;
        session.AppointmentTime = appointment;
        SetupSessionLookup(session);
        SetupSummaryDependencies();

        var (notFound, data) = await _service.GetSummaryAsync(_userId, _sessionId, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(notFound, Is.False);
            Assert.That(data, Is.Not.Null);
            Assert.That(data!.SessionId, Is.EqualTo(_sessionId));
            Assert.That(data.DepartmentName, Is.EqualTo("Cardiology"));
            Assert.That(data.ReminderEmailRequired, Is.True);
            Assert.That(data.ReminderPushRequired, Is.False);
            Assert.That(data.ReminderEmailSent, Is.False);
            Assert.That(data.ReminderPushSent, Is.False);
            Assert.That(data.ReminderSmsSent, Is.False);
        });
        _jobSchedulerMock.Verify(s => s.ScheduleReminderSms(It.IsAny<Guid>(), It.IsAny<DateTime>()), Times.Never);
        _jobSchedulerMock.Verify(s => s.EnqueueReminderSms(It.IsAny<Guid>()), Times.Never);
    }

    // ── CompleteSummaryAsync ─────────────────────────────────────────────────

    [Test]
    [Category("B")]
    public async Task CompleteSummaryAsync_EmptyIds_ReturnsNotFound()
    {
        var (succeeded, notFound, errors, data) = await _service.CompleteSummaryAsync(
            Guid.Empty, Guid.Empty, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(succeeded, Is.False);
            Assert.That(notFound, Is.True);
            Assert.That(errors, Is.Empty);
            Assert.That(data, Is.Null);
        });
    }

    [Test]
    [Category("A")]
    public async Task CompleteSummaryAsync_SessionNotFound_ReturnsNotFound()
    {
        SetupSessionLookup(null);

        var (succeeded, notFound, _, data) = await _service.CompleteSummaryAsync(_userId, _sessionId, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(succeeded, Is.False);
            Assert.That(notFound, Is.True);
            Assert.That(data, Is.Null);
        });
    }

    [Test]
    [Category("A")]
    public async Task CompleteSummaryAsync_SessionNotCompleted_ReturnsValidationError()
    {
        SetupSessionLookup(MakeSession(status: ConsultationSessionStatus.Failed));

        var (succeeded, notFound, errors, data) = await _service.CompleteSummaryAsync(_userId, _sessionId, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(succeeded, Is.False);
            Assert.That(notFound, Is.False);
            Assert.That(errors, Is.Not.Empty);
            Assert.That(data, Is.Null);
        });
    }

    [Test]
    [Category("A")]
    public async Task CompleteSummaryAsync_UserMissing_ReturnsNotFound()
    {
        SetupSessionLookup(MakeSession());
        _userServiceMock.Setup(u => u.GetUserByIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ApplicationUserResponse?)null);

        var (succeeded, notFound, _, data) = await _service.CompleteSummaryAsync(_userId, _sessionId, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(succeeded, Is.False);
            Assert.That(notFound, Is.True);
            Assert.That(data, Is.Null);
        });
    }

    [Test]
    [Category("N")]
    public async Task CompleteSummaryAsync_ReminderDueSoon_EnqueuesImmediateSmsAndSaves()
    {
        var session = MakeSession();
        session.IsReminderEnabled = true;
        session.AppointmentTime = DateTime.UtcNow.AddMinutes(30);
        SetupSessionLookup(session);
        SetupSummaryDependencies();

        var (succeeded, notFound, errors, data) = await _service.CompleteSummaryAsync(_userId, _sessionId, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(succeeded, Is.True);
            Assert.That(notFound, Is.False);
            Assert.That(errors, Is.Empty);
            Assert.That(data, Is.Not.Null);
            Assert.That(session.ReminderScheduledAt, Is.Not.Null);
            Assert.That(session.ReminderSmsSentAt, Is.Null);
        });
        _jobSchedulerMock.Verify(s => s.EnqueueReminderSms(_sessionId), Times.Once);
        _jobSchedulerMock.Verify(s => s.ScheduleReminderSms(It.IsAny<Guid>(), It.IsAny<DateTime>()), Times.Never);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    [Category("N")]
    public async Task CompleteSummaryAsync_ReminderFarInFuture_SchedulesSmsForOneHourBefore()
    {
        var appointment = DateTime.UtcNow.AddHours(5);
        var session = MakeSession();
        session.IsReminderEnabled = true;
        session.AppointmentTime = appointment;
        SetupSessionLookup(session);
        SetupSummaryDependencies();

        var (succeeded, _, _, _) = await _service.CompleteSummaryAsync(_userId, _sessionId, CancellationToken.None);

        Assert.That(succeeded, Is.True);
        Assert.That(session.ReminderScheduledAt, Is.Not.Null);
        Assert.That(session.ReminderSmsSentAt, Is.Null);
        _jobSchedulerMock.Verify(s => s.ScheduleReminderSms(
            _sessionId,
            It.Is<DateTime>(dt => Math.Abs((dt - appointment.AddHours(-1)).TotalSeconds) < 1)), Times.Once);
        _jobSchedulerMock.Verify(s => s.EnqueueReminderSms(It.IsAny<Guid>()), Times.Never);
    }

    [Test]
    [Category("N")]
    public async Task CompleteSummaryAsync_MergesChecklistItemsAndFacilityOverridesDepartment()
    {
        var facilityId = Guid.NewGuid();
        var session = MakeSession();
        session.FacilityId = facilityId;
        SetupSessionLookup(session);
        SetupSummaryDependencies(facilityId: facilityId);

        var sharedId = Guid.NewGuid();
        _checklistItemServiceMock.Setup(c => c.GetByDepartmentIdAsync(_departmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ChecklistItemResponse>
            {
                new() { Id = sharedId, Content = "department version", IsMandatory = false },
                new() { Id = Guid.NewGuid(), Content = "department only", IsMandatory = true },
            });
        _checklistItemServiceMock.Setup(c => c.GetByFacilityIdAsync(facilityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ChecklistItemResponse>
            {
                new() { Id = sharedId, Content = "facility version", IsMandatory = true },
            });

        var (succeeded, _, _, data) = await _service.CompleteSummaryAsync(_userId, _sessionId, CancellationToken.None);

        Assert.That(succeeded, Is.True);
        Assert.That(data!.ChecklistItems, Has.Count.EqualTo(2));
        var sharedResult = data.ChecklistItems.Single(item => item.Id == sharedId);
        Assert.That(sharedResult.Content, Is.EqualTo("facility version"));
    }

    // ── ProcessSendReminderSmsAsync ──────────────────────────────────────────

    [Test]
    [Category("B")]
    public async Task ProcessSendReminderSmsAsync_EmptySessionId_DoesNothing()
    {
        await _service.ProcessSendReminderSmsAsync(Guid.Empty, CancellationToken.None);

        _sessionsRepoMock.Verify(r => r.FirstOrDefaultAsync(
            It.IsAny<Expression<Func<ConsultationSession, bool>>>(),
            It.IsAny<bool>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    [Category("A")]
    public async Task ProcessSendReminderSmsAsync_SessionNotFound_DoesNothing()
    {
        SetupSessionLookup(null);

        await _service.ProcessSendReminderSmsAsync(_sessionId, CancellationToken.None);

        _emailSenderMock.Verify(s => s.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    [Category("A")]
    public async Task ProcessSendReminderSmsAsync_ReminderDisabled_DoesNothing()
    {
        var session = MakeSession();
        session.IsReminderEnabled = false;
        session.AppointmentTime = DateTime.UtcNow.AddHours(1);
        SetupSessionLookup(session);

        await _service.ProcessSendReminderSmsAsync(_sessionId, CancellationToken.None);

        _emailSenderMock.Verify(s => s.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    [Category("A")]
    public async Task ProcessSendReminderSmsAsync_AppointmentAlreadyPassed_DoesNothing()
    {
        var session = MakeSession();
        session.IsReminderEnabled = true;
        session.AppointmentTime = DateTime.UtcNow.AddMinutes(-5);
        SetupSessionLookup(session);

        await _service.ProcessSendReminderSmsAsync(_sessionId, CancellationToken.None);

        _emailSenderMock.Verify(s => s.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    [Category("A")]
    public async Task ProcessSendReminderSmsAsync_UserEmailMissing_DoesNothingWhenNoPushDevices()
    {
        var session = MakeSession();
        session.IsReminderEnabled = true;
        session.AppointmentTime = DateTime.UtcNow.AddHours(1);
        SetupSessionLookup(session);
        _userServiceMock.Setup(u => u.GetUserByIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApplicationUserResponse { Id = _userId, Email = null });

        await _service.ProcessSendReminderSmsAsync(_sessionId, CancellationToken.None);

        _emailSenderMock.Verify(s => s.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _pushGatewayMock.Verify(g => g.SendAsync(It.IsAny<PushNotificationMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    [Category("N")]
    public async Task ProcessSendReminderSmsAsync_PushOnlySucceeds_MarksSentAndSaves()
    {
        var session = MakeSession();
        session.IsReminderEnabled = true;
        session.AppointmentTime = DateTime.UtcNow.AddHours(1);
        SetupSessionLookup(session);
        _userServiceMock.Setup(u => u.GetUserByIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApplicationUserResponse { Id = _userId, Email = null });
        _medicalDepartmentServiceMock.Setup(m => m.GetMedicalDepartmentByIdAsync(_departmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MedicalDepartmentResponse { Id = _departmentId, DepartmentName = "Cardiology" });
        SetupPushDevices(new UserPushDeviceData(
            Guid.NewGuid(),
            _userId,
            "ExponentPushToken[test]",
            1,
            "android",
            true));
        _pushGatewayMock.Setup(g => g.SendAsync(It.IsAny<PushNotificationMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PushSendResult(PushSendOutcome.Accepted, "ticket-1"));

        await _service.ProcessSendReminderSmsAsync(_sessionId, CancellationToken.None);

        Assert.That(session.ReminderSmsSentAt, Is.Not.Null);
        Assert.That(session.ReminderPushSentAt, Is.Not.Null);
        _pushGatewayMock.Verify(g => g.SendAsync(It.IsAny<PushNotificationMessage>(), It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Test]
    [Category("N")]
    public async Task ProcessSendReminderSmsAsync_EmailFailsTransientlyPushSucceeds_PersistsPushAndThrows()
    {
        var session = MakeSession();
        session.IsReminderEnabled = true;
        session.AppointmentTime = DateTime.UtcNow.AddHours(1);
        SetupSessionLookup(session);
        _userServiceMock.Setup(u => u.GetUserByIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApplicationUserResponse { Id = _userId, Email = "user@gmail.com" });
        _medicalDepartmentServiceMock.Setup(m => m.GetMedicalDepartmentByIdAsync(_departmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MedicalDepartmentResponse { Id = _departmentId, DepartmentName = "Cardiology" });
        _emailSenderMock.Setup(s => s.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TransientRemoteCallException("Brevo timeout", 504));
        SetupPushDevices(new UserPushDeviceData(
            Guid.NewGuid(),
            _userId,
            "ExponentPushToken[test]",
            1,
            "android",
            true));
        _pushGatewayMock.Setup(g => g.SendAsync(It.IsAny<PushNotificationMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PushSendResult(PushSendOutcome.Accepted, "ticket-1"));

        Assert.ThrowsAsync<TransientRemoteCallException>(
            async () => await _service.ProcessSendReminderSmsAsync(_sessionId, CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(session.ReminderPushSentAt, Is.Not.Null);
            Assert.That(session.ReminderEmailSentAt, Is.Null);
            Assert.That(session.ReminderSmsSentAt, Is.Null);
        });
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    [Category("N")]
    public async Task ProcessSendReminderSmsAsync_EmailAlreadySent_SkipsEmailAndCompletesPush()
    {
        var session = MakeSession();
        session.IsReminderEnabled = true;
        session.AppointmentTime = DateTime.UtcNow.AddHours(1);
        session.ReminderEmailSentAt = DateTime.UtcNow.AddMinutes(-5);
        SetupSessionLookup(session);
        _userServiceMock.Setup(u => u.GetUserByIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApplicationUserResponse { Id = _userId, Email = "user@gmail.com" });
        _medicalDepartmentServiceMock.Setup(m => m.GetMedicalDepartmentByIdAsync(_departmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MedicalDepartmentResponse { Id = _departmentId, DepartmentName = "Cardiology" });
        SetupPushDevices(new UserPushDeviceData(
            Guid.NewGuid(),
            _userId,
            "ExponentPushToken[test]",
            1,
            "android",
            true));
        _pushGatewayMock.Setup(g => g.SendAsync(It.IsAny<PushNotificationMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PushSendResult(PushSendOutcome.Accepted, "ticket-1"));

        await _service.ProcessSendReminderSmsAsync(_sessionId, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(session.ReminderPushSentAt, Is.Not.Null);
            Assert.That(session.ReminderSmsSentAt, Is.Not.Null);
        });
        _emailSenderMock.Verify(
            s => s.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    [Category("N")]
    public async Task ProcessSendReminderSmsAsync_EmailTerminalFailPushSucceeds_DoesNotMarkFullySent()
    {
        var session = MakeSession();
        session.IsReminderEnabled = true;
        session.AppointmentTime = DateTime.UtcNow.AddHours(1);
        SetupSessionLookup(session);
        _userServiceMock.Setup(u => u.GetUserByIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApplicationUserResponse { Id = _userId, Email = "user@gmail.com" });
        _medicalDepartmentServiceMock.Setup(m => m.GetMedicalDepartmentByIdAsync(_departmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MedicalDepartmentResponse { Id = _departmentId, DepartmentName = "Cardiology" });
        _emailSenderMock.Setup(s => s.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Brevo failed"));
        SetupPushDevices(new UserPushDeviceData(
            Guid.NewGuid(),
            _userId,
            "ExponentPushToken[test]",
            1,
            "android",
            true));
        _pushGatewayMock.Setup(g => g.SendAsync(It.IsAny<PushNotificationMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PushSendResult(PushSendOutcome.Accepted, "ticket-1"));

        await _service.ProcessSendReminderSmsAsync(_sessionId, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(session.ReminderPushSentAt, Is.Not.Null);
            Assert.That(session.ReminderSmsSentAt, Is.Null);
        });
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    [Category("A")]
    public async Task ProcessSendReminderSmsAsync_SendFails_DoesNotMarkSentOrSave()
    {
        var session = MakeSession();
        session.IsReminderEnabled = true;
        session.AppointmentTime = DateTime.UtcNow.AddHours(1);
        SetupSessionLookup(session);
        _userServiceMock.Setup(u => u.GetUserByIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApplicationUserResponse { Id = _userId, Email = "user@gmail.com" });
        _emailSenderMock.Setup(s => s.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Brevo failed"));

        await _service.ProcessSendReminderSmsAsync(_sessionId, CancellationToken.None);

        Assert.That(session.ReminderSmsSentAt, Is.Null);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    [Category("N")]
    public async Task ProcessSendReminderSmsAsync_SendSucceeds_MarksSentAndSaves()
    {
        var session = MakeSession();
        session.IsReminderEnabled = true;
        session.AppointmentTime = DateTime.UtcNow.AddHours(1);
        SetupSessionLookup(session);
        _userServiceMock.Setup(u => u.GetUserByIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApplicationUserResponse { Id = _userId, DisplayName = "Nguyen Van A", Email = "user@gmail.com" });
        _medicalDepartmentServiceMock.Setup(m => m.GetMedicalDepartmentByIdAsync(_departmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MedicalDepartmentResponse { Id = _departmentId, DepartmentName = "Cardiology" });
        _emailSenderMock.Setup(s => s.SendAsync(
                "user@gmail.com",
                ConsultationReminderEmailBuilder.Subject,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await _service.ProcessSendReminderSmsAsync(_sessionId, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(session.ReminderEmailSentAt, Is.Not.Null);
            Assert.That(session.ReminderSmsSentAt, Is.Not.Null);
        });
        _sessionsRepoMock.Verify(r => r.Update(session), Times.AtLeastOnce);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    private void SetupSessionLookup(ConsultationSession? session)
    {
        _sessionsRepoMock.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<ConsultationSession, bool>>>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
    }

    private void SetupSummaryDependencies(Guid? facilityId = null)
    {
        _userServiceMock.Setup(u => u.GetUserByIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApplicationUserResponse { Id = _userId, DisplayName = "Nguyen Van A", Email = "user@gmail.com" });
        _medicalDepartmentServiceMock.Setup(m => m.GetMedicalDepartmentByIdAsync(_departmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MedicalDepartmentResponse { Id = _departmentId, DepartmentName = "Cardiology" });
        if (facilityId.HasValue)
        {
            _medicalFacilityServiceMock.Setup(m => m.GetMedicalFacilityByIdAsync(facilityId.Value, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MedicalFacilityResponse { Id = facilityId.Value, FacilityName = "General Hospital" });
        }
        _checklistItemServiceMock.Setup(c => c.GetByDepartmentIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ChecklistItemResponse>());
        _checklistItemServiceMock.Setup(c => c.GetByFacilityIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ChecklistItemResponse>());
        _questionsRepoMock.Setup(r => r.GetPagedAsync(
                1, 100,
                It.IsAny<Expression<Func<ConsultationQuestion, bool>>>(),
                It.IsAny<Func<IQueryable<ConsultationQuestion>, IOrderedQueryable<ConsultationQuestion>>>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<ConsultationQuestion>
            {
                PageNumber = 1,
                PageSize = 100,
                Items = Array.Empty<ConsultationQuestion>(),
            });
    }

    private void SetupPushDevices(params UserPushDeviceData[] devices)
    {
        _pushDeviceRepositoryMock
            .Setup(r => r.GetActiveByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(devices);
    }
}
