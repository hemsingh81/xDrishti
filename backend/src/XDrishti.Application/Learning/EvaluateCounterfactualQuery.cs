using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>Evaluates the frozen-model counterfactual (P6-12): builds the training data of the stored runs, reserves the hold-out and compares a model frozen at a cut date with the same recipe retrained on a schedule, on the same later trades. Nothing is stored or applied.</summary>
/// <param name="Sources">One to 50 stored runs with features, as for <see cref="TrainModelCommand"/>.</param>
/// <param name="Options">The cell-statistics knobs of the cell features.</param>
/// <param name="Required">Feature names a row must have known; null means the catalog default.</param>
/// <param name="Validation">The fold knobs of P6-05 (minimum train months, embargo, minimum train trades).</param>
/// <param name="Counterfactual">The counterfactual knobs.</param>
/// <param name="HoldOutMonths">Months at the end of the data kept out; 0 keeps none, and the report then says so.</param>
public sealed record EvaluateCounterfactualQuery(IReadOnlyList<CellSource> Sources, CellStatsOptions Options, IReadOnlyList<string>? Required, ValidationOptions Validation, CounterfactualOptions Counterfactual, int HoldOutMonths) : IQuery<Result<CounterfactualReport>>;
