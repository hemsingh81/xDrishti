using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Portfolio.Trades;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Research.CostSheets;

/// <summary>
/// Compares a stored charge sheet with the owner's original global transaction report files (spec P3-03f, F5). Nothing is written: not the stored transactions, not the sheet, not the suggested <c>checked</c> note.
/// </summary>
/// <param name="SheetId">The stored sheet.</param>
/// <param name="Files">The report files (CSV, XLS or XLSX), one per financial year.</param>
public sealed record CheckCostSheetQuery(string SheetId, IReadOnlyList<TradeFileContent> Files) : IQuery<Result<CostCheckReport>>;
