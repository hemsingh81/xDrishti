using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Selection.SmartRules;

namespace XDrishti.Infrastructure.Planning;

/// <summary>
/// The facts of a plan from stored data (spec P7-09, ADR 0092): the Trading-role accounts with their risk profile and overrides, the synced holdings, the sector and baskets of each instrument, and the correlation of daily returns up to the close
/// of the session. Read only; nothing here talks to a broker or writes anything.
/// <b>What has no data source is null</b> (unavailable downstream, never a pass): the market figures (VIX, breadth, events; P3-01d/e), real taken trades, the funds of a broker-funds account.
/// <b>The open book is unavailable by default</b> (the system keeps no position record for a Trading account: no journal yet, P7-14): book, open risk and the loss today are null, so the fill takes nothing and the plan says so. Only when the owner sets
/// <c>Planning:OpenBook:AssumedEmpty</c> are they the empty book and zero, and the plan says that instead. Positions held outside this system (the broker's own app) are NOT known and not counted.
/// </summary>
internal sealed class StoredPlanContextSource(
    IAccountStore accounts,
    IRiskProfileStore profiles,
    IHoldingQueries holdings,
    IHoldingClassifications classifications,
    IBarQueries bars,
    IOpenBookSettings openBook) : IPlanContextSource
{
    /// <summary>Calendar days of daily bars read before the close of the session (about 60 sessions plus slack).</summary>
    public const int ReturnLookbackDays = 120;

    public async ValueTask<Result<PlanContext>> ReadAsync(PlanContextRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var active = await accounts.ListActiveAsync(cancellationToken);
        var trading = active.Where(a => a.Trading.Enabled).OrderBy(a => a.Id).ToList();
        var profileById = (await profiles.ListAsync(cancellationToken)).ToDictionary(p => p.Id);

        // shortcut: accounts, risk profiles and holdings are read as of now, not as of the close of the session; upgrade when a plan is built for an older session or a re-run must reproduce a past night (needs dated account and holding history).
        var rows = await holdings.ListAsync(new HoldingFilter(null, null, null, null), cancellationToken);
        var open = rows.Where(h => h.ClosedAt is null && h.Qty > 0).ToList();
        var contextAccounts = trading.Select(a => ToAccount(a, open, profileById, openBook.AssumedEmpty)).ToList();

        var ids = request.Instruments.Where(i => i.Id is not null).Select(i => i.Id!.Value).Distinct().ToList();
        var sectors = ids.Count == 0 ? new Dictionary<Guid, string>() : (await classifications.SectorsAsync(ids, cancellationToken)).ToDictionary(p => p.Key, p => p.Value);
        var baskets = ids.Count == 0 ? new Dictionary<Guid, IReadOnlyList<BasketRef>>() : (await classifications.BasketsAsync(ids, cancellationToken)).ToDictionary(p => p.Key, p => p.Value);
        var heldIds = open.Where(h => h.InstrumentId is not null).Select(h => h.InstrumentId!.Value).ToHashSet();
        var heldSymbols = open.Select(h => (h.CanonicalSymbol ?? h.TradingSymbol).ToUpperInvariant()).ToHashSet(StringComparer.Ordinal);

        var facts = new Dictionary<string, InstrumentFacts>(StringComparer.Ordinal);
        foreach (var i in request.Instruments)
        {
            var held = (i.Id is { } id && heldIds.Contains(id)) || heldSymbols.Contains(i.Symbol.ToUpperInvariant());
            facts[i.Symbol.ToUpperInvariant()] = new InstrumentFacts(
                i.Id is { } sid && sectors.TryGetValue(sid, out var sector) ? sector : null,
                i.Id is { } bid ? baskets.TryGetValue(bid, out var list) ? [.. list.Select(b => b.Name).Order(StringComparer.Ordinal)] : [] : null,
                held);
        }

        var correlations = await CorrelationsAsync(request, cancellationToken);
        var notes = new List<string> { openBook.AssumedEmpty ? PlanContextNotes.NoBookNote : PlanContextNotes.BookUnavailableNote };
        if (!contextAccounts.Any(a => a.Mode == TradingMode.Paper))
        {
            notes.Add(PlanContextNotes.NoAccountNote);
        }

        return Result.Success(new PlanContext(openBook.AssumedEmpty ? [] : null, facts, contextAccounts, correlations, null, null, notes));
    }

    internal static ContextAccount ToAccount(BrokerAccount account, List<HoldingData> open, Dictionary<Guid, RiskProfile> profiles, bool bookAssumedEmpty)
    {
        var t = account.Trading;
        var profile = t.RiskProfileId is { } pid && profiles.TryGetValue(pid, out var p) ? p : null;
        var overrides = t.Overrides;
        var equity = t.CapitalSource == CapitalSource.Fixed && t.CapitalAmount is { } amount ? (double?)(double)amount : null;
        var held = account.Portfolio.Enabled
            ? (IReadOnlySet<string>)open.Where(h => h.AccountId == account.Id).Select(h => (h.CanonicalSymbol ?? h.TradingSymbol).ToUpperInvariant()).ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
        return new ContextAccount(
            account.Id,
            t.Mode,
            equity,
            D(overrides.RiskPerTradePct ?? profile?.RiskPerTradePct),
            D(profile?.MaxCapitalPerTradePct),
            D(profile?.MaxTotalRiskPct),
            bookAssumedEmpty ? 0 : null,
            equity,
            profile?.MaxPerSector,
            D(overrides.DailyLossPct ?? profile?.DailyLossPct),
            bookAssumedEmpty ? 0 : null,
            held);
    }

    private static double? D(decimal? value) => value is { } v ? (double)v : null;

    // shortcut: closes are unadjusted for splits and bonus issues (a split shows as a fake return) and there is no outlier guard; upgrade when an adjusted close series is wired in (or returns beyond +-25 % are dropped).
    // shortcut: one serial daily-bar read per ranked instrument, with no cap on how many; upgrade when the ranked list grows past about 100 instruments (one batched query, or a cap with an "unavailable" note).
    private async Task<CorrelationTable?> CorrelationsAsync(PlanContextRequest request, CancellationToken cancellationToken)
    {
        var withIds = request.Instruments.Where(i => i.Id is not null).ToList();
        if (withIds.Count < 2)
        {
            return null;
        }

        var returns = new Dictionary<string, IReadOnlyList<DailyReturn>>(StringComparer.Ordinal);
        foreach (var i in withIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Day bars up to and including the session; the pure series drops anything later and any bar that is not complete.
            var daily = await bars.ReadDailyBarsAsync(i.Id!.Value, request.Session.AddDays(-ReturnLookbackDays), request.Session.AddDays(1), cancellationToken);
            returns[i.Symbol] = ReturnSeries.FromCloses(daily.Where(b => b.Complete).Select(b => (DateOnly.FromDateTime(b.Ts.ToOffset(TimeSpan.FromHours(5.5)).DateTime), b.Close)), request.Session);
        }

        return CorrelationTable.FromReturns(returns, request.ForDate, [.. withIds.Select(i => i.Symbol)]);
    }
}
