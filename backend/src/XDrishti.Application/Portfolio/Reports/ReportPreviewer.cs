using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Portfolio.Imports;
using XDrishti.Application.Portfolio.Trades;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Portfolio.Reports;

/// <summary>
/// Recognises a downloaded file by its title and header and previews it as the use case of its kind (ADR 0029): a global transaction report as trades, a depository-charges sheet as costs, a demat holding statement as a check, a tradebook with a buy/sell column through the
/// tradebook's own preview; the profit and loss statements, a tradebook without sides and a statement of amounts are said to be not imported, a dividend list is sent to the dividend import and anything else is refused with the accepted kinds. Nothing is stored. A file that cannot be
/// read is the preview's <c>Error</c>, never an exception.
/// </summary>
internal sealed class ReportPreviewer(
    ReportWorkspace reports,
    BillService bills,
    DpService costs,
    DematService demat,
    IQueryHandler<PreviewTradesQuery, Result<TradePreviewReport>> tradebook)
{
    public async ValueTask<ImportFilePreview> PreviewAsync(
        BrokerAccount account, string fileName, TradeFileContent content, IReadOnlyList<BillChoice> choices, CancellationToken cancellationToken)
    {
        var loaded = await reports.LoadAsync(content, cancellationToken);
        if (loaded.IsFailure)
        {
            return ReportViews.Failed(fileName, FileKindDetector.Code(FileKind.Unknown), loaded.Error);
        }

        var sheet = loaded.Value;
        var kind = FileKindDetector.DetectSheet(sheet.Leading(FileKindDetector.LeadingRows).Select(r => r.Cells));
        var code = FileKindDetector.Code(kind);
        return kind switch
        {
            FileKind.GlobalTransactionReport => await bills.PreviewAsync(account, fileName, sheet, choices, cancellationToken),
            FileKind.DpCharges => await costs.PreviewAsync(account, fileName, sheet, cancellationToken),
            FileKind.DematHolding => await demat.PreviewAsync(account, fileName, sheet, cancellationToken),
            FileKind.Trades => await TradebookAsync(account, fileName, content, cancellationToken),
            FileKind.PnlReport => ReportViews.NotImported(fileName, code, FileKindDetector.PnlReportText),
            FileKind.TradebookWithoutSides => ReportViews.NotImported(fileName, code, FileKindDetector.TradebookWithoutSidesText),
            FileKind.Statement => ReportViews.NotImported(fileName, code, FileKindDetector.StatementText),
            FileKind.Dividends => ReportViews.Routed(fileName, code, "dividends", "This is a dividend payout list. Import it with Import dividends in the Cash & dividends panel."),
            _ => ReportViews.Failed(fileName, code, ReportErrors.Unknown()),
        };
    }

    private async ValueTask<ImportFilePreview> TradebookAsync(BrokerAccount account, string fileName, TradeFileContent content, CancellationToken cancellationToken)
    {
        var preview = await tradebook.HandleAsync(new PreviewTradesQuery(account.Id, fileName, content), cancellationToken);
        if (preview.IsFailure)
        {
            return ReportViews.Failed(fileName, FileKindDetector.TradesCode, preview.Error);
        }

        var report = preview.Value;
        var file = report.File;
        var summary = new ImportSummary(file.Rows, file.Parsed, file.NewTrades, file.AlreadyImported, file.Skipped.Total, file.BadTotal, null, null);
        var warnings = file.BadTotal > 0 ? new List<ImportWarning> { new("bad_rows", "Some rows cannot be read and are not imported; the first ones are listed with their line.", file.BadTotal) } : [];
        return new ImportFilePreview(fileName, FileKindDetector.TradesCode, true, null, null, null, summary, warnings, report, null, null, null, report.Digest);
    }
}
