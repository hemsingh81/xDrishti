using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>Replays the drift monitors over stored runs (P6-09): the last months of history are the monitored period, the rest is the reference. Read only: nothing is recorded and no cell changes state.</summary>
/// <param name="Sources">One to 50 stored runs, one run per strategy, each with the style and exit label of its cell.</param>
/// <param name="Options">The knobs of the monitors.</param>
/// <param name="MonitorMonths">The monitored period is the last this many months before the latest exit (1 to 120).</param>
/// <param name="ModelId">A registered model whose calibration to watch on outcomes it was not trained on; null for the cell monitors only.</param>
/// <param name="CellOptions">The cell-statistics knobs of the cell features (only read with a model).</param>
/// <param name="Required">Feature names a row must have known (only read with a model); null means the catalog default.</param>
public sealed record ReplayDriftQuery(IReadOnlyList<CellSource> Sources, DriftOptions Options, int MonitorMonths, long? ModelId, CellStatsOptions CellOptions, IReadOnlyList<string>? Required) : IQuery<Result<DriftReport>>;
