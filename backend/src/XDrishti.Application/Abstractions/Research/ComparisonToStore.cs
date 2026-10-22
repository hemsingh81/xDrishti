namespace XDrishti.Application.Abstractions.Research;

/// <summary>One member of a stored comparison: the key of its stored run, its candidate id and the entries a gated run would have skipped under its exit (P4-05, D10).</summary>
/// <param name="RunKey">The run key of the member; the run is stored on its own.</param>
/// <param name="CandidateId">The id of the candidate, <c>FIXED_RR{rr=2}</c>.</param>
/// <param name="GatedBlocked">The gated count of the candidate.</param>
public sealed record ComparisonMemberToStore(string RunKey, string CandidateId, long GatedBlocked);

/// <summary>
/// The text of a comparison as the store keeps it (P4-05, D10): <c>comparison.csv</c> and <c>diagnostics.csv</c> in the canonical form of the export (the report to a fixed precision) and the SHA-256 of the pair, made by the verb from the report it
/// printed, as <see cref="RunRecordText"/> is for a run.
/// </summary>
public sealed record ComparisonRecordText
{
    /// <summary>The text and its digest.</summary>
    /// <exception cref="ArgumentException">A text is empty or the digest is not 64 lower-case hex characters.</exception>
    public ComparisonRecordText(string comparisonCsv, string diagnosticsCsv, string recordSha256)
    {
        ArgumentException.ThrowIfNullOrEmpty(comparisonCsv);
        ArgumentException.ThrowIfNullOrEmpty(diagnosticsCsv);
        ComparisonCsv = comparisonCsv;
        DiagnosticsCsv = diagnosticsCsv;
        RecordSha256 = RunRecordText.IsHex64(recordSha256) ? recordSha256 : throw new ArgumentException("A digest is 64 lower-case hex characters.", nameof(recordSha256));
    }

    /// <summary>The table of the comparison (<c>comparison.csv</c>).</summary>
    public string ComparisonCsv { get; }

    /// <summary>The MAE/MFE table (<c>diagnostics.csv</c>).</summary>
    public string DiagnosticsCsv { get; }

    /// <summary>The SHA-256 of the UTF-8 bytes of <c>comparison.csv</c>, a line feed and <c>diagnostics.csv</c>.</summary>
    public string RecordSha256 { get; }
}

/// <summary>What the store is given for a comparison (P4-05, D10): the keys, the members, the common-set count, the text with its digest, who stores it and the instant (read once by the handler from <c>TimeProvider</c>).</summary>
/// <param name="ComparisonKey">The key of the comparison (<c>ComparisonRun.ComparisonKey</c>); the idempotency key.</param>
/// <param name="EntriesKey">The key of everything but the exit; equal for runs that can be joined on <c>trade_key</c>.</param>
/// <param name="StrategyId">The strategy.</param>
/// <param name="Unit"><c>RFilled</c> or <c>RNet</c>.</param>
/// <param name="CommonEntries">The entries taken under every candidate.</param>
/// <param name="Members">The candidates in order; at least two.</param>
/// <param name="Record">The text and its digest.</param>
/// <param name="Actor">Who stores it.</param>
/// <param name="CreatedAt">When.</param>
public sealed record ComparisonToStore(
    string ComparisonKey,
    string EntriesKey,
    string StrategyId,
    string Unit,
    int CommonEntries,
    IReadOnlyList<ComparisonMemberToStore> Members,
    ComparisonRecordText Record,
    string Actor,
    DateTimeOffset CreatedAt);

/// <summary>The header of a stored comparison.</summary>
/// <param name="Id">The row id.</param>
/// <param name="ComparisonKey">The key.</param>
/// <param name="EntriesKey">The entries key.</param>
/// <param name="StrategyId">The strategy.</param>
/// <param name="Unit">The unit of the table.</param>
/// <param name="CommonEntries">The entries taken under every candidate.</param>
/// <param name="Members">The members in order.</param>
/// <param name="RecordSha256">The digest of the text.</param>
/// <param name="CreatedAt">When it was stored.</param>
/// <param name="CreatedBy">Who stored it.</param>
public sealed record StoredComparisonHeader(
    long Id,
    string ComparisonKey,
    string EntriesKey,
    string StrategyId,
    string Unit,
    int CommonEntries,
    IReadOnlyList<ComparisonMemberToStore> Members,
    string RecordSha256,
    DateTimeOffset CreatedAt,
    string CreatedBy);
