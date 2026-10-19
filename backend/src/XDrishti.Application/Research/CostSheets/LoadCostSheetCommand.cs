using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.Research.CostSheets;

/// <summary>
/// Stores a charge sheet read from the text of a file (spec P3-03f, F4): the text is parsed strictly, a <c>test-</c> id is refused, a stored sheet with other rates is replaced only when <paramref name="Replace"/> is true, and a sheet with the
/// same rates updates its descriptive fields (label, source, checked) without it.
/// </summary>
/// <param name="Text">The text of the sheet file.</param>
/// <param name="Replace">Whether other rates under the same id may be overwritten.</param>
/// <param name="Actor">Who is recorded as the author of the row (<c>cli:cost-sheet</c> for the verb).</param>
public sealed record LoadCostSheetCommand(string Text, bool Replace, string Actor) : ICommand;
