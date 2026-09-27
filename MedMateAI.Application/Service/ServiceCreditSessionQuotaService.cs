using MedMateAI.Application.IService;
using MedMateAI.Application.Models;
using MedMateAI.Application.Models.ServiceCredits;
using MedMateAI.Domain.Entities;
using MedMateAI.Domain.Enums;
using MedMateAI.Domain.Persistence;
using MedMateAI.Domain.Repository;
using Microsoft.Extensions.Logging;

namespace MedMateAI.Application.Service;

public abstract class ServiceCreditSessionQuotaService<TSession>
    where TSession : BaseEntity
{
    private readonly IServiceCreditService _serviceCreditService;
    private readonly IGenericRepository<TSession> _sessions;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger _logger;
    private readonly string _referenceType;
    private readonly string _idempotencyPrefix;
    private readonly string _workflowName;
    private readonly Func<TSession, Guid> _getUserId;
    private readonly Func<TSession, Guid?> _getUserSubscriptionId;
    private readonly Func<TSession, Guid?> _getUserSubscriptionUsageId;
    private readonly Func<TSession, SubscriptionQuotaActionType?> _getFinalizeAction;

    protected ServiceCreditSessionQuotaService(
        IServiceCreditService serviceCreditService,
        IGenericRepository<TSession> sessions,
        IUnitOfWork unitOfWork,
        ILogger logger,
        string referenceType,
        string idempotencyPrefix,
        string workflowName,
        Func<TSession, Guid> getUserId,
        Func<TSession, Guid?> getUserSubscriptionId,
        Func<TSession, Guid?> getUserSubscriptionUsageId,
        Func<TSession, SubscriptionQuotaActionType?> getFinalizeAction)
    {
        _serviceCreditService = serviceCreditService;
        _sessions = sessions;
        _unitOfWork = unitOfWork;
        _logger = logger;
        _referenceType = referenceType;
        _idempotencyPrefix = idempotencyPrefix;
        _workflowName = workflowName;
        _getUserId = getUserId;
        _getUserSubscriptionId = getUserSubscriptionId;
        _getUserSubscriptionUsageId = getUserSubscriptionUsageId;
        _getFinalizeAction = getFinalizeAction;
    }

    private string SessionDescription => $"{_workflowName.ToLowerInvariant()} session";

    public Task<ServiceCreditOperationResult<UserSubscriptionUsage>> ReserveAsync(
        Guid userId,
        Guid sessionId,
        Guid actorUserId,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        return _serviceCreditService.ReserveAsync(
            userId,
            _referenceType,
            sessionId,
            actorUserId,
            BuildIdempotencyKey("reserve", sessionId),
            $"{_workflowName} session service credit reserved.",
            utcNow,
            cancellationToken);
    }

    public virtual async Task FinalizeAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var session = await _sessions.FirstOrDefaultAsync(
            current => current.Id == sessionId && !current.IsDeleted,
            cancellationToken: cancellationToken);

        if (session is null)
        {
            _logger.LogWarning(
                "{WorkflowName} quota finalization skipped because session {SessionId} was not found.",
                _workflowName,
                sessionId);
            return;
        }

        var userSubscriptionId = _getUserSubscriptionId(session);
        var userSubscriptionUsageId = _getUserSubscriptionUsageId(session);

        if (!userSubscriptionId.HasValue && !userSubscriptionUsageId.HasValue)
        {
            return;
        }

        if (!userSubscriptionId.HasValue || !userSubscriptionUsageId.HasValue)
        {
            _logger.LogError(
                "{SessionDescription} {SessionId} has inconsistent service credit linkage.",
                $"{_workflowName} session",
                sessionId);
            return;
        }

        var actionType = _getFinalizeAction(session);
        if (!actionType.HasValue)
        {
            return;
        }

        await FinalizeMutationAsync(
            session,
            userSubscriptionId.Value,
            userSubscriptionUsageId.Value,
            actionType.Value,
            cancellationToken);
    }

    private async Task FinalizeMutationAsync(
        TSession session,
        Guid userSubscriptionId,
        Guid userSubscriptionUsageId,
        SubscriptionQuotaActionType actionType,
        CancellationToken cancellationToken)
    {
        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        try
        {
            var usage = await _unitOfWork.QuotaUsages.GetByIdAsync(
                userSubscriptionUsageId,
                cancellationToken);
            if (usage is null || usage.UserSubscriptionId != userSubscriptionId)
            {
                throw new InvalidOperationException(
                    $"The {SessionDescription} service credit linkage is invalid.");
            }

            var isConsume = actionType == SubscriptionQuotaActionType.Consume;
            var mutationStatus = await _serviceCreditService.MutateAsync(
                usage.Id,
                usage.UserSubscriptionId,
                usage.QuotaId,
                actionType,
                _referenceType,
                session.Id,
                _getUserId(session),
                BuildIdempotencyKey(isConsume ? "consume" : "release", session.Id),
                $"{_workflowName} session service credit "
                    + (isConsume ? "consumed." : "released."),
                DateTime.UtcNow,
                cancellationToken);

            if (mutationStatus == QuotaMutationStatus.Rejected)
            {
                throw new InvalidOperationException(
                    $"The {SessionDescription} service credit mutation was rejected.");
            }

            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            _logger.LogError(
                ex,
                "{WorkflowName} quota finalization failed for session {SessionId} and action {ActionType}.",
                _workflowName,
                session.Id,
                actionType);
            throw;
        }
    }

    private string BuildIdempotencyKey(string action, Guid sessionId) =>
        $"{_idempotencyPrefix}:{action}:{sessionId:N}";
}
