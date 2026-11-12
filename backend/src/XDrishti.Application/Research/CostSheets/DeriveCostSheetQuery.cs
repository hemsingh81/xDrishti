using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Portfolio.Trades;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Research.CostSheets;

/// <summary>
/// Proposes a charge sheet from the owner's original global transaction report files (spec CS-01). The rates come from the older share of the bills; the answer holds the sheet text and the residuals and is written nowhere: not the database, not a file.
/// </summary>
/// <param name="Files">The report files (CSV, XLS or XLSX), one per financial year.</param>
/// <param name="SplitFraction">The share of the bills, oldest first, the rates are taken from (0.5 to 0.9).</param>
public sealed record DeriveCostSheetQuery(IReadOnlyList<TradeFileContent> Files, decimal SplitFraction) : IQuery<Result<CostDerivation>>;
