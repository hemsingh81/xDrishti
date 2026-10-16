using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Portfolio.Trades;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Portfolio.Reports;

/// <summary>
/// Recognises one downloaded file by its title and header and says what importing it would do, storing and changing nothing (ADR 0029): a global transaction report as bill-level trades with the lot rebuild each security would get, a depository-charges sheet as account costs, a
/// demat holding statement as a check of the synced holdings and a source of names, a tradebook as in ADR 0023. The file is read as CSV, <c>.xls</c> or <c>.xlsx</c> by its first bytes. <c>Choices</c> say what scrip names of a transaction report mean. The account must have the
/// Portfolio role on; a file that cannot be read or is of an unknown kind is the result's <c>Error</c>, not a failure of the request.
/// </summary>
public sealed record PreviewImportFileQuery(Guid AccountId, string FileName, TradeFileContent Content, IReadOnlyList<BillChoice> Choices) : IQuery<Result<ImportFilePreview>>;

internal sealed class PreviewImportFileHandler(ReportWorkspace reports, ReportPreviewer previewer, TimeProvider clock, ILogger<PreviewImportFileHandler> logger)
    : IQueryHandler<PreviewImportFileQuery, Result<ImportFilePreview>>
{
    public async ValueTask<Result<ImportFilePreview>> HandleAsync(PreviewImportFileQuery query, CancellationToken cancellationToken)
    {
        var started = clock.GetTimestamp();
        var account = await reports.AccountAsync(query.AccountId, cancellationToken);
        if (account.IsFailure)
        {
            return Result.Failure<ImportFilePreview>(account.Error);
        }

        var preview = await previewer.PreviewAsync(account.Value, query.FileName, query.Content, query.Choices, cancellationToken);
        var rows = preview.Summary?.Rows ?? 0;
        var toAdd = preview.Summary?.ToAdd ?? 0;
        var elapsedMs = (long)clock.GetElapsedTime(started).TotalMilliseconds;
        ReportsLog.Previewed(logger, account.Value.Slug, preview.Kind, rows, toAdd, elapsedMs);
        return Result.Success(preview);
    }
}
