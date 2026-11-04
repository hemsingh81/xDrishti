namespace XDrishti.Application.OptionReports;

/// <summary>The trades of a run that did not pair, by reason.</summary>
public sealed record UnpairedCountView(string Reason, int Trades);

/// <summary>The paired trades with one lot-feasibility word, and their share of the paired trades.</summary>
public sealed record LotFeasibilityView(string Feasibility, int Paired, ReportedShare Share);

/// <summary>
/// A stored expression run (spec P14-16): the profile, the counts paired and unpaired by reason, and the shares of the paired trades that used the model price, a derived expiry or the recent-archive spread, and per lot-feasibility word. The counts are of the whole run,
/// hold-out included; no figure of return, price or IV is shown.
/// </summary>
public sealed record ExpressionRunSummary(
    string Id,
    string CashRunKey,
    string ProfileHash,
    string RemarkVersion,
    DateTimeOffset CreatedAt,
    int Trades,
    int Paired,
    IReadOnlyList<UnpairedCountView> Unpaired,
    ReportedShare FallbackShare,
    ReportedShare ExpiryDerivedShare,
    ReportedShare SpreadFromRecentArchiveShare,
    IReadOnlyList<LotFeasibilityView> LotFeasibility,
    IReadOnlyList<string> Labels);

/// <summary>The newest stored expression runs, newest first; <paramref name="State"/> is <c>unavailable</c> with the reason <c>no_runs</c> when none is stored.</summary>
public sealed record ExpressionRunList(string State, string? Reason, IReadOnlyList<ExpressionRunSummary> Items);
