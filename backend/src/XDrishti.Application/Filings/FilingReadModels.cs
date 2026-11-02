using XDrishti.Application.Abstractions.Filings;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Filings;

/// <summary>A count of stored filings without a broadcast time, by closed reason (<c>absent</c>, <c>date_only</c>, <c>no_offset</c>, <c>unparsable</c>, <c>before_2000</c>, <c>broadcast_in_future</c>).</summary>
public sealed record FilingReasonCount(string Reason, int Count);

/// <summary>Counts of one kind in one quarter (by the period end, else the broadcast or ingest date in India time): <c>Ok</c> with a time, <c>Unavailable</c> without one, <c>Superseded</c> by a later revision, <c>OutOfOrder</c> stored unlinked.</summary>
public sealed record FilingQuarterCoverage(DateOnly QuarterEnd, int Ok, IReadOnlyList<FilingReasonCount> Unavailable, int Superseded, int OutOfOrder);

/// <summary>One kind: <c>State</c> is <c>value</c> when anything is stored in the range, else <c>unavailable</c> (never a bare zero); <c>InstrumentsWithout</c> counts tracked instruments with no filing of the kind in the range.</summary>
public sealed record FilingKindCoverage(string Kind, string State, int InstrumentsWithout, IReadOnlyList<FilingQuarterCoverage> Quarters);

/// <summary>The coverage of the filing store between <c>From</c> and <c>To</c>: counts only.</summary>
public sealed record FilingCoverageView(DateOnly From, DateOnly To, int TrackedInstruments, IReadOnlyList<FilingKindCoverage> Kinds);

/// <summary>Envelope metadata, at most 200 rows. With <c>asOf</c> the <c>State</c> is the point-in-time state (<c>value</c>, <c>none_visible</c>, <c>unavailable</c>) and <c>UnavailableCount</c> the filings of the instrument and kind without a broadcast time; without it both are null/0.</summary>
public sealed record FilingListView(string? State, int UnavailableCount, IReadOnlyList<FilingView> Items, bool Truncated);

public sealed record GetFilingCoverageQuery(DateOnly? From, DateOnly? To) : IQuery<Result<FilingCoverageView>>;

public sealed record ListFilingsQuery(string? Instrument, string? Kind, string? AsOf) : IQuery<Result<FilingListView>>;

public sealed record GetFilingQuery(Guid Id) : IQuery<Result<FilingView>>;
