using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>Records a move of a cell through its lifecycle (P6-08, doc 09 section 7). Illegal moves and unmet gates are refused with a reason.</summary>
/// <param name="Cell">The cell key <c>strategy|tf|side|style|exit|regime</c>.</param>
/// <param name="To">The wanted state.</param>
/// <param name="Reason">Why, at most 500 characters.</param>
/// <param name="Actor">Who or what asks.</param>
/// <param name="Facts">What the caller states to meet the gates.</param>
/// <param name="EvidenceIds">Ids of what justifies the move.</param>
/// <param name="BeforeRef">A reference to the thing before (a configuration version, for example), or null.</param>
/// <param name="AfterRef">A reference to the thing after, or null.</param>
public sealed record RecordCellTransitionCommand(string Cell, CellState To, string Reason, string Actor, TransitionFacts Facts, IReadOnlyList<string> EvidenceIds, string? BeforeRef = null, string? AfterRef = null) : ICommand;
