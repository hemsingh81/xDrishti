using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>Trains, validates and registers a model from stored runs (P6-07): the training data of P6-04, the hold-out cut of P6-05, the walk-forward verdict against the baselines, the final model of P6-06 on the development rows, its file and its record.</summary>
/// <param name="Sources">One to 50 stored runs with features (<c>xd learn features</c>), one run per strategy, each with the style and exit label of its cell.</param>
/// <param name="Options">The cell-statistics knobs of the cell features.</param>
/// <param name="Required">Feature names a row must have known; null means the catalog default.</param>
/// <param name="Validation">The fold knobs of the harness.</param>
/// <param name="HoldOutMonths">Months at the end of the data kept out of the model and its verdict; 0 keeps none, and the report then says so.</param>
/// <param name="Actor">Who asks (<c>cli:xd</c> for the verb).</param>
public sealed record TrainModelCommand(IReadOnlyList<CellSource> Sources, CellStatsOptions Options, IReadOnlyList<string>? Required, ValidationOptions Validation, int HoldOutMonths, string Actor) : ICommand;
