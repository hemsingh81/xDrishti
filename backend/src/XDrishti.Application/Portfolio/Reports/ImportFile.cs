using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Portfolio.Imports;
using XDrishti.Application.Portfolio.Trades;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.Reports;

/// <summary>
/// Imports one downloaded file as what its title and header say it is (ADR 0029): a global transaction report as one batch of bill-level trades (the lots rebuilt with <c>Rebuild</c>, the default, like a tradebook import - hand-dated lots are replaced only with the digest of the
/// preview the owner saw), a depository-charges sheet as one batch of account costs, a demat holding statement as remembered names (no holding changes), a tradebook as in ADR 0023. <c>Digest</c> is the preview's: required, except for a tradebook or transaction report without a
/// rebuild; when what the file would do changed meanwhile nothing is stored and the result is <c>rejected</c> with <c>portfolio.preview_stale</c> and the fresh preview. A report whose client code (UCC) differs from that of the reports imported into the account before (a remembered hash, ADR 0029) is <c>rejected</c> unless
/// <c>AcceptOtherAccount</c>; the first import with a code remembers it. <c>Choices</c> say what scrip names of a transaction report mean (and are remembered with <c>Remember</c>). Each import is all or nothing and audited with counts only. Per-file problems are in the result; an account that is missing or has the role off fails the request.
/// </summary>
public sealed record ImportFileCommand(
    Guid AccountId,
    string FileName,
    TradeFileContent Content,
    string? Digest,
    bool Rebuild,
    IReadOnlyList<BillChoice> Choices,
    bool AcceptOtherAccount,
    string Actor) : ICommand;

internal sealed class ImportFileHandler(
    ReportWorkspace reports,
    ReportPreviewer previewer,
    BillService bills,
    DpService costs,
    DematService demat,
    ReportClientCodes clientCodes,
    ICommandHandler<ImportTradesCommand, TradeImportResult> tradebook,
    TimeProvider clock,
    ILogger<ImportFileHandler> logger) : ICommandHandler<ImportFileCommand, ImportFileResult>
{
    public async ValueTask<Result<ImportFileResult>> HandleAsync(ImportFileCommand command, CancellationToken cancellationToken)
    {
        var started = clock.GetTimestamp();
        var account = await reports.AccountAsync(command.AccountId, cancellationToken);
        if (account.IsFailure)
        {
            return Result.Failure<ImportFileResult>(account.Error);
        }

        var loaded = await reports.LoadAsync(command.Content, cancellationToken);
        if (loaded.IsFailure)
        {
            return Result.Success(Rejected(command.FileName, FileKindDetector.Code(FileKind.Unknown), loaded.Error, null));
        }

        var sheet = loaded.Value;
        var kind = FileKindDetector.DetectSheet(sheet.Leading(FileKindDetector.LeadingRows).Select(r => r.Cells));
        var code = FileKindDetector.Code(kind);
        var done = kind switch
        {
            FileKind.GlobalTransactionReport => await BillAsync(account.Value, command, sheet, code, cancellationToken),
            FileKind.DpCharges => await CostsAsync(account.Value, command, sheet, code, cancellationToken),
            FileKind.DematHolding => await DematAsync(account.Value, command, sheet, code, cancellationToken),
            FileKind.Trades => await TradebookAsync(account.Value, command, code, cancellationToken),
            _ => await NotImportedAsync(account.Value, command, code, cancellationToken),
        };
        var stored = done.Trades?.Imported ?? done.Costs?.Imported ?? done.Demat?.NamesLearned ?? 0;
        var elapsedMs = (long)clock.GetElapsedTime(started).TotalMilliseconds;
        ReportsLog.Imported(logger, account.Value.Slug, code, stored, elapsedMs);
        return Result.Success(done);
    }

    private async ValueTask<ImportFileResult> BillAsync(BrokerAccount account, ImportFileCommand command, Sheet sheet, string code, CancellationToken cancellationToken)
    {
        if (command.Rebuild && string.IsNullOrWhiteSpace(command.Digest))
        {
            return Rejected(command.FileName, code, ReportErrors.DigestRequired(), null);
        }

        var read = GlobalTransactionReader.Read(sheet, XDrishti.Domain.Market.IndiaTime.Today(clock.GetUtcNow()));
        if (read.IsSuccess && !command.AcceptOtherAccount && await clientCodes.IsOtherAccountAsync(account.Id, read.Value.ClientCode, cancellationToken))
        {
            return Rejected(command.FileName, code, ReportErrors.OtherAccount, null);
        }

        var (imported, aliases) = await bills.ImportAsync(account, command.FileName, command.Content, sheet, command.Digest, command.Rebuild, command.Choices, command.Actor, cancellationToken);
        if (imported.IsFailure)
        {
            return await FailedAsync(account, command, code, imported.Error, cancellationToken);
        }

        var trades = imported.Value.Result;
        return new ImportFileResult(command.FileName, code, trades.Imported > 0 || aliases > 0 ? "imported" : "nothing_new", null, null, null, trades, null, null, aliases);
    }

    private async ValueTask<ImportFileResult> CostsAsync(BrokerAccount account, ImportFileCommand command, Sheet sheet, string code, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Digest))
        {
            return Rejected(command.FileName, code, ReportErrors.DigestRequired(), null);
        }

        var read = DpChargesReader.Read(sheet, XDrishti.Domain.Market.IndiaTime.Today(clock.GetUtcNow()));
        if (read.IsSuccess && !command.AcceptOtherAccount && await clientCodes.IsOtherAccountAsync(account.Id, read.Value.ClientCode, cancellationToken))
        {
            return Rejected(command.FileName, code, ReportErrors.OtherAccount, null);
        }

        var result = await costs.ImportAsync(account, command.FileName, command.Content, sheet, command.Digest, command.Actor, cancellationToken);
        if (result.IsFailure)
        {
            return await FailedAsync(account, command, code, result.Error, cancellationToken);
        }

        var costsDone = result.Value;
        return new ImportFileResult(command.FileName, code, costsDone.Imported > 0 || costsDone.NamesLearned > 0 ? "imported" : "nothing_new", null, null, null, null, costsDone, null, 0);
    }

    private async ValueTask<ImportFileResult> DematAsync(BrokerAccount account, ImportFileCommand command, Sheet sheet, string code, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Digest))
        {
            return Rejected(command.FileName, code, ReportErrors.DigestRequired(), null);
        }

        var read = DematHoldingReader.Read(sheet);
        if (read.IsSuccess && !command.AcceptOtherAccount && await clientCodes.IsOtherAccountAsync(account.Id, read.Value.ClientCode, cancellationToken))
        {
            return Rejected(command.FileName, code, ReportErrors.OtherAccount, null);
        }

        var result = await demat.ImportAsync(account, sheet, command.Digest, command.Actor, cancellationToken);
        if (result.IsFailure)
        {
            return await FailedAsync(account, command, code, result.Error, cancellationToken);
        }

        return new ImportFileResult(command.FileName, code, result.Value.NamesLearned > 0 ? "imported" : "nothing_new", null, null, null, null, null, result.Value, 0);
    }

    private async ValueTask<ImportFileResult> TradebookAsync(BrokerAccount account, ImportFileCommand command, string code, CancellationToken cancellationToken)
    {
        var result = await tradebook.HandleAsync(new ImportTradesCommand(account.Id, command.FileName, command.Content, command.Rebuild, command.Digest, command.Actor), cancellationToken);
        return result.IsFailure
            ? await FailedAsync(account, command, code, result.Error, cancellationToken)
            : new ImportFileResult(command.FileName, code, result.Value.Imported > 0 ? "imported" : "nothing_new", null, null, null, result.Value, null, null, 0);
    }

    /// <summary>A file of a kind that cannot be imported here: it is said why (the same text as the preview), nothing is stored.</summary>
    private async ValueTask<ImportFileResult> NotImportedAsync(BrokerAccount account, ImportFileCommand command, string code, CancellationToken cancellationToken)
    {
        var preview = await previewer.PreviewAsync(account, command.FileName, command.Content, command.Choices, cancellationToken);
        return preview.Error is { } error
            ? Rejected(command.FileName, code, error, preview)
            : new ImportFileResult(command.FileName, code, "not_supported", preview.Message, null, preview, null, null, null, 0);
    }

    /// <summary>A refused import. When the preview the owner confirmed is stale, the fresh preview comes with it.</summary>
    private async ValueTask<ImportFileResult> FailedAsync(BrokerAccount account, ImportFileCommand command, string code, Error error, CancellationToken cancellationToken)
    {
        var fresh = error.Code == PortfolioErrors.PreviewStaleCode ? await previewer.PreviewAsync(account, command.FileName, command.Content, command.Choices, cancellationToken) : null;
        return Rejected(command.FileName, code, ReportViews.Error(error), fresh);
    }

    private static ImportFileResult Rejected(string fileName, string code, Error error, ImportFilePreview? fresh) => Rejected(fileName, code, ReportViews.Error(error), fresh);

    private static ImportFileResult Rejected(string fileName, string code, ImportFileError error, ImportFilePreview? fresh) =>
        new(fileName, code, "rejected", error.Message, error, fresh, null, null, null, 0);
}
