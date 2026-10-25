namespace XDrishti.Domain.Reporting;

/// <summary>The daily closes of one instrument, as stored (P8-07).</summary>
/// <param name="Id">The instrument id: exclusions are keyed by it, because two instruments can share a symbol.</param>
/// <param name="Symbol">The symbol, for naming an exclusion.</param>
/// <param name="Closes">The closes, in any order; the computation sorts them.</param>
public sealed record InstrumentSeries(Guid Id, string Symbol, IReadOnlyList<DailyClose> Closes);
