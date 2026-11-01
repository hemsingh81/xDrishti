namespace XDrishti.Application.OptionQuality;

/// <summary>One check of a session as the API shows it: the code, the status word (<c>pass</c>, <c>warn</c>, <c>fail</c>, <c>unavailable</c>), the measured share, the threshold it was judged by and the reason. No price, IV or Greek.</summary>
public sealed record OptionCoverageCheckView(string Code, string Status, double? Value, double? Threshold, string? Reason);

/// <summary>One session of the archive: a chain session of the underlying, or one rolling series (<paramref name="FetchKey"/>) on a day.</summary>
public sealed record OptionCoverageSessionView(DateOnly Date, string Source, string? FetchKey, IReadOnlyList<OptionCoverageCheckView> Checks, bool Usable);

/// <summary>The checked sessions of one underlying in a range and the cumulative count of usable chain sessions up to the end of the range (the number P14-07 compares with 60). <paramref name="Truncated"/> is true when more rolling sessions exist than are listed.</summary>
public sealed record OptionCoverageView(string Underlying, IReadOnlyList<OptionCoverageSessionView> Sessions, int UsableChainSessions, bool Truncated);
