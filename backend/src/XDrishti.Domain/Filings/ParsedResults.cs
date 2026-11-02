namespace XDrishti.Domain.Filings;

/// <summary>
/// What the parser made of one file: an outcome (<c>parsed</c>, <c>rejected</c>, <c>not_xbrl</c>, <c>no_facts</c>), a closed reason, the facts and the counts of what it skipped or flagged (<see cref="Flags"/>: counts only, never a value or an element text).
/// </summary>
public sealed record ParsedResults(string Outcome, string? Reason, IReadOnlyList<ParsedFact> Facts, IReadOnlyDictionary<string, int> Flags)
{
    private static readonly IReadOnlyDictionary<string, int> NoFlags = new Dictionary<string, int>();

    public static ParsedResults Reject(string reason) => new(ResultCodes.Rejected, reason, [], NoFlags);

    public static ParsedResults NotXbrl() => new(ResultCodes.NotXbrl, null, [], NoFlags);

    public static ParsedResults NoFactsBecause(string reason, IReadOnlyDictionary<string, int> flags) => new(ResultCodes.NoFacts, reason, [], flags);
}
