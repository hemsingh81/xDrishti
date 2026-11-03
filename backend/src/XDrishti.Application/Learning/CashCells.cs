using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Learning;

namespace XDrishti.Application.Learning;

/// <summary>
/// The cash-only rule of the decision paths (P14-06, ADR 0193): selection, planning and paper take only the cells whose expression is cash. A cell of another expression is skipped and counted (<see cref="SkippedCode"/>), never an error and never a
/// candidate, so a stored option or pair cell changes no plan, ticket or order.
/// </summary>
public static partial class CashCells
{
    /// <summary>The name of the skip counter in the log.</summary>
    public const string SkippedCode = "non_cash_cell_skipped";

    /// <summary>The cash rows in their order; logs the count of the others when there are any.</summary>
    public static IReadOnlyList<StoredCellStats> Only(IReadOnlyList<StoredCellStats> rows, string path, ILogger? logger)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var cash = rows.Count == 0 || rows.All(r => r.Row.Cell.IsCash) ? rows : [.. rows.Where(r => r.Row.Cell.IsCash)];
        if (cash.Count != rows.Count && logger is not null)
        {
            Skipped(logger, SkippedCode, path, rows.Count - cash.Count);
        }

        return cash;
    }

    /// <summary>Logs one cell of another expression than cash left out of a decision path that works on one ticket at a time.</summary>
    public static void LeftOut(ILogger? logger, string path)
    {
        if (logger is not null)
        {
            Skipped(logger, SkippedCode, path, 1);
        }
    }

    [LoggerMessage(EventId = 4610, Level = LogLevel.Information, Message = "{Code} on {Path}: {Count} cell(s) of another expression than cash left out")]
    private static partial void Skipped(ILogger logger, string code, string path, int count);
}
