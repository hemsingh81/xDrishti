namespace XDrishti.SimExport.Verification;

/// <summary>
/// A line of the check log with no card (spec P3-09, V3): an excluded session, a flagged day or a position the data cut short. The owner checks one fact on an external chart: that the day was what the run record says it was.
/// </summary>
/// <param name="Seq">The place on the sheet, after the trades.</param>
/// <param name="Kind"><c>excluded_session</c>, <c>flagged_day</c> or <c>open_at_end</c>.</param>
/// <param name="Detail">For an excluded session the reason (<c>special_session</c>, <c>short_session</c>, <c>missing_session</c>); otherwise empty.</param>
/// <param name="Symbol">The instrument.</param>
/// <param name="Date">The day.</param>
/// <param name="RunIndex">The run the line was read from.</param>
public sealed record SpotRow(int Seq, string Kind, string Detail, string Symbol, DateOnly Date, int RunIndex)
{
    /// <summary>The key of the line in the log: <c>spot|kind[:detail]|symbol|date</c>.</summary>
    public string Key => "spot|" + Kind + (Detail.Length == 0 ? string.Empty : ":" + Detail) + "|" + Symbol + "|" + Date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The question the owner answers, in words that say nothing about a trade.</summary>
    public string Question => Kind switch
    {
        SpotChecks.ExcludedSession => "was this day a " + Detail.Replace('_', ' ') + " on an external chart?",
        SpotChecks.FlaggedDay => "does an external chart show a jump on this day that the record flags?",
        SpotChecks.OpenAtEnd => "do the stored bars of this day end where the data does?",
        _ => string.Empty,
    };
}
