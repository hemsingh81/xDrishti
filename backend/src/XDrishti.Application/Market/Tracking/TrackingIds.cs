using XDrishti.Domain.Common;
using XDrishti.Domain.Market;

namespace XDrishti.Application.Market.Tracking;

/// <summary>Checks the instrument ids of a track or untrack request (at the boundary: 1 to 500 entries, none empty).</summary>
internal static class TrackingIds
{
    public static Result<Guid[]> Check(TrackingInput? input)
    {
        var ids = input?.InstrumentIds;
        if (ids is null || ids.Count == 0)
        {
            return Result.Failure<Guid[]>(MarketErrors.Invalid("instrumentIds", "Pick at least one instrument."));
        }

        if (ids.Count > TrackingLimits.MaxIdsPerCall)
        {
            return Result.Failure<Guid[]>(MarketErrors.Invalid("instrumentIds", $"Use at most {TrackingLimits.MaxIdsPerCall} instruments per call."));
        }

        return ids.Contains(Guid.Empty)
            ? Result.Failure<Guid[]>(MarketErrors.Invalid("instrumentIds", "An instrument id is empty."))
            : Result.Success(ids.Distinct().ToArray());
    }
}
