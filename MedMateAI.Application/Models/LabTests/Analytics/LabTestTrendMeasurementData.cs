using MedMateAI.Domain.Enums;

namespace MedMateAI.Application.Models.LabTests.Analytics;

public sealed record LabTestTrendMeasurementData(
    Guid ResultDetailId,
    Guid SessionId,
    Guid IndicatorId,
    string Symbol,
    string? Name,
    string? IndicatorUnit,
    DateTime SessionCreatedAt,
    DateOnly TestDate,
    double Value,
    LabResultStatus Status,
    double? ReferenceMin,
    double? ReferenceMax,
    string? ReferenceUnit,
    double? DeviationPercent,
    string? FacilityName);
