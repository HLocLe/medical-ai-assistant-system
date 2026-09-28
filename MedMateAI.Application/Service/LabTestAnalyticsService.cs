using MedMateAI.Application.DTOs.LabTests.Analytics;
using MedMateAI.Application.IRepository;
using MedMateAI.Application.IService;
using MedMateAI.Application.Models.Analytics;
using MedMateAI.Application.Models.LabTests.Analytics;
using MedMateAI.Domain.Enums;

namespace MedMateAI.Application.Service;

public sealed class LabTestAnalyticsService : ILabTestAnalyticsService
{
    private const double DeviationComparisonTolerance = 0.01d;

    private readonly ILabTestAnalyticsRepository _repository;

    public LabTestAnalyticsService(ILabTestAnalyticsRepository repository)
    {
        _repository = repository;
    }

    public async Task<AnalyticsOperationResult<IReadOnlyList<LabTestTrendIndicatorResponse>>>
        GetAvailableIndicatorsAsync(
            Guid userId,
            DateOnly? from,
            DateOnly? to,
            CancellationToken cancellationToken = default)
    {
        var validationError = ValidateRequest(userId, from, to);
        if (validationError is not null)
        {
            return AnalyticsOperationResult<IReadOnlyList<LabTestTrendIndicatorResponse>>.Fail(
                validationError.Value.Error,
                validationError.Value.Message);
        }

        var indicators = await _repository.GetAvailableIndicatorsAsync(
            userId,
            from,
            to,
            cancellationToken);

        var response = indicators
            .Select(indicator => new LabTestTrendIndicatorResponse
            {
                IndicatorId = indicator.IndicatorId,
                Symbol = indicator.Symbol,
                Name = indicator.Name ?? indicator.Symbol,
                Unit = NormalizeUnit(indicator.Unit),
                MeasurementCount = indicator.MeasurementCount,
                FirstTestDate = indicator.FirstTestDate,
                LatestTestDate = indicator.LatestTestDate
            })
            .ToList();

        return AnalyticsOperationResult<IReadOnlyList<LabTestTrendIndicatorResponse>>.Ok(
            response);
    }

    public async Task<AnalyticsOperationResult<LabTestIndicatorTrendResponse>>
        GetIndicatorTrendAsync(
            Guid userId,
            Guid indicatorId,
            DateOnly? from,
            DateOnly? to,
            CancellationToken cancellationToken = default)
    {
        if (indicatorId == Guid.Empty)
        {
            return AnalyticsOperationResult<LabTestIndicatorTrendResponse>.Fail(
                AnalyticsErrorCode.InvalidRequest,
                "Indicator ID is invalid.");
        }

        var validationError = ValidateRequest(userId, from, to);
        if (validationError is not null)
        {
            return AnalyticsOperationResult<LabTestIndicatorTrendResponse>.Fail(
                validationError.Value.Error,
                validationError.Value.Message);
        }

        var repositoryMeasurements = await _repository.GetIndicatorMeasurementsAsync(
            userId,
            indicatorId,
            from,
            to,
            cancellationToken);
        var measurements = repositoryMeasurements
            .Where(measurement => double.IsFinite(measurement.Value))
            .OrderBy(measurement => measurement.TestDate)
            .ThenBy(measurement => measurement.SessionId)
            .ThenBy(measurement => measurement.ResultDetailId)
            .ToList();

        if (measurements.Count == 0)
        {
            return AnalyticsOperationResult<LabTestIndicatorTrendResponse>.Fail(
                AnalyticsErrorCode.LabTestTrendNotFound,
                "No chartable measurements were found for this indicator.");
        }

        var latest = measurements[^1];
        var previous = measurements.LastOrDefault(measurement =>
            measurement.TestDate < latest.TestDate);
        var persistedUnits = measurements
            .Select(measurement => NormalizeUnit(measurement.ReferenceUnit))
            .Where(unit => unit is not null)
            .Select(unit => unit!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var response = new LabTestIndicatorTrendResponse
        {
            IndicatorId = latest.IndicatorId,
            Symbol = latest.Symbol,
            Name = latest.Name ?? latest.Symbol,
            Unit = GetComparableUnit(latest),
            MeasurementCount = measurements.Count,
            LatestValue = latest.Value,
            PreviousValue = previous?.Value,
            Trend = ClassifyTrend(previous, latest),
            HasMixedUnits = persistedUnits.Count > 1,
            Points = measurements
                .Select(ToPointResponse)
                .ToList()
        };

        return AnalyticsOperationResult<LabTestIndicatorTrendResponse>.Ok(response);
    }

    private static LabTestTrendPointResponse ToPointResponse(
        LabTestTrendMeasurementData measurement)
    {
        return new LabTestTrendPointResponse
        {
            SessionId = measurement.SessionId,
            TestDate = measurement.TestDate,
            Value = measurement.Value,
            Status = measurement.Status,
            ReferenceMin = measurement.ReferenceMin,
            ReferenceMax = measurement.ReferenceMax,
            Unit = NormalizeUnit(measurement.ReferenceUnit),
            DeviationPercent = measurement.DeviationPercent,
            FacilityName = measurement.FacilityName
        };
    }

    private static LabTestTrendClassification ClassifyTrend(
        LabTestTrendMeasurementData? previous,
        LabTestTrendMeasurementData latest)
    {
        if (previous is null
            || previous.TestDate == latest.TestDate
            || !HaveCompatibleUnits(previous, latest))
        {
            return LabTestTrendClassification.InsufficientData;
        }

        if (previous.Status == LabResultStatus.Normal
            && latest.Status == LabResultStatus.Normal)
        {
            return LabTestTrendClassification.InRange;
        }

        if (IsAbnormal(previous.Status) && latest.Status == LabResultStatus.Normal)
        {
            return LabTestTrendClassification.TowardReferenceRange;
        }

        if (previous.Status == LabResultStatus.Normal && IsAbnormal(latest.Status))
        {
            return LabTestTrendClassification.AwayFromReferenceRange;
        }

        if (!IsAbnormal(previous.Status)
            || !IsAbnormal(latest.Status)
            || !IsUsableDeviation(previous.DeviationPercent)
            || !IsUsableDeviation(latest.DeviationPercent))
        {
            return LabTestTrendClassification.InsufficientData;
        }

        var difference = latest.DeviationPercent!.Value - previous.DeviationPercent!.Value;
        if (Math.Abs(difference) <= DeviationComparisonTolerance)
        {
            return LabTestTrendClassification.Stable;
        }

        return difference < 0
            ? LabTestTrendClassification.TowardReferenceRange
            : LabTestTrendClassification.AwayFromReferenceRange;
    }

    private static bool HaveCompatibleUnits(
        LabTestTrendMeasurementData previous,
        LabTestTrendMeasurementData latest)
    {
        var previousUnit = GetComparableUnit(previous);
        var latestUnit = GetComparableUnit(latest);

        if (previousUnit is null || latestUnit is null)
        {
            return previousUnit is null && latestUnit is null;
        }

        return string.Equals(
            previousUnit,
            latestUnit,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetComparableUnit(LabTestTrendMeasurementData measurement) =>
        NormalizeUnit(measurement.ReferenceUnit)
        ?? NormalizeUnit(measurement.IndicatorUnit);

    private static string? NormalizeUnit(string? unit) =>
        string.IsNullOrWhiteSpace(unit) ? null : unit.Trim();

    private static bool IsAbnormal(LabResultStatus status) =>
        status is LabResultStatus.High or LabResultStatus.Low;

    private static bool IsUsableDeviation(double? deviation) =>
        deviation.HasValue
        && double.IsFinite(deviation.Value)
        && deviation.Value >= 0;

    private static (AnalyticsErrorCode Error, string Message)? ValidateRequest(
        Guid userId,
        DateOnly? from,
        DateOnly? to)
    {
        if (userId == Guid.Empty)
        {
            return (AnalyticsErrorCode.InvalidRequest, "User ID is invalid.");
        }

        if (from.HasValue && to.HasValue && from.Value > to.Value)
        {
            return (
                AnalyticsErrorCode.InvalidDateRange,
                "The from date must be on or before the to date.");
        }

        return null;
    }
}
