using System.Globalization;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.Reports;
using XDrishti.Application.Portfolio.Trades;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Research.CostSheets;

/// <summary>The bill rows of the owner's report files, the rows a second file repeats dropped, with the reader's counts of refused and repeated rows.</summary>
internal sealed record ReportRows(IReadOnlyList<GtrRow> Rows, int Refused, int Repeated);

/// <summary>
/// Reads the original global transaction report files through the same reader as the report import (spec P3-03f, F5; shared by <c>check</c> and <c>derive</c>, CS-01). A file it cannot read refuses the whole read with the reader's own message, which
/// names columns and limits, never a value of the file.
/// </summary>
internal static class ReportRowReader
{
    public static async Task<Result<ReportRows>> ReadAsync(IReadOnlyList<TradeFileContent> files, ISpreadsheetReader spreadsheets, DateOnly today, CancellationToken cancellationToken)
    {
        var rows = new List<GtrRow>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var refused = 0;
        var repeated = 0;
        for (var i = 0; i < files.Count; i++)
        {
            var sheet = await SheetLoader.LoadAsync(files[i], spreadsheets, cancellationToken);
            var read = sheet.IsFailure ? Result.Failure<GtrFile>(sheet.Error) : GlobalTransactionReader.Read(sheet.Value, today);
            if (read.IsFailure)
            {
                var prefix = "File " + (i + 1).ToString(CultureInfo.InvariantCulture) + ": ";
                return Result.Failure<ReportRows>(read.Error with
                {
                    Message = prefix + read.Error.Message,
                    Fields = read.Error.Fields?.Select(f => f with { Message = prefix + f.Message }).ToList(),
                });
            }

            refused += read.Value.BadTotal;
            repeated += read.Value.Repeated;
            foreach (var row in read.Value.Items)
            {
                // the same bill in two files (overlapping years) is one bill
                var key = FormattableString.Invariant($"{row.BillNo}|{row.Date:yyyyMMdd}|{BillName.Parse(row.Name).Key}|{ReportCells.Norm(row.Exchange)}|{row.BuyQty}|{row.BuyValue}|{row.SellQty}|{row.SellValue}|{row.Charges}");
                if (seen.Add(key))
                {
                    rows.Add(row);
                }
                else
                {
                    repeated++;
                }
            }
        }

        return Result.Success(new ReportRows(rows, refused, repeated));
    }
}
