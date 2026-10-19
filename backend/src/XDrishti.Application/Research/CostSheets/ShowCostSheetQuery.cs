using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Research.CostSheets;

/// <summary>Reads one stored charge sheet for display (spec P3-03f, F4): its canonical text, rates hash and label.</summary>
/// <param name="SheetId">The sheet id.</param>
public sealed record ShowCostSheetQuery(string SheetId) : IQuery<Result<ShownCostSheet>>;
