using XDrishti.Domain.Common;

namespace XDrishti.Domain.Planning;

/// <summary>
/// The indicative numbers of a ticket as of the close of <see cref="Session"/> (spec P7-19, ADR 0109): entry reference, stop, target. Rupees, four decimals. A reading of the close, not tomorrow's price and not an order.
/// Built only through <see cref="Create"/>, which refuses a wrong orientation, a non-positive level and an implausible stop distance.
/// </summary>
/// <param name="EntryRef">The close of D.</param>
/// <param name="Session">D.</param>
/// <param name="Stop">The stop, when it is numeric.</param>
/// <param name="Anchor">What the stop is measured from; set exactly when <paramref name="Stop"/> is.</param>
/// <param name="Target">The target of a fixed reward-to-risk exit, when there is a stop.</param>
/// <param name="RewardRisk">That reward-to-risk; set exactly when <paramref name="Target"/> is.</param>
public sealed record TicketLevels(decimal EntryRef, DateOnly Session, decimal? Stop, StopAnchor? Anchor, decimal? Target, decimal? RewardRisk)
{
    /// <summary>A stop this far from the entry (fraction of it) or further is withheld as a probable data fault.</summary>
    public const decimal MaxRiskFraction = 0.30m;

    /// <summary>Reason code when the stop distance is zero, on the wrong side or above <see cref="MaxRiskFraction"/>.</summary>
    public const string ImplausibleCode = "levels:implausible_stop_distance";

    /// <summary>Reason code when the stop has no number.</summary>
    public const string NotNumericCode = "levels:stop_not_numeric";

    /// <summary>Checks and rounds (half away from zero, four decimals). Inconsistent arguments (stop without anchor, target without stop) are invalid.</summary>
    public static Result<TicketLevels> Create(string side, decimal entryRef, DateOnly session, decimal? stop, StopAnchor? anchor, decimal? target, decimal? rewardRisk)
    {
        var buy = side == "BUY";
        if ((!buy && side != "SELL") || entryRef <= 0 || (stop is null) != (anchor is null) || (target is null) != (rewardRisk is null) || (target is not null && stop is null) || rewardRisk <= 0)
        {
            return Result.Failure<TicketLevels>(Error.Validation("levels:invalid", "The levels are inconsistent."));
        }

        var entry = Round(entryRef);
        if (stop is null)
        {
            return Result.Success(new TicketLevels(entry, session, null, null, null, null));
        }

        var s = Round(stop.Value);
        var distance = Math.Abs(entry - s);
        if (s <= 0 || distance == 0 || distance > MaxRiskFraction * entry || (buy ? s >= entry : s <= entry))
        {
            return Result.Failure<TicketLevels>(Error.Validation(ImplausibleCode, "The stop distance is zero, on the wrong side or too large."));
        }

        decimal? t = target is { } raw ? Round(raw) : null;
        if (t is { } tv && (tv <= 0 || (buy ? tv <= entry : tv >= entry)))
        {
            return Result.Failure<TicketLevels>(Error.Validation(ImplausibleCode, "The target is on the wrong side."));
        }

        return Result.Success(new TicketLevels(entry, session, s, anchor, t, rewardRisk is { } r ? Round(r) : null));
    }

    private static decimal Round(decimal value) => Math.Round(value, 4, MidpointRounding.AwayFromZero);
}
