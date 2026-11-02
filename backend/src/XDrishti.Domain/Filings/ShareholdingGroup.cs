namespace XDrishti.Domain.Filings;

/// <summary>The rows of one (symbol, period end) that share one time: one envelope with up to four lines. <see cref="Line"/> is the first csv line of the group (for a reject). <see cref="SumOff"/> is set when the four categories are all present and do not add up to 100 within 1.0 point (counted, never corrected).</summary>
public sealed record ShareholdingGroup(string Symbol, DateOnly PeriodEnd, BroadcastTime Broadcast, IReadOnlyList<ShareholdingLine> Lines, bool SumOff, int Line)
{
    public const decimal SumTolerance = 1.0m;

    public DateTimeOffset? Time => Broadcast.State == BroadcastState.Ok ? NewFiling.ToMicroseconds(Broadcast.Instant) : null;

    public static bool IsSumOff(IReadOnlyCollection<ShareholdingLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var values = ShareholdingCsv.Categories.Select(c => lines.FirstOrDefault(l => l.Category == c)?.Pct).ToList();
        return values.All(v => v is not null) && Math.Abs(values.Sum(v => v!.Value) - 100m) > SumTolerance;
    }
}
