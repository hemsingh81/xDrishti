using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Accounts.Buckets;
using XDrishti.Application.Portfolio;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Accounts;

/// <summary>
/// Changes the portfolio settings of an account: which bucket imports go to (its own default bucket or a custom one), whether it counts in totals, the
/// automatic refresh (off or daily) and the first import date. A field that is left out (null) keeps its current value; <c>DefaultBucket</c> "" (or the name of
/// the account's own bucket) selects the own bucket. <b>The first import happens here (ADR 0013):</b> when the request carries <c>FirstImport</c> rows, the account
/// has the Portfolio role, is connected to Dhan and has no holdings yet, the chosen holdings are imported after the settings are saved, in the same request. The
/// settings are saved whatever happens to the import; its outcome comes back in the answer (failed, with <c>portfolio.import_failed</c>, or deferred to the first sync).
/// </summary>
public sealed record UpdatePortfolioCommand(Guid AccountId, PortfolioInput Input, string Actor) : ICommand;

internal sealed class UpdatePortfolioHandler(
    IAccountStore accounts,
    DefaultBuckets defaultBuckets,
    HoldingsFetcher fetcher,
    HoldingsImporter importer,
    IHoldingStore holdings,
    IAccountEventLog events,
    IAccountUnitOfWork unitOfWork,
    AccountViews views,
    TimeProvider clock,
    ILogger<UpdatePortfolioHandler> logger) : ICommandHandler<UpdatePortfolioCommand, PortfolioSaved>
{
    public async ValueTask<Result<PortfolioSaved>> HandleAsync(UpdatePortfolioCommand command, CancellationToken cancellationToken)
    {
        var account = await accounts.FindAsync(command.AccountId, cancellationToken);
        if (account is null)
        {
            return Result.Failure<PortfolioSaved>(AccountErrors.NotFound);
        }

        var errors = new FieldErrors();
        var keepBucket = command.Input?.DefaultBucket is null;
        var custom = keepBucket ? null : await defaultBuckets.ResolveChoiceAsync(account, command.Input?.DefaultBucket, errors, "defaultBucket", cancellationToken);
        var bucketId = keepBucket ? account.Portfolio.DefaultBucketId : custom?.Id;
        var bucketName = keepBucket ? await defaultBuckets.NameOfAsync(bucketId, cancellationToken) : custom?.Name;
        var before = account.Portfolio.Refresh;
        var refresh = AccountInputs.ParseRefresh(command.Input, before, errors);
        var now = clock.GetUtcNow();
        var today = AccountTime.TodayInIndia(now);
        var firstImportDate = AccountInputs.ParseFirstImportDate(command.Input, today, errors);
        var importDate = firstImportDate ?? account.Portfolio.ImportDate;
        var importRequest = command.Input?.FirstImport is { Rows: { Count: > 0 } rows } ? new ImportInput(importDate, rows) : null;
        if (importRequest is not null)
        {
            HoldingsImporter.Check(importRequest, importDate, today, errors, "firstImport.");
        }

        if (errors.HasErrors)
        {
            return Result.Failure<PortfolioSaved>(AccountErrors.Invalid(errors));
        }

        var dateBefore = account.Portfolio.ImportDate;
        var includeInTotals = command.Input?.IncludeInTotals ?? account.Portfolio.IncludeInTotals;
        account.ConfigurePortfolio(new PortfolioSpec(bucketId, includeInTotals, refresh, firstImportDate), now);
        events.Record(AccountEvent.Record(
            AccountEventType.PortfolioUpdated,
            account.Id,
            $"Portfolio ({(account.Portfolio.Enabled ? "role on" : "role off")}): imports go to {(bucketName is null ? "the account's own bucket" : $"bucket {bucketName}")}, {(account.Portfolio.IncludeInTotals ? "included in" : "excluded from")} totals",
            command.Actor,
            now));
        if (account.Portfolio.Refresh != before)
        {
            events.Record(AccountEvent.Record(
                AccountEventType.PortfolioRefreshSettingsChanged, account.Id, RefreshText(account.Portfolio.Refresh), command.Actor, now));
        }

        if (account.Portfolio.ImportDate != dateBefore)
        {
            events.Record(AccountEvent.Record(
                AccountEventType.PortfolioRefreshSettingsChanged, account.Id, $"First import date: {DateText(dateBefore)} -> {DateText(account.Portfolio.ImportDate)}", command.Actor, now));
        }

        if (!await unitOfWork.TrySaveChangesAsync(cancellationToken))
        {
            AccountsLog.ChangeCollided(logger, account.Slug);
            return Result.Failure<PortfolioSaved>(AccountErrors.ConcurrentChange);
        }

        AccountsLog.PortfolioUpdated(logger, account.Slug);
        var import = importRequest is null ? null : await FirstImportAsync(account, importRequest, command.Actor, cancellationToken);
        return Result.Success(new PortfolioSaved(await views.DetailAsync(account, cancellationToken), import));
    }

    /// <summary>The first import of the saved settings. Never fails the request: the settings are saved already, so a problem is part of the answer.</summary>
    private async ValueTask<FirstImportView> FirstImportAsync(BrokerAccount account, ImportInput request, string actor, CancellationToken cancellationToken)
    {
        if (await holdings.AnyForAccountAsync(account.Id, cancellationToken))
        {
            return FirstImports.Skipped;
        }

        if (!account.Portfolio.Enabled)
        {
            return FirstImports.Deferred("The Portfolio role is off. Once it is on and the account is connected, the first sync imports every holding dated " + request.DefaultDate!.Value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) + ".");
        }

        if ((await fetcher.ConnectedAsync(account.Id, cancellationToken)).IsFailure)
        {
            return FirstImports.Deferred("The account is not connected to Dhan. Once it is, the first sync imports every holding dated " + request.DefaultDate!.Value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) + ".");
        }

        var imported = await importer.ImportAsync(account.Id, request, actor, cancellationToken);
        if (imported.IsFailure)
        {
            AccountsLog.FirstImportFailed(logger, account.Slug, imported.Error.Code);
            return FirstImports.Failed(imported.Error);
        }

        return FirstImports.Done(imported.Value.Result);
    }

    private static string DateText(DateOnly? date) => date?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) ?? "none";

    /// <summary>For example <c>Automatic holdings refresh: daily at 16:00 IST</c>.</summary>
    private static string RefreshText(PortfolioRefresh refresh) => refresh.Mode switch
    {
        PortfolioRefreshMode.Daily => $"Automatic holdings refresh: daily at {AccountCodes.Name(refresh.DailyTimeIst)} IST",
        _ => "Automatic holdings refresh: off",
    };
}
