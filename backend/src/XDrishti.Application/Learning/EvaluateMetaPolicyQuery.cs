using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>Evaluates the meta-labelling policy of the champion model (P6-13): needs a champion, builds the training data of the stored runs, reserves the hold-out and judges take or skip against taking everything. Nothing is stored or applied.</summary>
/// <param name="Sources">One to 50 stored runs with features, as for <see cref="TrainModelCommand"/>.</param>
/// <param name="Options">The cell-statistics knobs of the cell features.</param>
/// <param name="Required">Feature names a row must have known; null means the catalog default.</param>
/// <param name="Validation">The fold knobs of P6-05.</param>
/// <param name="Policy">The policy knobs.</param>
/// <param name="HoldOutMonths">Months at the end of the data kept out; 0 keeps none, and the report then says so.</param>
public sealed record EvaluateMetaPolicyQuery(IReadOnlyList<CellSource> Sources, CellStatsOptions Options, IReadOnlyList<string>? Required, ValidationOptions Validation, MetaPolicyOptions Policy, int HoldOutMonths) : IQuery<Result<MetaPolicyReport>>;
