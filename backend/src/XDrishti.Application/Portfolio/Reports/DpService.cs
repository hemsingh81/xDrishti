using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.Imports;
using XDrishti.Application.Portfolio.Trades;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.Reports;

/// <summary>A depository-charges sheet read against the account: the sheet, what each line would do and the names it teaches.</summary>
internal sealed record DpPrepared(DpFile File, DpPlan Plan, IReadOnlyList<TaughtName> Missing, int KnownNames, IReadOnlyList<TaughtName> Taught);

/// <summary>
/// The depository-charges sheet as account costs (ADR 0029): previews it and imports it as one batch of ledger entries of class charges (description "DP charges", source import) that the net figures count once; teaches the names of its securities. Everything is decided by
/// <see cref="DpPlanner"/> from the sheet, the identities stored and Dhan's own charges of the same days; the sheet's lines of ₹0 and on the buy side are listed and not stored. Failures of the file are the preview's <c>Error</c>, never exceptions. The import is refused (<c>409 portfolio.sync_running</c>) while a holdings sync or a broker history run of the account runs.
/// </summary>
internal sealed class DpService(
    ReportWorkspace reports,
    ILedgerStore ledger,
    ICostBatches batches,
    ISecurityAliases securityAliases,
    IAccountEventLog events,
    ReportClientCodes clientCodes,
    SyncGuard guard,
    IPortfolioUnitOfWork unitOfWork,
    TimeProvider clock)
{
    private const string Kind = FileKindDetector.DpChargesCode;

    /// <summary>The most lines a preview lists.</summary>
    public const int MaxListedLines = 300;

    public async ValueTask<ImportFilePreview> PreviewAsync(BrokerAccount account, string fileName, Sheet sheet, CancellationToken cancellationToken)
    {
        var read = DpChargesReader.Read(sheet, IndiaTime.Today(clock.GetUtcNow()));
        if (read.IsFailure)
        {
            return ReportViews.Failed(fileName, Kind, read.Error);
        }

        var file = read.Value;
        var prepared = await PrepareAsync(account, file, cancellationToken);
        var plan = prepared.Plan;
        var counts = new DpCounts(
            file.Lines.Count,
            plan.Count(DpStatus.New),
            plan.Count(DpStatus.Zero),
            plan.Count(DpStatus.BuySide),
            plan.Count(DpStatus.AlreadyImported),
            plan.Count(DpStatus.AlreadyInLedger),
            plan.Lines.Count(l => DpPlanner.IsOffMarket(l.Line)));
        var costs = new DpPreview(
            [.. plan.Lines.Take(MaxListedLines).Select(Line)],
            plan.Lines.Count,
            counts,
            plan.AmountToStore,
            new NameLearning(prepared.KnownNames, prepared.Missing.Count));
        var warnings = new List<ImportWarning>();
        var other = await clientCodes.IsOtherAccountAsync(account.Id, file.ClientCode, cancellationToken);
        if (other)
        {
            warnings.Add(ReportViews.OtherAccountWarning());
        }

        if (file.BadTotal > 0)
        {
            warnings.Add(new ImportWarning("bad_rows", "Some lines cannot be read and are not imported; the first ones are listed with their line.", file.BadTotal));
        }

        var summary = new ImportSummary(file.Rows, file.Lines.Count, counts.ToStore, counts.AlreadyImported + counts.AlreadyInLedger, counts.Zero + counts.BuySide, file.BadTotal, file.From, file.To);
        return new ImportFilePreview(fileName, Kind, true, null, null, null, summary, warnings, null, null, costs, null, plan.Digest);
    }

    /// <summary>Imports the sheet: the costs as one batch, the names it teaches. <paramref name="digest"/> is the preview's.</summary>
    public async ValueTask<Result<CostImportResult>> ImportAsync(
        BrokerAccount account, string fileName, TradeFileContent content, Sheet sheet, string digest, string actor, CancellationToken cancellationToken)
    {
        var read = DpChargesReader.Read(sheet, IndiaTime.Today(clock.GetUtcNow()));
        if (read.IsFailure)
        {
            return Result.Failure<CostImportResult>(read.Error);
        }

        var file = read.Value;
        var sha256 = content.Sha256();
        var accountName = account.Name;
        CostImportResult? result = null;
        var outcome = await unitOfWork.RunAsync(
            async token =>
            {
                // The costs are read against the ledger below inside this transaction; a broker history run (ledger or trades) or a holdings sync that is running changes them under us, and the sheet's lines would be stored again beside the ledger's own.
                if (await guard.RunningAsync(account.Id, token))
                {
                    return Result.Failure(PortfolioErrors.SyncRunning);
                }

                var at = clock.GetUtcNow();
                var prepared = await PrepareAsync(account, file, token);
                if (!string.Equals(digest.Trim(), prepared.Plan.Digest, StringComparison.OrdinalIgnoreCase))
                {
                    return Result.Failure(ReportErrors.PreviewStale());
                }

                var batchId = Guid.CreateVersion7(at);
                var toStore = prepared.Plan.ToStore.ToList();
                CostBatch? batch = null;
                if (toStore.Count > 0)
                {
                    var amount = prepared.Plan.AmountToStore;
                    var notStored = file.Lines.Count - toStore.Count;
                    batch = CostBatch.Start(batchId, account.Id, fileName, sha256, file.Lines.Count, toStore.Count, notStored, file.BadTotal, amount, actor, at);
                    batches.Add(batch);
                    ledger.Add(
                    [
                        .. toStore.Select(p => LedgerEntry.FromImport(
                            account.Id,
                            p.Line.Date,
                            DpPlanner.Description,
                            DpPlanner.Narration(p.Line),
                            p.Line.Charge,
                            p.Line.Isin,
                            p.Line.Quantity > 0 ? p.Line.Quantity : null,
                            batchId,
                            p.ExternalId,
                            at)),
                    ]);
                    events.Record(AccountEvent.Record(
                        AccountEventType.CostsImported,
                        account.Id,
                        $"Costs imported: batch {batchId}, {file.Lines.Count} valid lines, {toStore.Count} stored, {notStored} not stored, {file.BadTotal} bad",
                        actor,
                        at));
                }

                var learned = SecurityNames.Learn(securityAliases, events, account.Id, prepared.Missing, SecurityAliasSource.Dp, actor, at);
                await clientCodes.RememberAsync(account.Id, file.ClientCode, at, token);
                if (!await unitOfWork.TrySaveAsync(token))
                {
                    return Result.Failure(ReportErrors.CostsChanged);
                }

                result = new CostImportResult(
                    batch is null ? null : CostViews.Batch(new CostBatchRow(batch, toStore.Count), accountName),
                    toStore.Count,
                    prepared.Plan.AmountToStore,
                    prepared.Plan.Count(DpStatus.AlreadyImported),
                    prepared.Plan.Count(DpStatus.AlreadyInLedger),
                    learned);
                return Result.Success();
            },
            cancellationToken);
        return outcome.IsFailure ? Result.Failure<CostImportResult>(outcome.Error) : Result.Success(result!);
    }

    private async ValueTask<DpPrepared> PrepareAsync(BrokerAccount account, DpFile file, CancellationToken cancellationToken)
    {
        var data = await reports.ReadAsync(account, cancellationToken);
        var taught = SecurityNames.Of(file.Lines.Select(l => (l.Name, l.Isin)));
        var missing = SecurityNames.Missing(data.Learned, taught);
        IReadOnlySet<string> stored = new HashSet<string>(StringComparer.Ordinal);
        IReadOnlyList<LedgerChargeOnDay> brokerCharges = [];
        if (file.Lines.Count > 0)
        {
            var provisional = DpPlanner.Plan(account.Id, file, stored, [], missing.Count);
            stored = await ledger.StoredIdsAsync(account.Id, [.. provisional.Lines.Select(l => l.ExternalId)], cancellationToken);
            brokerCharges = await ledger.ListBrokerChargesAsync(account.Id, file.Lines.Min(l => l.Date), file.Lines.Max(l => l.Date), cancellationToken);
        }

        var plan = DpPlanner.Plan(account.Id, file, stored, brokerCharges, missing.Count);
        return new DpPrepared(file, plan, missing, taught.Count - missing.Count, taught);
    }

    private static DpLineView Line(PlannedDp planned) => new(
        planned.Line.Line,
        planned.Line.Date,
        planned.Line.Isin,
        planned.Line.Name,
        planned.Line.Quantity,
        planned.Line.Side.ToString().ToLowerInvariant(),
        planned.Line.Type,
        planned.Line.Charge,
        planned.Status switch
        {
            DpStatus.New => "new",
            DpStatus.Zero => "zero",
            DpStatus.BuySide => "buy_side",
            DpStatus.AlreadyImported => "already_imported",
            _ => "already_in_ledger",
        });
}
