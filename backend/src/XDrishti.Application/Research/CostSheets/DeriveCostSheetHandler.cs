using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Research.CostSheets;

/// <summary>
/// Derives a proposed charge sheet (spec CS-01): reads the original report files through the reader <c>check</c> uses and hands the rows to <see cref="CostSheetDeriver"/>. It stores nothing; the host writes the file. A file it cannot read refuses the
/// whole derivation with the reader's own message.
/// </summary>
internal sealed class DeriveCostSheetHandler(ISpreadsheetReader spreadsheets, TimeProvider time) : IQueryHandler<DeriveCostSheetQuery, Result<CostDerivation>>
{
    public async ValueTask<Result<CostDerivation>> HandleAsync(DeriveCostSheetQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Files.Count == 0)
        {
            return Result.Failure<CostDerivation>(PortfolioErrors.Invalid("file", "Give at least one global transaction report file."));
        }

        if (query.SplitFraction is < CostSheetDeriver.MinSplit or > CostSheetDeriver.MaxSplit)
        {
            return Result.Failure<CostDerivation>(PortfolioErrors.Invalid("split", "The split must be from 0.5 to 0.9."));
        }

        var today = IndiaTime.Today(time.GetUtcNow());
        var read = await ReportRowReader.ReadAsync(query.Files, spreadsheets, today, cancellationToken);
        if (read.IsFailure)
        {
            return Result.Failure<CostDerivation>(read.Error);
        }

        return CostSheetDeriver.Run(read.Value.Rows, query.Files.Count, read.Value.Refused, read.Value.Repeated, query.SplitFraction, today);
    }
}
