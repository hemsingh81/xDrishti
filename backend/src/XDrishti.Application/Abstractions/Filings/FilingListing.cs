namespace XDrishti.Application.Abstractions.Filings;

/// <summary>The result of one scan: accepted entries (ordered by symbol, kind, period end, hash), rejected files (by name), problems of the inbox itself and whether more files than <c>Filings:MaxFiles</c> were left unlisted.</summary>
public sealed record FilingListing(IReadOnlyList<FilingEntry> Accepted, IReadOnlyList<RejectedFiling> Rejected, IReadOnlyList<string> Problems, bool Truncated)
{
    public static FilingListing Empty(params string[] problems) => new([], [], problems, false);
}
