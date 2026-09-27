using MedMateAI.Application.IService;
using MedMateAI.Domain.Entities;
using MedMateAI.Domain.Enums;
using MedMateAI.Domain.Persistence;
using MedMateAI.Domain.Repository;
using Microsoft.Extensions.Logging;

namespace MedMateAI.Application.Service;

public sealed class SymptomAnalysisQuotaService
    : ServiceCreditSessionQuotaService<SymptomAnalysisSession>,
      ISymptomAnalysisQuotaService
{
    public const string ReferenceType = "SymptomAnalysisSession";
    public const string FreeQuotaFeature = "symptom-analysis";

    private readonly IGenericRepository<SymptomAnalysisSession> _sessions;
    private readonly IFreeQuotaService _freeQuotaService;

    public SymptomAnalysisQuotaService(
        IServiceCreditService serviceCreditService,
        IFreeQuotaService freeQuotaService,
        IGenericRepository<SymptomAnalysisSession> sessions,
        IUnitOfWork unitOfWork,
        ILogger<SymptomAnalysisQuotaService> logger)
        : base(
            serviceCreditService,
            sessions,
            unitOfWork,
            logger,
            ReferenceType,
            "symptom-analysis",
            "Symptom analysis",
            static session => session.UserId ?? Guid.Empty,
            static session => session.UserSubscriptionId,
            static session => session.UserSubscriptionUsageId,
            static session => GetFinalizeAction(session.Status))
    {
        _sessions = sessions;
        _freeQuotaService = freeQuotaService;
    }

    public override async Task FinalizeAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var session = await _sessions.FirstOrDefaultAsync(
            current => current.Id == sessionId && !current.IsDeleted,
            cancellationToken: cancellationToken);

        if (session is not { QuotaSource: QuotaSource.Free })
        {
            await base.FinalizeAsync(sessionId, cancellationToken);
            return;
        }

        var actionType = GetFinalizeAction(session.Status);
        if (!actionType.HasValue || !session.FreeQuotaUsageId.HasValue || !session.UserId.HasValue)
        {
            return;
        }

        await _freeQuotaService.FinalizeAsync(
            session.FreeQuotaUsageId.Value,
            session.UserId.Value,
            FreeQuotaFeature,
            actionType.Value,
            ReferenceType,
            session.Id,
            cancellationToken);
    }

    private static SubscriptionQuotaActionType? GetFinalizeAction(SymptomAnalysisSessionStatus status) =>
        status switch
        {
            SymptomAnalysisSessionStatus.Completed => SubscriptionQuotaActionType.Consume,
            SymptomAnalysisSessionStatus.Failed => SubscriptionQuotaActionType.Release,
            _ => null
        };
}
