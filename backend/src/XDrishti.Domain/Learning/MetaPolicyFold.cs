namespace XDrishti.Domain.Learning;

/// <summary>One fold of the policy evaluation (P6-13): thresholds chosen from earlier folds only, applied to this fold's out-of-sample scores.</summary>
/// <param name="Fold">The validation fold.</param>
/// <param name="Evaluable">Enough earlier scores to choose a threshold.</param>
/// <param name="Thresholds">The thresholds used (empty when not evaluable).</param>
/// <param name="Totals">Kept and skipped R in the fold; empty when not evaluable.</param>
public sealed record MetaPolicyFold(ValidationFold Fold, bool Evaluable, IReadOnlyList<MetaPolicyThreshold> Thresholds, MetaPolicyTotals Totals);
