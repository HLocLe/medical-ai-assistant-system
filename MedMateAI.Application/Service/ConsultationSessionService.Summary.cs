using MedMateAI.Application.Common;
using MedMateAI.Application.DTOs.ChecklistItems.Responses;
using MedMateAI.Application.DTOs.ConsultationSessions.Requests;
using MedMateAI.Application.DTOs.ConsultationSessions.Responses;
using MedMateAI.Application.DTOs.Users.Responses;
using MedMateAI.Application.Helpers;
using MedMateAI.Application.Models.Notifications;
using MedMateAI.Domain.Entities;
using MedMateAI.Domain.Enums;

namespace MedMateAI.Application.Service;

public sealed partial class ConsultationSessionService
{
    public async Task<(bool Succeeded, bool NotFound, IEnumerable<string> Errors)> RegisterReminderAsync(
        Guid userId,
        Guid sessionId,
        RegisterConsultationReminderRequest request,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty || sessionId == Guid.Empty)
        {
            return (false, true, Array.Empty<string>());
        }

        if (request is null)
        {
            return (false, false, new[] { "Request body là bắt buộc" });
        }

        var session = await GetOwnedSessionAsync(userId, sessionId, cancellationToken);
        if (session is null)
        {
            return (false, true, Array.Empty<string>());
        }

        if (session.Status != ConsultationSessionStatus.Completed)
        {
            return (false, false, new[] { "Phiên tư vấn chưa hoàn thành. Vui lòng hoàn tất bước tạo câu hỏi trước." });
        }

        if (!request.EnableReminder)
        {
            session.IsReminderEnabled = false;
            session.UpdatedAt = DateTime.UtcNow;
            _consultationSessions.Update(session);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return (true, false, Array.Empty<string>());
        }

        var user = await _userService.GetUserByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return (false, false, new[] { "Không tìm thấy người dùng." });
        }

        var hasEmail = !string.IsNullOrWhiteSpace(user.Email);
        var hasPushDevices = await HasActivePushDevicesAsync(userId, cancellationToken);
        if (!hasEmail && !hasPushDevices)
        {
            return (false, false, new[] { "Email hoặc thiết bị nhận push là bắt buộc để đăng ký nhắc nhở." });
        }

        session.IsReminderEnabled = true;
        session.UpdatedAt = DateTime.UtcNow;
        _consultationSessions.Update(session);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return (true, false, Array.Empty<string>());
    }

    public async Task<(bool NotFound, ConsultationSummaryResponse? Data)> GetSummaryAsync(
        Guid userId,
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty || sessionId == Guid.Empty)
        {
            return (true, null);
        }

        var summary = await BuildSummaryAsync(userId, sessionId, sendReminderSms: false, cancellationToken);
        if (summary is null)
        {
            return (true, null);
        }

        return (false, summary);
    }

    public async Task<(bool Succeeded, bool NotFound, IEnumerable<string> Errors, ConsultationSummaryResponse? Data)> CompleteSummaryAsync(
        Guid userId,
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty || sessionId == Guid.Empty)
        {
            return (false, true, Array.Empty<string>(), null);
        }

        var session = await GetOwnedSessionAsync(userId, sessionId, cancellationToken);
        if (session is null)
        {
            return (false, true, Array.Empty<string>(), null);
        }

        if (session.Status != ConsultationSessionStatus.Completed)
        {
            return (false, false, new[] { "Phiên tư vấn chưa hoàn thành." }, null);
        }

        var summary = await BuildSummaryAsync(userId, sessionId, sendReminderSms: true, cancellationToken);
        if (summary is null)
        {
            return (false, true, Array.Empty<string>(), null);
        }

        return (true, false, Array.Empty<string>(), summary);
    }

    private async Task<ConsultationSession?> GetOwnedSessionAsync(
        Guid userId,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        return await _consultationSessions.FirstOrDefaultAsync(
            x => !x.IsDeleted && x.Id == sessionId && x.UserId == userId,
            cancellationToken: cancellationToken);
    }

    private async Task<ConsultationSummaryResponse?> BuildSummaryAsync(
        Guid userId,
        Guid sessionId,
        bool sendReminderSms,
        CancellationToken cancellationToken)
    {
        var session = await GetOwnedSessionAsync(userId, sessionId, cancellationToken);
        if (session is null || session.Status != ConsultationSessionStatus.Completed)
        {
            return null;
        }

        var user = await _userService.GetUserByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return null;
        }

        var department = await _medicalDepartmentService.GetMedicalDepartmentByIdAsync(
            session.DepartmentId,
            cancellationToken);

        string? facilityName = null;
        if (session.FacilityId.HasValue)
        {
            var facility = await _medicalFacilityService.GetMedicalFacilityByIdAsync(
                session.FacilityId.Value,
                cancellationToken);
            facilityName = facility?.FacilityName?.Trim();
        }

        var checklistItems = await GetMergedChecklistItemsAsync(
            session.DepartmentId,
            session.FacilityId,
            cancellationToken);

        var questionsPaged = await _consultationQuestions.GetPagedAsync(
            1,
            100,
            q => !q.IsDeleted && q.ConsultationSessionId == sessionId,
            q => q.OrderBy(x => x.Priority),
            cancellationToken: cancellationToken);

        var emailRequired = HasDeliveryEmail(user);
        var pushRequired = await HasActivePushDevicesAsync(userId, cancellationToken);

        if (sendReminderSms
            && session.IsReminderEnabled
            && !session.ReminderScheduledAt.HasValue
            && !session.ReminderSmsSentAt.HasValue
            && session.AppointmentTime.HasValue
            && (emailRequired || pushRequired))
        {
            var remindAtUtc = session.AppointmentTime.Value.ToUniversalTime().AddHours(-1);
            if (remindAtUtc > DateTime.UtcNow)
            {
                _jobScheduler.ScheduleReminderSms(session.Id, remindAtUtc);
            }
            else
            {
                _jobScheduler.EnqueueReminderSms(session.Id);
            }

            // ReminderSmsSentAt stays null until the Hangfire job finishes all required channels.
            session.ReminderScheduledAt = DateTime.UtcNow;
            session.UpdatedAt = DateTime.UtcNow;
            _consultationSessions.Update(session);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return new ConsultationSummaryResponse
        {
            SessionId = session.Id,
            User = new ConsultationSummaryUserInfoResponse
            {
                DisplayName = user.DisplayName ?? user.UserName ?? string.Empty,
                Email = user.Email,
                DateOfBirth = user.DateOfBirth,
            },
            DepartmentId = session.DepartmentId,
            DepartmentName = department?.DepartmentName?.Trim() ?? string.Empty,
            FacilityId = session.FacilityId,
            FacilityName = facilityName,
            AppointmentTime = session.AppointmentTime,
            Symptoms = session.UserSymptoms?.Trim() ?? string.Empty,
            Status = session.Status,
            IsReminderEnabled = session.IsReminderEnabled,
            ReminderEmailRequired = emailRequired,
            ReminderPushRequired = pushRequired,
            ReminderEmailSent = session.ReminderEmailSentAt.HasValue,
            ReminderPushSent = session.ReminderPushSentAt.HasValue,
            ReminderSmsSent = session.ReminderSmsSentAt.HasValue,
            ChecklistItems = checklistItems,
            Questions = questionsPaged.Items
                .Select(question => new ConsultationQuestionResponse
                {
                    Id = question.Id,
                    QuestionText = question.QuestionText,
                    Category = question.Category,
                    Priority = question.Priority,
                })
                .ToList(),
        };
    }

    private async Task<IReadOnlyList<ChecklistItemResponse>> GetMergedChecklistItemsAsync(
        Guid departmentId,
        Guid? facilityId,
        CancellationToken cancellationToken)
    {
        var departmentItems = await _checklistItemService.GetByDepartmentIdAsync(departmentId, cancellationToken);
        var merged = new Dictionary<Guid, ChecklistItemResponse>();

        foreach (var item in departmentItems)
        {
            merged[item.Id] = item;
        }

        if (facilityId.HasValue && facilityId.Value != Guid.Empty)
        {
            var facilityItems = await _checklistItemService.GetByFacilityIdAsync(facilityId.Value, cancellationToken);
            foreach (var item in facilityItems)
            {
                merged[item.Id] = item;
            }
        }

        return merged.Values
            .OrderByDescending(item => item.IsMandatory)
            .ThenBy(item => item.Content)
            .ToList();
    }

    public async Task ProcessSendReminderSmsAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        if (sessionId == Guid.Empty)
        {
            return;
        }

        var session = await _consultationSessions.FirstOrDefaultAsync(
            x => !x.IsDeleted && x.Id == sessionId,
            cancellationToken: cancellationToken);

        if (session is null
            || !session.IsReminderEnabled
            || session.ReminderSmsSentAt.HasValue
            || !session.AppointmentTime.HasValue
            || DateTime.UtcNow >= session.AppointmentTime.Value.ToUniversalTime())
        {
            return;
        }

        var user = await _userService.GetUserByIdAsync(session.UserId, cancellationToken);
        if (user is null)
        {
            return;
        }

        var emailRequired = HasDeliveryEmail(user);
        var pushRequired = await HasActivePushDevicesAsync(session.UserId, cancellationToken);
        if (!emailRequired && !pushRequired)
        {
            return;
        }

        var department = await _medicalDepartmentService.GetMedicalDepartmentByIdAsync(
            session.DepartmentId,
            cancellationToken);

        string? facilityName = null;
        if (session.FacilityId.HasValue)
        {
            var facility = await _medicalFacilityService.GetMedicalFacilityByIdAsync(
                session.FacilityId.Value,
                cancellationToken);
            facilityName = facility?.FacilityName?.Trim();
        }

        var departmentName = department?.DepartmentName?.Trim() ?? string.Empty;
        var facilityDisplayName = facilityName ?? "Chưa cập nhật";
        var utcNow = DateTime.UtcNow;
        var sessionDirty = false;
        var hasRetryableFailure = false;

        var emailOk = !emailRequired || session.ReminderEmailSentAt.HasValue;
        if (emailRequired && !session.ReminderEmailSentAt.HasValue)
        {
            var htmlContent = ConsultationReminderEmailBuilder.BuildHtml(
                user.DisplayName ?? user.UserName ?? "Bạn",
                user.DateOfBirth,
                departmentName,
                facilityDisplayName,
                session.AppointmentTime);

            try
            {
                await _emailSender.SendAsync(
                    user.Email!,
                    ConsultationReminderEmailBuilder.Subject,
                    htmlContent,
                    cancellationToken);
                session.ReminderEmailSentAt = utcNow;
                emailOk = true;
                sessionDirty = true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (BackgroundJobRetry.IsRetryable(ex))
            {
                hasRetryableFailure = true;
            }
            catch (Exception)
            {
                // Terminal email failure — do not retry this channel.
                emailOk = false;
            }
        }

        var pushOk = !pushRequired || session.ReminderPushSentAt.HasValue;
        if (pushRequired && !session.ReminderPushSentAt.HasValue)
        {
            var pushAttempt = await SendConsultationReminderPushAsync(
                session,
                departmentName,
                facilityDisplayName,
                cancellationToken);

            if (pushAttempt.Succeeded)
            {
                session.ReminderPushSentAt = utcNow;
                pushOk = true;
                sessionDirty = true;
            }
            else if (pushAttempt.HasRetryableFailure)
            {
                hasRetryableFailure = true;
                pushOk = false;
            }
            else
            {
                pushOk = false;
            }
        }

        if (sessionDirty)
        {
            session.UpdatedAt = utcNow;
            _consultationSessions.Update(session);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        if (emailOk && pushOk)
        {
            session.ReminderSmsSentAt = DateTime.UtcNow;
            session.UpdatedAt = DateTime.UtcNow;
            _consultationSessions.Update(session);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return;
        }

        if (hasRetryableFailure)
        {
            throw new TransientRemoteCallException(
                "Consultation reminder hit a transient email/push failure.");
        }
    }

    private static bool HasDeliveryEmail(ApplicationUserResponse user)
    {
        return !string.IsNullOrWhiteSpace(user.Email);
    }

    private async Task<bool> HasActivePushDevicesAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var devices = await _pushDeviceRepository.GetActiveByUserIdAsync(userId, cancellationToken);
        return devices.Count > 0;
    }

    private async Task<ReminderPushAttemptResult> SendConsultationReminderPushAsync(
        ConsultationSession session,
        string departmentName,
        string facilityName,
        CancellationToken cancellationToken)
    {
        var devices = await _pushDeviceRepository.GetActiveByUserIdAsync(
            session.UserId,
            cancellationToken);
        if (devices.Count == 0)
        {
            return new ReminderPushAttemptResult(Succeeded: false, HasRetryableFailure: false);
        }

        var body = ConsultationReminderPushBuilder.BuildBody(
            departmentName,
            facilityName,
            session.AppointmentTime);
        var data = ConsultationReminderPushBuilder.BuildData(session.Id);
        var ttlSeconds = ConsultationReminderPushBuilder.BuildTimeToLiveSeconds(session.AppointmentTime);
        var anyAccepted = false;
        var hasRetryableFailure = false;
        var utcNow = DateTime.UtcNow;

        foreach (var device in devices)
        {
            if (string.IsNullOrWhiteSpace(device.ExpoPushToken))
            {
                continue;
            }

            var result = await _pushGateway.SendAsync(
                new PushNotificationMessage(
                    device.ExpoPushToken,
                    ConsultationReminderPushBuilder.Title,
                    body,
                    data,
                    ttlSeconds),
                cancellationToken);

            switch (result.Outcome)
            {
                case PushSendOutcome.Accepted:
                    anyAccepted = true;
                    break;
                case PushSendOutcome.RetryableFailure:
                    hasRetryableFailure = true;
                    break;
                case PushSendOutcome.InvalidDevice:
                    await _pushDeviceRepository.DeactivateIfTokenVersionMatchesAsync(
                        device.Id,
                        device.UserId,
                        device.TokenVersion,
                        utcNow,
                        cancellationToken);
                    break;
            }
        }

        return new ReminderPushAttemptResult(anyAccepted, hasRetryableFailure && !anyAccepted);
    }

    private sealed record ReminderPushAttemptResult(bool Succeeded, bool HasRetryableFailure);
}
