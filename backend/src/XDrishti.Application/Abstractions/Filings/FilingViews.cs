namespace XDrishti.Application.Abstractions.Filings;

/// <summary>Metadata of one envelope (no file content, no path, no source reference).</summary>
public sealed record FilingView(Guid Id, string Symbol, string Kind, DateOnly? PeriodEnd, DateTimeOffset? BroadcastTs, string BroadcastState, string? UnavailableReason, DateTimeOffset IngestedAt, string ContentSha256, long FileBytes, string FileName, Guid? Supersedes);

/// <summary>Counts of one kind in one quarter: stored with a time, stored without one (by reason), superseded by a revision, and stored out of order.</summary>
public sealed record FilingCoverageCell(string Kind, DateOnly QuarterEnd, int Ok, int Superseded, int OutOfOrder, IReadOnlyDictionary<string, int> Unavailable);

/// <summary>What the reader counted for a range: the cells, the tracked instruments and, per kind, the tracked instruments with no filing of that kind in the range.</summary>
public sealed record FilingCoverageData(IReadOnlyList<FilingCoverageCell> Cells, int TrackedInstruments, IReadOnlyDictionary<string, int> InstrumentsWithout);
