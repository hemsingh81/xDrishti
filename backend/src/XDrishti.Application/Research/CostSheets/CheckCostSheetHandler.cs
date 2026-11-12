using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Abstractions.Research;
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
        var read = await ReportRowReader.ReadAsync(query.Files, spreadsheets, today, cancellationToken);
        if (read.IsFailure)
        {
            return Result.Failure<CostCheckReport>(read.Error);
        }

        return Result.Success(CostBillCheck.Run(found.Value, read.Value.Rows, query.Files.Count, read.Value.Refused, read.Value.Repeated, today));
    }
}
