namespace XDrishti.Application.OptionReports;

/// <summary>How many sessions ended in an outcome (<c>pass</c>, <c>warn</c>, <c>fail</c>, <c>unavailable</c> with its reason).</summary>
public sealed record SessionOutcomeView(string Outcome, string? Reason, int Sessions);

/// <summary>The outcomes of the checked chain sessions, or <c>unavailable</c> when no session was ever checked.</summary>
public sealed record GapsView(string State, string? Reason, IReadOnlyList<SessionOutcomeView> Outcomes);

/// <summary>The backfill keys of the underlying by their latest state, or <c>unavailable</c> when the fetch log holds none.</summary>
public sealed record BackfillView(string State, string? Reason, int? Done, int? Empty, int? NoData, int? GivenUp);

/// <summary>One underlying of the option archive: recorded and usable chain sessions, the first and last recorded date, the gaps by outcome, the backfill keys and the half-spread model state. Counts and dates only; no price, IV or Greek.</summary>
public sealed record UnderlyingArchiveSummary(
    string Underlying,
    ReportedCount RecordedSessions,
    ReportedCount UsableSessions,
    ReportedDate FirstDate,
    ReportedDate LastDate,
    GapsView Gaps,
    BackfillView Backfill,
    ReportedCount SpreadModelSessions,
    ReportedDate SpreadModelFirstSession,
    ReportedDate SpreadModelLastSession);

/// <summary>The option archive per underlying (spec P14-16); an empty list when nothing is stored.</summary>
public sealed record OptionArchiveSummaryView(IReadOnlyList<UnderlyingArchiveSummary> Underlyings);
