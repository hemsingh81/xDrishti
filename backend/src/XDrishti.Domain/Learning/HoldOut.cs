using XDrishti.Domain.Common;

namespace XDrishti.Domain.Learning;

/// <summary>
/// The hold-out rule (P6-00 question 2, ADR 0067): the last months of the data are kept out of every tuning and model choice and are used once. Starts on <c>lastDate - months + 1 day</c>. A development row whose label horizon (resolved instant plus one bar)
/// reaches the first decision of the hold-out is purged, so no development label is decided by hold-out prices. Whether the hold-out has been spent is a record kept by the phase check (P6-18); this type only cuts it.
/// </summary>
public static class HoldOut
{
    /// <summary>One bar, in seconds, added to a resolved instant (ADR 0065).</summary>
    public const long BarSeconds = 60;

    /// <summary>Cuts <paramref name="rows"/> into development and hold-out.</summary>
    /// <param name="rows">All labelled rows.</param>
    /// <param name="months">Months to reserve (6).</param>
    public static Result<HoldOutSplit> Reserve(IReadOnlyList<ValidationRow> rows, int months = 6)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (months is < 1 or > 120)
        {
            return Result.Failure<HoldOutSplit>(Error.Validation("validation.holdout_months", "Hold-out months must be 1 to 120."));
        }

        if (rows.Count == 0)
        {
            return Result.Failure<HoldOutSplit>(Error.Validation("validation.no_rows", "There are no rows."));
        }

        var last = rows.Max(r => r.TradeDate);
        var start = last.AddMonths(-months).AddDays(1);
        var reserved = rows.Where(r => r.TradeDate >= start).ToList();
        if (reserved.Count == 0 || rows.Min(r => r.TradeDate) >= start)
        {
            return Result.Failure<HoldOutSplit>(Error.Validation("validation.holdout_empty", "The hold-out or the development part would be empty."));
        }

        var firstDecision = reserved.Min(r => r.DecisionEpochSeconds);
        var before = rows.Where(r => r.TradeDate < start).ToList();
        var development = before.Where(r => r.ResolvedEpochSeconds + BarSeconds < firstDecision).ToList();
        return Result.Success(new HoldOutSplit(development, reserved, start, before.Count - development.Count));
    }
}
