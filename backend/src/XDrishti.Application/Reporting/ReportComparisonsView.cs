namespace XDrishti.Application.Reporting;

/// <summary>The stored comparisons of exits on identical entries (spec P8-10 part b). Choosing one names its member runs as the run set of a report; the report must then be split by exit (<c>lens=exit</c>).</summary>
/// <param name="Comparisons">Newest first. Empty is an ordinary answer.</param>
/// <param name="Limits">What always holds: in-sample, modelled fills, the same entries.</param>
public sealed record ReportComparisonsView(IReadOnlyList<ComparisonView> Comparisons, IReadOnlyList<string> Limits);

/// <summary>A stored comparison.</summary>
/// <param name="ComparisonKey">The comparison key.</param>
/// <param name="EntriesKey">The key of the common entries.</param>
/// <param name="StrategyId">The strategy.</param>
/// <param name="Unit">RFilled (gross) or RNet.</param>
/// <param name="CommonEntries">Entries every member shares.</param>
/// <param name="Members">The member runs, with the candidate each ran.</param>
/// <param name="CreatedAt">When it was stored.</param>
public sealed record ComparisonView(string ComparisonKey, string EntriesKey, string StrategyId, string Unit, int CommonEntries, IReadOnlyList<ComparisonMemberView> Members, DateTimeOffset CreatedAt);

/// <summary>One member of a comparison.</summary>
/// <param name="RunKey">The stored run key.</param>
/// <param name="CandidateId">The exit candidate id.</param>
public sealed record ComparisonMemberView(string RunKey, string CandidateId);
