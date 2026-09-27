using MedMateAI.Domain.Common;
using MedMateAI.Domain.Entities;
using MedMateAI.Domain.Enums;

namespace MedMateAI.Domain.Repository;

public interface IRecoveryPlanRequestRepository
{
    Task<RecoveryPlanRequest?> GetByIdAsync(
        Guid requestId,
        CancellationToken cancellationToken = default);

    Task<RecoveryPlanRequest?> GetByIdForUpdateAsync(
        Guid requestId,
        CancellationToken cancellationToken = default);

    Task<bool> LockUserRecoveryPlanWorkflowAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<bool> HasOpenRequestForUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Guid>> GetExpiredAssignmentIdsAsync(
        DateTime utcNow,
        int batchSize,
        CancellationToken cancellationToken = default);

    Task<PagedResult<RecoveryPlanRequest>> GetOpenPagedAsync(
        int pageNumber,
        int pageSize,
        RecoveryPlanDiseaseGroup? diseaseGroup,
        CancellationToken cancellationToken = default);

    Task<PagedResult<RecoveryPlanRequest>> GetByUserPagedAsync(
        Guid userId,
        int pageNumber,
        int pageSize,
        RecoveryPlanRequestStatus? status,
        CancellationToken cancellationToken = default);

    Task<DoctorRecoveryPlanRequestData?> GetAssignedToDoctorByIdAsync(
        Guid doctorId,
        Guid requestId,
        CancellationToken cancellationToken = default);

    Task<PagedResult<DoctorRecoveryPlanRequestData>> GetAssignedToDoctorPagedAsync(
        Guid doctorId,
        int pageNumber,
        int pageSize,
        RecoveryPlanRequestStatus? status,
        CancellationToken cancellationToken = default);

    Task<RecoveryPlanRequest?> TryAcceptAsync(
        Guid requestId,
        Guid doctorId,
        DateTime acceptedAt,
        DateTime assignmentExpiresAt,
        DateTime utcNow,
        CancellationToken cancellationToken = default);

    Task<Doctor?> GetDoctorByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<RecoveryPlanRealtimeDoctorAccessData?> GetRealtimeDoctorAccessAsync(
        Guid doctorUserId,
        CancellationToken cancellationToken = default);

    Task<Doctor?> GetDoctorByUserIdForUpdateAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<int> CountActiveAssignmentsAsync(
        Guid doctorId,
        CancellationToken cancellationToken = default);

    Task<bool> IsOwnedTreatmentJourneyAsync(
        Guid treatmentJourneyId,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<bool> IsOwnedLabSessionAsync(
        Guid labTestSessionId,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<bool> IsLabSessionAssignedToDoctorAsync(
        Guid labTestSessionId,
        Guid doctorId,
        CancellationToken cancellationToken = default);

    Task<RecoveryPlanRequestReadinessProfileData?> GetPatientProfileReadinessAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    void Add(RecoveryPlanRequest request);

    void AddEvent(RecoveryPlanRequestEvent requestEvent);

    void AddOutbox(OutboxMessage message);
}
