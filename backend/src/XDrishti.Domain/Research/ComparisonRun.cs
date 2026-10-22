using System.Text;

namespace XDrishti.Domain.Research;

/// <summary>One candidate of a prepared strategy, as the use case sees it (P4-05): its id, the strategy record its run keeps and the hash of the definition without its exits and overlays (the entries part of the key, D10).</summary>
/// <param name="Id">The id of the candidate (<see cref="ExitChoice.IdOf"/>), or the strategy id for a plain run.</param>
/// <param name="Exit">The exit of the candidate; null for a plain run with the built-in's own exit.</param>
/// <param name="Record">The strategy as the run of this candidate records it; its definition hash includes the exit, so the run keys differ.</param>
/// <param name="EntriesDefinitionSha256">SHA-256 of the canonical definition with <c>exits</c> and <c>overlays</c> removed: equal for every candidate of one comparison.</param>
public sealed record CandidateRecord(string Id, ExitCandidate? Exit, StrategyRecord Record, string EntriesDefinitionSha256);

/// <summary>
/// A comparison (P4-05): one run record per candidate, the report that sets them side by side and the two keys. The <see cref="EntriesKey"/> is the same for every candidate and for any other run that differs from it only in the exit (the join on
/// <c>trade_key</c> is then meaningful); the <see cref="ComparisonKey"/> names this comparison, with its members.
/// </summary>
/// <param name="Candidates">The candidates in order, each with the run of that exit.</param>
/// <param name="Report">The table, the paired figures and the diagnostics.</param>
/// <param name="EntriesKey">Hash of everything but the exit.</param>
public sealed record ComparisonRun(IReadOnlyList<CandidateRun> Candidates, ComparisonReport Report, string EntriesKey)
{
    /// <summary>
    /// The lower-case hex SHA-256 of <c>xd-comparison 1</c>, the entries key and the run key of each candidate in order, one per line: equal for equal members, different when a member, the order or the entries change.
    /// </summary>
    public string ComparisonKey
    {
        get
        {
            var text = new StringBuilder("xd-comparison 1\nentries=").Append(EntriesKey).Append('\n');
            foreach (var candidate in Candidates)
            {
                text.Append("member=").Append(candidate.Run.RunKey).Append('\n');
            }

            return CanonicalText.Sha256Hex(text.ToString());
        }
    }
}
