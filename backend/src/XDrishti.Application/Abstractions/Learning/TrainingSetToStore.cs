using XDrishti.Domain.Learning;

namespace XDrishti.Application.Abstractions.Learning;

/// <summary>The record of a training set to keep (P6-04, ADR 0066): which runs, rule, catalog and knobs made it, and what it holds and dropped. The rows are not stored: they are rebuilt from the stored runs and features, and the id proves they are the same.</summary>
/// <param name="Data">The joined training data (its rows are not written).</param>
/// <param name="Sources">The sources as text, <c>runKey:style:exit</c> joined by commas, in key order.</param>
/// <param name="Options">The cell-statistics knobs used for the cell features.</param>
/// <param name="Unresolved">Trades left out of the label set because the bars ended while open.</param>
/// <param name="NoTarget">Trades left out for want of a target level.</param>
/// <param name="CreatedBy">Who made it.</param>
/// <param name="CreatedAt">When.</param>
public sealed record TrainingSetToStore(TrainingData Data, string Sources, CellStatsOptions Options, int Unresolved, int NoTarget, string CreatedBy, DateTimeOffset CreatedAt);
