namespace XDrishti.Application.Abstractions.OptionReports;

/// <summary>A stored expression run as the report reads it: the header (counts, the unpaired counts as JSON, the model-price share, the labels) and, over its paired rows only, the number with a derived expiry, with the recent-archive spread label and per lot-feasibility word. No figure of return.</summary>
public sealed record ExpressionRunStored(
    string RunId,
    string CashRunKey,
    string ProfileHash,
    string RemarkVersion,
    DateTimeOffset CreatedAt,
    int Trades,
    int Paired,
    string UnpairedJson,
    double? BsFallbackShare,
    IReadOnlyList<string> Labels,
    int DerivedExpiry,
    int SpreadFromRecentArchive,
    IReadOnlyDictionary<string, int> Sizable);

/// <summary>A stored pair formation: its identity, quarter, window, counts and the detail JSON the half-lives and the Benjamini-Hochberg table are read from.</summary>
public sealed record PairFormationStored(string FormationId, string Family, string Quarter, DateOnly WindowFrom, DateOnly WindowTo, DateTimeOffset CreatedAt, int Candidates, int Tested, int Unavailable, int Selected, string DetailJson);

/// <summary>
/// The read side of the P14 reports (spec P14-16): aggregates of the archive's quality results, the stored expression runs and the stored pair formations. Select only; every read is bounded by a date range, a run id, a profile or a limit,
/// and none returns a price, IV, Greek, bar or quote.
/// </summary>
public interface IOptionReportReader
{
    /// <summary>The underlyings with a chain quality result between the two India dates, at most <paramref name="limit"/>.</summary>
    ValueTask<IReadOnlyList<Guid>> ArchiveUnderlyingsAsync(DateOnly from, DateOnly to, int limit, CancellationToken cancellationToken);

    /// <summary>The newest stored expression runs, of one profile or of all, at most <paramref name="limit"/>.</summary>
    ValueTask<IReadOnlyList<ExpressionRunStored>> ExpressionRunsAsync(string? profileHash, int limit, CancellationToken cancellationToken);

    /// <summary>One stored expression run by its 64-character id, or null.</summary>
    ValueTask<ExpressionRunStored?> ExpressionRunAsync(string runId, CancellationToken cancellationToken);

    /// <summary>The stored formations of the families whose window overlaps the two dates, oldest quarter first, at most <paramref name="limit"/>.</summary>
    ValueTask<IReadOnlyList<PairFormationStored>> PairFormationsAsync(IReadOnlyList<string> families, DateOnly from, DateOnly to, int limit, CancellationToken cancellationToken);
}
