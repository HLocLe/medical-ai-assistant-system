using MedMateAI.Application.IRepository;
using MedMateAI.Application.Models.LabTests.Analytics;
using MedMateAI.Domain.Entities;
using MedMateAI.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MedMateAI.Infrastructure.Repositories;

public sealed class LabTestAnalyticsRepository : ILabTestAnalyticsRepository
{
    private readonly ApplicationDbContext _context;

    public LabTestAnalyticsRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<LabTestTrendIndicatorData>>
        GetAvailableIndicatorsAsync(
            Guid userId,
            DateOnly? from,
            DateOnly? to,
            CancellationToken cancellationToken = default)
    {
        var query = ApplyDateRange(BuildChartableMeasurements(userId), from, to);

        var rows = await query
            .Select(detail => new
            {
                IndicatorId = detail.IndicatorId!.Value,
                Symbol = detail.Indicator!.Symbol,
                Name = detail.Indicator.FullName,
                Unit = detail.Indicator.Unit,
                SessionCreatedAt = detail.TestSession.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(row => new
            {
                row.IndicatorId,
                row.Symbol,
                row.Name,
                row.Unit
            })
            .Select(group => new LabTestTrendIndicatorData(
                group.Key.IndicatorId,
                group.Key.Symbol,
                group.Key.Name,
                group.Key.Unit,
                group.Count(),
                group.Min(row => DateOnly.FromDateTime(row.SessionCreatedAt)),
                group.Max(row => DateOnly.FromDateTime(row.SessionCreatedAt))))
            .OrderBy(indicator => indicator.Symbol)
            .ThenBy(indicator => indicator.IndicatorId)
            .ToList();
    }

    public async Task<IReadOnlyList<LabTestTrendMeasurementData>>
        GetIndicatorMeasurementsAsync(
            Guid userId,
            Guid indicatorId,
            DateOnly? from,
            DateOnly? to,
            CancellationToken cancellationToken = default)
    {
        var query = ApplyDateRange(BuildChartableMeasurements(userId), from, to)
            .Where(detail => detail.IndicatorId == indicatorId);

        var rows = await query
            .OrderBy(detail => detail.TestSession.CreatedAt)
            .ThenBy(detail => detail.TestSessionId)
            .ThenBy(detail => detail.Id)
            .Select(detail => new
            {
                detail.Id,
                detail.TestSessionId,
                IndicatorId = detail.IndicatorId!.Value,
                Symbol = detail.Indicator!.Symbol,
                Name = detail.Indicator.FullName,
                IndicatorUnit = detail.Indicator.Unit,
                SessionCreatedAt = detail.TestSession.CreatedAt,
                Value = detail.UserValue!.Value,
                detail.Status,
                detail.ReferenceMinUsed,
                detail.ReferenceMaxUsed,
                detail.ReferenceUnitUsed,
                detail.DeviationPercent,
                detail.TestSession.FacilityName
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(row => new LabTestTrendMeasurementData(
                row.Id,
                row.TestSessionId,
                row.IndicatorId,
                row.Symbol,
                row.Name,
                row.IndicatorUnit,
                row.SessionCreatedAt,
                DateOnly.FromDateTime(row.SessionCreatedAt),
                row.Value,
                row.Status,
                row.ReferenceMinUsed,
                row.ReferenceMaxUsed,
                row.ReferenceUnitUsed,
                row.DeviationPercent,
                row.FacilityName))
            .ToList();
    }

    private IQueryable<LabTestResultDetail> BuildChartableMeasurements(Guid userId)
    {
        return _context.LabTestResultDetails
            .AsNoTracking()
            .Where(detail =>
                !detail.IsDeleted
                && detail.IsMatched
                && detail.IndicatorId.HasValue
                && detail.UserValue.HasValue
                && detail.Indicator != null
                && !detail.Indicator.IsDeleted
                && !detail.TestSession.IsDeleted
                && detail.TestSession.UserId == userId
                && detail.TestSession.Status == LabTestSessionStatus.Completed);
    }

    private static IQueryable<LabTestResultDetail> ApplyDateRange(
        IQueryable<LabTestResultDetail> query,
        DateOnly? from,
        DateOnly? to)
    {
        if (from.HasValue)
        {
            var fromUtc = from.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            query = query.Where(detail => detail.TestSession.CreatedAt >= fromUtc);
        }

        if (to.HasValue)
        {
            var toExclusiveUtc = to.Value.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            query = query.Where(detail => detail.TestSession.CreatedAt < toExclusiveUtc);
        }

        return query;
    }
}
