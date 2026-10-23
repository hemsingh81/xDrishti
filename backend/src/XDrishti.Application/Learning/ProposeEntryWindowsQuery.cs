using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>Proposes learned entry windows per cell from stored runs (P6-14): reads and labels the closed trades, reserves the hold-out and learns. Nothing is stored or applied.</summary>
/// <param name="Sources">One to 50 stored runs, one run per strategy, each with the style and exit label of its cell (as for the cell statistics).</param>
/// <param name="Options">The window knobs.</param>
/// <param name="Validation">The fold knobs of P6-05.</param>
/// <param name="HoldOutMonths">Months at the end of the data kept out of the search (6); 0 keeps none, and the report then says so.</param>
public sealed record ProposeEntryWindowsQuery(IReadOnlyList<CellSource> Sources, EntryWindowOptions Options, ValidationOptions Validation, int HoldOutMonths) : IQuery<Result<EntryWindowReport>>;
