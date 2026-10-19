using XDrishti.Domain.Research;

namespace XDrishti.SimExport.Verification;

/// <summary>
/// A trade of the pack (spec P3-09, V5): its place on the sheet, its run, the trade, why it is there (<see cref="PackPlanner.RandomSource"/> or <see cref="PackPlanner.EdgeSource"/>), the categories it belongs to and the tick of its instrument.
/// The source and the categories go to <c>engine-values.csv</c> only: the log, the index, the card names and <c>pack.json</c> do not hold them.
/// </summary>
public sealed record PackRow(int Seq, int RunIndex, RunTrade Trade, string Source, IReadOnlyList<string> Categories, decimal? Tick);
