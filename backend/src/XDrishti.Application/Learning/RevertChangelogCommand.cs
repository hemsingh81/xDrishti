using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.Learning;

/// <summary>Reverts the latest transition of a cell by appending a new row that points at it (P6-08). Nothing is updated or deleted.</summary>
/// <param name="Id">The row to revert.</param>
/// <param name="Reason">Why, at most 500 characters.</param>
/// <param name="Actor">Who or what asks.</param>
public sealed record RevertChangelogCommand(long Id, string Reason, string Actor) : ICommand;
