using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Filings;

/// <summary>One stored fact of a filing: a field for a span and basis, its state (<c>value</c> or <c>unavailable</c>), the number as filed or the reason, and the unit.</summary>
public sealed record FilingFactView(string Field, string Span, string Basis, string State, decimal? Value, string Unit, string? Reason);

/// <summary>
/// <c>GET /api/filings/{id}/facts</c>: the parse outcome of one filing with the parser version, the stored facts and the counts the parser kept (skipped contexts, sanity flags). <c>Outcome</c> is <c>not_parsed</c> until the parse step has run. No file content, no path.
/// </summary>
public sealed record FilingFactsView(Guid FilingId, string ParserVersion, string Outcome, string? Reason, int FactCount, IReadOnlyDictionary<string, int> Flags, IReadOnlyList<FilingFactView> Facts);

public sealed record GetFilingFactsQuery(Guid Id) : IQuery<Result<FilingFactsView>>;
