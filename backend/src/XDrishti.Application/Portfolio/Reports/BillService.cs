using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.DividendImport;
using XDrishti.Application.Portfolio.Imports;
using XDrishti.Application.Portfolio.Trades;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.Reports;

/// <summary>A global transaction report read against the account: the report, the plan of its trades, what the owner's choices decided to remember and the data the names were matched against.</summary>
internal sealed record BillPrepared(BillPlan Plan, AppliedChoices Applied, ReportData Data);

/// <summary>
/// The global transaction report as trades (ADR 0029): previews it and imports it through the tradebook's own flow (lot plan, digest, batch, Undo, <c>LotsRebuilt</c> audit), as bill-level trades. The report is read, its names are matched against the account's holdings, the master
/// and the names that files taught, and the broker's own trades for the same day, ISIN and side take precedence over the report's rows; the planned trades go through <see cref="TradeWorkspace"/> and <see cref="TradeImporter"/> like a tradebook's. Nothing is guessed: a name that
/// fits more than one security, or none, keeps its trades without an ISIN until the owner chooses. Failures of the file are the preview's <c>Error</c>, never exceptions.
/// </summary>
internal sealed class BillService(
    ReportWorkspace reports,
    TradeWorkspace tradeWorkspace,
    ITradeStore trades,
    TradeImporter importer,
    IDividendAliases dividendAliases,
    ISecurityAliases securityAliases,
    IAccountEventLog events,
    ReportClientCodes clientCodes,
    TimeProvider clock)
{
    private const string Kind = FileKindDetector.GlobalTransactionReportCode;

    public async ValueTask<ImportFilePreview> PreviewAsync(BrokerAccount account, string fileName, Sheet sheet, IReadOnlyList<BillChoice> choices, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var read = GlobalTransactionReader.Read(sheet, IndiaTime.Today(now));
        if (read.IsFailure)
        {
            return ReportViews.Failed(fileName, Kind, read.Error);
        }

        var file = read.Value;
        var prepared = await PrepareAsync(account, file, choices, cancellationToken);
        if (prepared.IsFailure)
        {
            return ReportViews.Failed(fileName, Kind, prepared.Error);
        }

        var plan = prepared.Value.Plan;
        var trade = await tradeWorkspace.PlanAsync(account, plan.Identified, null, "preview", now, TradeSource.Bill, cancellationToken);
        var fresh = trade.Items.Count - trade.AlreadyImported;
        var report = TradeDigests.Salted(TradeViews.Report(fileName, plan.File, trade.Plan, trade.AlreadyImported, fresh), plan.Fingerprint);
        var other = await clientCodes.IsOtherAccountAsync(account.Id, file.ClientCode, cancellationToken);
        var summary = new ImportSummary(file.Rows, file.Items.Count + file.Repeated, fresh, trade.AlreadyImported, plan.File.Skipped, file.BadTotal, file.From, file.To);
        return new ImportFilePreview(fileName, Kind, true, null, null, null, summary, ReportViews.BillWarnings(file, plan, other), report, ReportViews.Bill(plan), null, null, report.Digest);
    }

    /// <summary>Imports the report as one batch of bill-level trades. <paramref name="digest"/> is the preview's, required with <paramref name="rebuild"/>.</summary>
    public async ValueTask<(Result<TradesImported> Imported, int AliasesSaved)> ImportAsync(
        BrokerAccount account, string fileName, TradeFileContent content, Sheet sheet, string? digest, bool rebuild, IReadOnlyList<BillChoice> choices, string actor, CancellationToken cancellationToken)
    {
        var read = GlobalTransactionReader.Read(sheet, IndiaTime.Today(clock.GetUtcNow()));
        if (read.IsFailure)
        {
            return (Result.Failure<TradesImported>(read.Error), 0);
        }

        var file = read.Value;
        var aliasesSaved = 0;
        var imported = await importer.ImportAsync(
            account,
            fileName,
            content.Sha256(),
            rebuild,
            digest,
            actor,
            async token =>
            {
                var prepared = await PrepareAsync(account, file, choices, token);
                if (prepared.IsFailure)
                {
                    return Result.Failure<PreparedTrades>(prepared.Error);
                }

                var work = prepared.Value;
                return Result.Success(new PreparedTrades(
                    work.Plan.File,
                    work.Plan.Identified,
                    TradeSource.Bill,
                    work.Plan.Fingerprint,
                    async (at, ct) =>
                    {
                        aliasesSaved = await RememberAsync(account, work, actor, at, ct);
                        await clientCodes.RememberAsync(account.Id, file.ClientCode, at, ct);
                    }));
            },
            cancellationToken);
        return (imported, aliasesSaved);
    }

    /// <summary>
    /// Reads what the names are matched against, applies the owner's choices and plans the trades. One read of each kind of data for the whole file; the broker's trades are read for the dates of the file only.
    /// </summary>
    private async ValueTask<Result<BillPrepared>> PrepareAsync(BrokerAccount account, GtrFile file, IReadOnlyList<BillChoice> choices, CancellationToken cancellationToken)
    {
        var data = await reports.ReadAsync(account, cancellationToken);
        var index = data.Index();
        var keys = file.Items.Select(r => (Row: r, Name: BillName.Parse(r.Name))).Where(x => BillTrades.NotEquity(x.Row, x.Name) is null).Select(x => x.Name.Key).ToHashSet(StringComparer.Ordinal);
        var applied = BillChoices.Apply(index, choices, keys);
        if (applied.IsFailure)
        {
            return Result.Failure<BillPrepared>(applied.Error);
        }

        var groups = file.Items.Count == 0
            ? []
            : await trades.ListBrokerGroupsAsync(account.Id, file.Items.Min(i => i.Date), file.Items.Max(i => i.Date), cancellationToken);
        return Result.Success(new BillPrepared(BillPlanner.Plan(file, index, groups), applied.Value, data));
    }

    /// <summary>Collects the aliases and learned names the owner's choices asked to remember; the number of names remembered.</summary>
    private async ValueTask<int> RememberAsync(BrokerAccount account, BillPrepared work, string actor, DateTimeOffset at, CancellationToken cancellationToken)
    {
        var saved = await DividendAliasSaver.SaveAsync(dividendAliases, events, account.Id, work.Applied.Aliases, actor, at, cancellationToken);
        var wanted = work.Applied.Learned.Select(l => new TaughtName(l.NameKey, l.Isin, l.Name)).ToList();
        return saved + SecurityNames.Learn(securityAliases, events, account.Id, SecurityNames.Missing(work.Data.Learned, wanted), SecurityAliasSource.Choice, actor, at);
    }
}
