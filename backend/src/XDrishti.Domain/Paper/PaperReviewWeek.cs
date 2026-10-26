using XDrishti.Domain.Learning;
using XDrishti.Domain.Market;

namespace XDrishti.Domain.Paper;

/// <summary>
/// The ISO week of a paper review (spec P10-03, criterion 9): the same week key and the same storing rule as the learning digest (ADR 0094), because the review reads today's state exactly as the digest does.
/// </summary>
/// <param name="Week">The ISO week.</param>
public readonly record struct PaperReviewWeek(DigestWeek Week)
{
    /// <summary>The key, <c>2026-W43</c>.</summary>
    public string Key => Week.Key;

    /// <summary>The Monday.</summary>
    public DateOnly Start => Week.Start;

    /// <summary>The Sunday.</summary>
    public DateOnly End => Week.End;

    /// <summary>The week a date falls in.</summary>
    public static PaperReviewWeek Of(DateOnly date) => new(DigestWeek.Of(date));

    /// <summary>Reads <c>YYYY-Www</c>; null when it is not a week that exists.</summary>
    public static PaperReviewWeek? TryParse(string? text) => DigestWeek.TryParse(text) is { } w ? new PaperReviewWeek(w) : null;

    /// <summary>Whether the week has not started at <paramref name="now"/> (India time).</summary>
    public bool IsFuture(DateTimeOffset now) => Start > IndiaTime.Today(now);

    /// <summary>Why the review of this week may not be stored at <paramref name="now"/>, or null (current week from Saturday 10:00 IST, or the most recent completed week).</summary>
    public string? StoreRefusal(DateTimeOffset now) => Week.StoreRefusal(now)?.Replace("digest", "review", StringComparison.Ordinal);
}
