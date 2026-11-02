namespace XDrishti.Domain.Filings;

/// <summary>
/// An instant at which a results event of an instrument happened, for the <see cref="Learning.LossTagKind.NewsResults"/> tag (P15-06, ADR 0182): the broadcast time of a stored <c>results</c> filing, or, when no filing exists for a calendar meeting whose event session lies in the span of a trade,
/// the 09:15 India time of that session (<paramref name="FromCalendar"/>). A description of the past, built from data known after the trade; never a decision-time input.
/// </summary>
/// <param name="At">The instant (whole microseconds, UTC).</param>
/// <param name="FromCalendar">True when the instant is the session open of a calendar meeting, not a broadcast.</param>
public sealed record EventInstant(DateTimeOffset At, bool FromCalendar);
