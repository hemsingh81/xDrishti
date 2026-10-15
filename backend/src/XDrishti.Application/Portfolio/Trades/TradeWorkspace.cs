using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Actions;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.Trades;

/// <summary>A file read against the account: the plan, the file's trades as transactions (stored already or new) and the holdings the plan was made from (tracked, so a rebuild can change them).</summary>
internal sealed record FilePlan(TradePlan Plan, IReadOnlyList<FileTrade> Items, IReadOnlyList<Holding> Holdings)
{
    public int AlreadyImported => Items.Count(i => i.AlreadyStored);

    public IEnumerable<Transaction> NewTrades => Items.Where(i => !i.AlreadyStored).Select(i => i.Trade);
}

/// <summary>What replacing lots did: holdings changed, lots removed and created, and the hand-dated and sync-added lots among the removed.</summary>
internal sealed record RebuildApplied(int Holdings, int LotsReplaced, int LotsCreated, int HandDated, int Sync);

/// <summary>
/// The steps the trade use cases share (ADR 0023): the account must exist and have the Portfolio role on; the file is planned against the holdings, the stored trades, the master list and the applied corporate actions
/// (<see cref="TradeRebuildPlanner"/>); a plan is applied by replacing the lots of the holdings it says <c>ok</c> for. Reads are one query each; nothing here saves — the use case does, in its unit of work.
/// </summary>
internal sealed class TradeWorkspace(
    IAccountStore accounts,
    IHoldingStore holdings,
    ITradeStore trades,
    ITradeReferenceData reference,
    IAccountEventLog events)
{
    /// <summary>The active account with the Portfolio role on (read only).</summary>
    public ValueTask<Result<BrokerAccount>> AccountAsync(Guid accountId, CancellationToken cancellationToken) => PortfolioAccess.AccountWithRoleAsync(accounts, accountId, cancellationToken);

    /// <summary>
    /// Plans the import of the identified trades of a file: the new ones become transactions (batch <paramref name="batchId"/>) and are rebuilt into the holdings together with the stored trades; trades whose identity is stored already
    /// count in the file's securities but are not added again.
    /// </summary>
    public ValueTask<FilePlan> PlanAsync(BrokerAccount account, IReadOnlyList<IdentifiedTrade> identified, Guid? batchId, string actor, DateTimeOffset now, CancellationToken cancellationToken) =>
        PlanAsync(account, identified, batchId, actor, now, TradeSource.Csv, cancellationToken);

    /// <summary>The same for trades that came from <paramref name="source"/> (a tradebook CSV, or the bill-level rows of a Dhan report, ADR 0029).</summary>
    public async ValueTask<FilePlan> PlanAsync(
        BrokerAccount account, IReadOnlyList<IdentifiedTrade> identified, Guid? batchId, string actor, DateTimeOffset now, TradeSource source, CancellationToken cancellationToken)
    {
        var all = await holdings.ListForAccountAsync(account.Id, cancellationToken);
        var stored = await trades.ListActiveAsync(account.Id, cancellationToken);
        var storedIds = stored.Select(t => t.ExternalId).ToHashSet(StringComparer.Ordinal);
        var master = new TradeInstruments(await reference.FindInstrumentsAsync(
            [.. identified.Select(i => i.Trade.Isin).OfType<string>().Distinct(StringComparer.Ordinal)],
            [.. identified.Select(i => i.Trade.Symbol).Where(s => s.Length > 0).Distinct(StringComparer.Ordinal)],
            cancellationToken));
        var actions = await ActionsOfAsync(all, cancellationToken);
        var items = new List<FileTrade>(identified.Count);
        foreach (var (row, externalId) in identified.Select(i => (i.Trade, i.ExternalId)))
        {
            var transaction = Transaction.Create(
                account.Id,
                master.Find(row.Isin, row.Symbol)?.Id,
                row.Isin,
                row.Symbol,
                row.Exchange,
                row.Side,
                row.Date,
                row.Time,
                row.Qty,
                row.Price,
                row.Charges,
                source,
                batchId,
                row.Line,
                externalId,
                actor,
                now);
            items.Add(new FileTrade(transaction, storedIds.Contains(externalId)));
        }

        var plan = TradeRebuildPlanner.Plan(all, stored, items, master, actions, account.Portfolio.ImportDate);
        return new FilePlan(plan, items, all);
    }

    /// <summary>The applied corporate actions of the instruments of these holdings (one query).</summary>
    public async ValueTask<IReadOnlyDictionary<Guid, IReadOnlyList<AppliedAction>>> ActionsOfAsync(IEnumerable<Holding> held, CancellationToken cancellationToken)
    {
        var ids = held.Where(h => h.Status == HoldingStatus.Open).Select(h => h.InstrumentId).OfType<Guid>().Distinct().ToArray();
        return ids.Length == 0 ? new Dictionary<Guid, IReadOnlyList<AppliedAction>>() : await reference.ListAppliedActionsAsync(ids, cancellationToken);
    }

    /// <summary>Replaces the lots of every holding whose rebuild is <c>ok</c> (the lots of the others stay) and marks the holding rows so that saving checks for a sync or quote that changed them meanwhile.</summary>
    public RebuildApplied Apply(IEnumerable<HoldingRebuild> rebuilds, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(rebuilds);
        var changed = 0;
        var replaced = 0;
        var created = 0;
        var handDated = 0;
        var sync = 0;
        foreach (var rebuild in rebuilds.Where(r => r.WillReplace))
        {
            var holding = rebuild.Holding!;
            holding.ReplaceLots(rebuild.Plan, now);
            holdings.Touch(holding);
            changed++;
            replaced += rebuild.LotsBefore;
            created += rebuild.LotsAfter;
            handDated += rebuild.HandDatedLotsReplaced;
            sync += rebuild.SyncLotsReplaced;
        }

        return new RebuildApplied(changed, replaced, created, handDated, sync);
    }

    /// <summary>
    /// Works out the rebuild of one holding from its stored trades (ADR 0023): reads the account's holdings (tracked, so applying the result changes what is saved), the holding's security's trades and the applied corporate actions. Used by the owner's
    /// rebuild and by the automatic rebuild after a broker trade sync (ADR 0025), so both decide the same way.
    /// </summary>
    public async ValueTask<HoldingRebuild> RebuildOfAsync(Guid accountId, Guid holdingId, DateOnly firstImportDate, CancellationToken cancellationToken)
    {
        var all = await holdings.ListForAccountAsync(accountId, cancellationToken);
        return await RebuildOfAsync(accountId, all.Single(h => h.Id == holdingId), new HoldingMatcher(all), firstImportDate, cancellationToken);
    }

    /// <summary>
    /// The same for a holding the caller has read already (tracked, with its lots) and the matcher over the account's holdings: the automatic rebuild reads the account's holdings once for all its candidates and then only the one holding
    /// of each candidate inside that candidate's unit of work.
    /// </summary>
    public async ValueTask<HoldingRebuild> RebuildOfAsync(Guid accountId, Holding holding, HoldingMatcher matcher, DateOnly firstImportDate, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(holding);
        ArgumentNullException.ThrowIfNull(matcher);
        if (holding.Status != HoldingStatus.Open)
        {
            return HoldingRebuild.Without(RebuildStatuses.NoHolding, holding.Id.ToString("N"));
        }

        if (matcher.Find(holding.Isin, holding.TradingSymbol) is not { Kind: MatchKind.One })
        {
            return HoldingRebuild.Without(RebuildStatuses.Ambiguous, holding.Id.ToString("N"));
        }

        // Only this security's trades are read, not the account's whole history.
        var candidates = await trades.ListActiveForSecurityAsync(accountId, TradeRules.NormaliseIsin(holding.Isin), TradeRules.NormaliseSymbol(holding.TradingSymbol), cancellationToken);
        var mine = HoldingRebuilds.Of(matcher, holding, candidates);
        var actions = await ActionsOfAsync([holding], cancellationToken);
        return HoldingRebuilds.Compute(holding, mine, holding.InstrumentId is { } instrument ? actions.GetValueOrDefault(instrument) ?? [] : [], firstImportDate);
    }

    /// <summary>Audits a rebuild (counts only): nothing per lot, nothing about the securities. <paramref name="trigger"/> says what started it when it was not the owner's own request (<c>broker_sync</c>).</summary>
    public void RecordRebuilt(Guid accountId, RebuildApplied applied, string actor, DateTimeOffset now, string? trigger = null)
    {
        ArgumentNullException.ThrowIfNull(applied);
        if (applied.Holdings == 0)
        {
            return;
        }

        events.Record(AccountEvent.Record(
            AccountEventType.LotsRebuilt,
            accountId,
            $"Lots rebuilt from trades: {applied.Holdings} {(applied.Holdings == 1 ? "holding" : "holdings")}, {applied.LotsReplaced} lots replaced by {applied.LotsCreated} ({applied.HandDated} hand-dated, {applied.Sync} added by syncs){(trigger is null ? string.Empty : $", trigger {trigger}")}",
            actor,
            now));
    }
}
