using MedMateAI.Domain.Common;
using MedMateAI.Domain.Entities;
using MedMateAI.Domain.Enums;
using MedMateAI.Domain.Repository;
using Microsoft.EntityFrameworkCore;

namespace MedMateAI.Infrastructure.Repositories;

public sealed class SymptomAnalysisSessionRepository
    : GenericRepository<SymptomAnalysisSession>, ISymptomAnalysisSessionRepository
{
    private readonly ApplicationDbContext _context;

    public SymptomAnalysisSessionRepository(ApplicationDbContext context)
        : base(context)
    {
        _context = context;
    }

    public async Task<bool> TryMarkSubmittedAsync(
        Guid sessionId,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        var affectedRows = await _context.SymptomAnalysisSessions
            .Where(session =>
                session.Id == sessionId
                && !session.IsDeleted
                && session.Status == SymptomAnalysisSessionStatus.Processing
                && session.SubmittedAt == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(session => session.SubmittedAt, utcNow)
                    .SetProperty(session => session.UpdatedAt, utcNow),
                cancellationToken);

        return affectedRows == 1;
    }

    public async Task<IReadOnlyList<Guid>> GetAbandonedSessionIdsAsync(
        DateTime createdBefore,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        return await _context.SymptomAnalysisSessions
            .AsNoTracking()
            .Where(session =>
                !session.IsDeleted
                && session.Status == SymptomAnalysisSessionStatus.Processing
                && session.SubmittedAt == null
                && session.CreatedAt < createdBefore)
            .OrderBy(session => session.CreatedAt)
            .Select(session => session.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> TryMarkAbandonedAsFailedAsync(
        Guid sessionId,
        DateTime createdBefore,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        var affectedRows = await _context.SymptomAnalysisSessions
            .Where(session =>
                session.Id == sessionId
                && !session.IsDeleted
                && session.Status == SymptomAnalysisSessionStatus.Processing
                && session.SubmittedAt == null
                && session.CreatedAt < createdBefore)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(session => session.Status, SymptomAnalysisSessionStatus.Failed)
                    .SetProperty(session => session.UpdatedAt, utcNow),
                cancellationToken);

        return affectedRows == 1;
    }

    public Task<PagedResult<SymptomAnalysisSession>> GetPagedByUserIdAsync(
        Guid userId,
        SymptomAnalysisSessionType? sessionType,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        return GetPagedAsync(
            pageNumber,
            pageSize,
            session =>
                !session.IsDeleted
                && session.UserId == userId
                && (!sessionType.HasValue || session.SessionType == sessionType.Value),
            query => query
                .OrderByDescending(session => session.CreatedAt)
                .ThenByDescending(session => session.Id),
            asNoTracking: true,
            cancellationToken);
    }

    public Task<PagedResult<SymptomAnalysisSession>> GetPagedAllAsync(
        SymptomAnalysisSessionType? sessionType,
        SymptomAnalysisSessionStatus? status,
        Guid? userId,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (userId.HasValue && userId.Value == Guid.Empty)
        {
            return GetPagedAsync(
                pageNumber,
                pageSize,
                session => false,
                cancellationToken: cancellationToken);
        }

        return GetPagedAsync(
            pageNumber,
            pageSize,
            session =>
                !session.IsDeleted
                && (!sessionType.HasValue || session.SessionType == sessionType.Value)
                && (!status.HasValue || session.Status == status.Value)
                && (!userId.HasValue || session.UserId == userId.Value),
            query => query
                .OrderByDescending(session => session.CreatedAt)
                .ThenByDescending(session => session.Id),
            asNoTracking: true,
            cancellationToken);
    }
}
