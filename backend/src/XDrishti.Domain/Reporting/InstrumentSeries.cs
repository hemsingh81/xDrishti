namespace XDrishti.Domain.Reporting;

/// <summary>The daily closes of one instrument, as stored (P8-07).</summary>
/// <param name="Symbol">The symbol, for naming an exclusion.</param>
/// <param name="Closes">The closes, in any order; the computation sorts them.</param>
public sealed record InstrumentSeries(string Symbol, IReadOnlyList<DailyClose> Closes);
