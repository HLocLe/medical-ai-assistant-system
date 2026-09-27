using MedMateAI.Domain.Common;
using MedMateAI.Domain.Entities;
using MedMateAI.Domain.Enums;

namespace MedMateAI.Domain.Repository;

public interface ISymptomAnalysisSessionRepository : IGenericRepository<SymptomAnalysisSession>
{
    Task<PagedResult<SymptomAnalysisSession>> GetPagedByUserIdAsync(
        Guid userId,
        SymptomAnalysisSessionType? sessionType,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<PagedResult<SymptomAnalysisSession>> GetPagedAllAsync(
        SymptomAnalysisSessionType? sessionType,
        SymptomAnalysisSessionStatus? status,
        Guid? userId,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<bool> TryMarkSubmittedAsync(
        Guid sessionId,
        DateTime utcNow,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Guid>> GetAbandonedSessionIdsAsync(
        DateTime createdBefore,
        int batchSize,
        CancellationToken cancellationToken = default);

    Task<bool> TryMarkAbandonedAsFailedAsync(
        Guid sessionId,
        DateTime createdBefore,
        DateTime utcNow,
        CancellationToken cancellationToken = default);
}
