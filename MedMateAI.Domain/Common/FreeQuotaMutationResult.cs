namespace MedMateAI.Domain.Common;

public sealed record FreeQuotaMutationResult(
    Guid UsageId,
    Guid UserId,
    int LimitValue,
    int UsedCountBefore,
    int UsedCountAfter,
    int ReservedCountBefore,
    int ReservedCountAfter);
