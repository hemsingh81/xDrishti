namespace XDrishti.Application.OptionReports;

/// <summary>The half-lives (in sessions) of the pairs a formation selected: how many, the smallest, the median and the largest; <c>unavailable</c> when it selected none.</summary>
public sealed record HalfLifeView(string State, string? Reason, int? Count, double? Min, double? Median, double? Max);

/// <summary>One stored formation of a quarter (spec P14-16): the candidates, the tested, the unavailable, the selected, the Benjamini-Hochberg threshold at its last rejection and the half-life distribution. No spread, price or test statistic.</summary>
public sealed record PairFormationSummary(
    string Id,
    string Family,
    string Quarter,
    DateOnly WindowFrom,
    DateOnly WindowTo,
    DateTimeOffset CreatedAt,
    int Candidates,
    int Tested,
    int Unavailable,
    int Selected,
    ReportedShare BhThreshold,
    HalfLifeView HalfLives);

/// <summary>The stored formations whose window overlaps the range, oldest quarter first; <paramref name="State"/> is <c>unavailable</c> with the reason <c>no_runs</c> when none is stored. <paramref name="Truncated"/> is true when more than 400 formations overlap the range: the oldest 400 are listed.</summary>
public sealed record PairFormationList(string State, string? Reason, DateOnly From, DateOnly To, IReadOnlyList<PairFormationSummary> Items, bool Truncated);
