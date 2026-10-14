namespace XDrishti.Application.Portfolio.DividendImport;

/// <summary>A row of the file with its identity, what it would become and the things worth the owner's attention (<see cref="DividendStatuses.AmountMismatch"/>, <see cref="DividendStatuses.BeforeFirstLot"/>, <see cref="DividendStatuses.PartialNameMatch"/>).</summary>
internal sealed record PlannedDividend(ParsedDividend Row, string ExternalId, DividendStatus Status, DividendMatch Match, IReadOnlyList<string> Warnings);

/// <summary>
/// A file read against an account: every distinct row planned, the date of the account's first lot (null: it has none), the digest that fingerprints what the owner sees, and what a later selection needs — the name index, the identities stored
/// already and the dividends typed in (<see cref="DividendManualIndex"/>).
/// </summary>
internal sealed record DividendPlan(
    DividendFile File,
    IReadOnlyList<PlannedDividend> Rows,
    DateOnly? FirstLotDate,
    string Digest,
    DividendNameIndex Index,
    IReadOnlySet<string> StoredIds,
    DividendManualIndex Manual)
{
    public int Count(DividendStatus status) => Rows.Count(r => r.Status == status);
}
