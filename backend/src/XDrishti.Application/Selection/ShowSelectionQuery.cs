using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Selection;

/// <summary>Shows the stored cells through the eligibility filters and the ranker as of a decision date (spec P7-01, ADR 0082). Read-only.</summary>
/// <param name="Date">The session the plan would be for. Only statistics with an as-of date before it are used.</param>
/// <param name="MinState">The lowest cell state that may pass.</param>
public sealed record ShowSelectionQuery(DateOnly Date, CellState MinState) : IQuery<Result<SelectionView>>;
