using System.Globalization;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Portfolio.Reports;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Research.CostSheets;

/// <summary>
/// The bill check (spec P3-03f, F5): reads the stored sheet and the original report files through the same reader as the report import, drops the rows a second file repeats, and hands the rows to <see cref="CostBillCheck"/>. It reads the sheet
/// and the files and writes nothing. A file it cannot read refuses the whole check with the reader's own message (which names columns and limits, never a value of the file).
/// </summary>
internal sealed class CheckCostSheetHandler(ICostSheetStore store, ISpreadsheetReader spreadsheets, TimeProvider time) : IQueryHandler<CheckCostSheetQuery, Result<CostCheckReport>>
{
    public async ValueTask<Result<CostCheckReport>> HandleAsync(CheckCostSheetQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Files.Count == 0)
        {
            return Result.Failure<CostCheckReport>(PortfolioErrors.Invalid("file", "Give at least one global transaction report file."));
        }

        var found = await store.FindAsync(query.SheetId, cancellationToken);
        if (found.IsFailure)
        {
            return Result.Failure<CostCheckReport>(found.Error);
        }

        var today = IndiaTime.Today(time.GetUtcNow());
        var rows = new List<GtrRow>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var refused = 0;
        var repeated = 0;
        for (var i = 0; i < query.Files.Count; i++)
        {
            var sheet = await SheetLoader.LoadAsync(query.Files[i], spreadsheets, cancellationToken);
            var read = sheet.IsFailure ? Result.Failure<GtrFile>(sheet.Error) : GlobalTransactionReader.Read(sheet.Value, today);
            if (read.IsFailure)
            {
                var prefix = "File " + (i + 1).ToString(CultureInfo.InvariantCulture) + ": ";
                return Result.Failure<CostCheckReport>(read.Error with
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

        return Result.Success(CostBillCheck.Run(found.Value, rows, query.Files.Count, refused, repeated, today));
    }
}
