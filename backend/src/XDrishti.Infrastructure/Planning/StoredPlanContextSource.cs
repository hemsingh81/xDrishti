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
/// <b>The open book is the empty list, by stated assumption</b>: the system keeps no position record for a Trading account (no journal yet, P7-14; P7 places no order, paper days start in P10), so the open risk, the open trades and the loss today
/// are zero, and the plan says so. Positions held outside this system (the broker's own app) are NOT known and not counted.
/// </summary>
internal sealed class StoredPlanContextSource(
    IAccountStore accounts,
    IRiskProfileStore profiles,
    IHoldingQueries holdings,
    IHoldingClassifications classifications,
    IBarQueries bars) : IPlanContextSource
{
    /// <summary>Calendar days of daily bars read before the close of the session (about 60 sessions plus slack).</summary>
    public const int ReturnLookbackDays = 120;

    public const string NoBookNote = "Open book: none recorded (no journal yet); positions held outside this system are not counted.";

    public const string NoAccountNote = "No Trading account to size for.";

    public async ValueTask<Result<PlanContext>> ReadAsync(PlanContextRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var active = await accounts.ListActiveAsync(cancellationToken);
        var trading = active.Where(a => a.Trading.Enabled).OrderBy(a => a.Id).ToList();
        var profileById = (await profiles.ListAsync(cancellationToken)).ToDictionary(p => p.Id);

        var rows = await holdings.ListAsync(new HoldingFilter(null, null, null, null), cancellationToken);
        var open = rows.Where(h => h.ClosedAt is null && h.Qty > 0).ToList();
        var contextAccounts = trading.Select(a => ToAccount(a, open, profileById)).ToList();

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
        var notes = new List<string> { NoBookNote };
        if (!contextAccounts.Any(a => a.Mode == TradingMode.Paper))
        {
            notes.Add(NoAccountNote);
        }

        return Result.Success(new PlanContext([], facts, contextAccounts, correlations, null, null, notes));
    }

    private static ContextAccount ToAccount(BrokerAccount account, List<HoldingData> open, Dictionary<Guid, RiskProfile> profiles)
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
            0,
            equity,
            profile?.MaxPerSector,
            D(overrides.DailyLossPct ?? profile?.DailyLossPct),
            0,
            held);
    }

    private static double? D(decimal? value) => value is { } v ? (double)v : null;

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
