using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>Evaluates the hypothesis triggers on replayed data (P6-10): proposals to reflect, never a hypothesis written, a run started or a change made.</summary>
/// <param name="Sources">The cell sources (stored runs with style and exit labels), as for the drift replay.</param>
/// <param name="Since">The date the last review stopped; trades exiting after it are new.</param>
/// <param name="MinNewTrades">Doc 16 <c>min_new_trades_per_cell</c>.</param>
/// <param name="Drift">The drift monitor knobs for the drift trigger.</param>
/// <param name="MonitorMonths">The monitored period of the drift replay.</param>
public sealed record EvaluateHypothesisTriggersQuery(IReadOnlyList<CellSource> Sources, DateOnly Since, int MinNewTrades, DriftOptions Drift, int MonitorMonths) : IQuery<Result<HypothesisTriggersView>>;
