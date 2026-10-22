using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>Builds the training set of stored runs (P6-04): triple-barrier labels joined to the stored decision-time features and to the cell statistics as of the day before each decision, and keeps its record.</summary>
/// <param name="Sources">One to 50 stored runs, each with the style and exit label of its cell (as for the cell statistics); one run per strategy.</param>
/// <param name="Options">The cell-statistics knobs; the doc 16 defaults when not given.</param>
/// <param name="Required">Feature names a row must have known; null means the catalog default.</param>
/// <param name="Actor">Who asks (<c>cli:xd</c> for the verb).</param>
public sealed record BuildTrainingSetCommand(IReadOnlyList<CellSource> Sources, CellStatsOptions Options, IReadOnlyList<string>? Required, string Actor) : ICommand;
