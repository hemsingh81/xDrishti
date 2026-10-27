using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>Computes the cell statistics of stored runs as of a date and keeps them as a snapshot (P6-01). Only trades that exited on or before the date are read.</summary>
/// <param name="AsOf">The as-of date.</param>
/// <param name="Sources">One to 50 stored runs.</param>
/// <param name="Options">The knobs; the doc 16 defaults when not given.</param>
/// <param name="Actor">Who asks (<c>cli:xd</c> for the verb).</param>
/// <param name="ByRegime">Also split every cell by the regime of its trades' entry day (P6-02, ADR 0146); the <c>*</c> rows are the same with and without it. Off by default.</param>
public sealed record ComputeCellStatsCommand(DateOnly AsOf, IReadOnlyList<CellSource> Sources, CellStatsOptions Options, string Actor, bool ByRegime = false) : ICommand;
