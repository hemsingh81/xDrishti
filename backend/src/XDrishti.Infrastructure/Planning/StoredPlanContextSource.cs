using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.LiveTrading;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.Context;
using XDrishti.Application.Abstractions.Paper;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Checklist;
using XDrishti.Domain.Common;
using XDrishti.Domain.LiveTrading;
using XDrishti.Domain.Paper;
using XDrishti.Domain.Selection.SmartRules;

namespace XDrishti.Infrastructure.Planning;

/// <summary>
/// The facts of a plan from stored data (spec P7-09, ADR 0092): the Trading-role accounts with their risk profile and overrides, the synced holdings, the sector and baskets of each instrument, and the correlation of daily returns up to the close
/// of the session. Read only; nothing here talks to a broker or writes anything.
/// <b>What has no data source is null</b> (unavailable downstream, never a pass): the events figure of the market gate (P15), real taken trades, the funds of a broker-funds account.
/// <b>The open book is unavailable by default</b> (the system keeps no position record for a Trading account: no journal yet, P7-14): book, open risk and the loss today are null, so the fill takes nothing and the plan says so. Only when the owner sets
/// <c>Planning:OpenBook:AssumedEmpty</c> are they the empty book and zero, and the plan says that instead. Positions held outside this system (the broker's own app) are NOT known and not counted.
/// </summary>
internal sealed class StoredPlanContextSource(
    IAccountStore accounts,
    IRiskProfileStore profiles,
    IHoldingQueries holdings,
    IHoldingClassifications classifications,
    IBarQueries bars,
    IOpenBookSettings openBook,
    IPaperBookReader paperBook,
    IContextFigures contextFigures,
    IRampStore ramp,
    ILiveRampSettings rampSettings) : IPlanContextSource
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

        // Only when the owner turned UsePaperLedger on (default off: nothing below runs and the context is byte-identical to P7-09). The book is the paper ledger, or unavailable when capture is behind.
        var paper = openBook.UsePaperLedger
            ? await paperBook.ReadAsync(request.Session, [.. trading.Where(a => a.Trading.Mode == TradingMode.Paper).Select(PaperAccountInput.From)], cancellationToken)
            : null;
        if (paper is PaperBook ledger)
        {
            contextAccounts = [.. contextAccounts.Select(a => WithLedger(a, ledger))];
        }

        // A manual-live account is sized only from its ramp step (spec P11-04): its scale and ramp event ride on the context; with no step it has none and gets nothing. The halt, capture and loss facts of its live-ready gate have no source yet (P11-01, P11-04b): they stay unknown, which is not a pass.
        if (contextAccounts.Any(a => a.Mode == TradingMode.ManualLive))
        {
            var steps = await ramp.CurrentAllAsync(cancellationToken);
            contextAccounts = [.. contextAccounts.Select(a => a.Mode == TradingMode.ManualLive ? WithRamp(a, steps.GetValueOrDefault(a.AccountId), rampSettings.StepFactors) : a)];
        }

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
        var (gate, marketNote) = await GateAsync(request.ForDate, cancellationToken);
        var notes = new List<string> { paper switch { PaperBook b => string.Join(' ', b.Notes), PaperBookUnavailable u => u.Reason, _ => openBook.AssumedEmpty ? PlanContextNotes.NoBookNote : PlanContextNotes.BookUnavailableNote } };
        if (!contextAccounts.Any(a => a.Mode == TradingMode.Paper))
        {
            notes.Add(PlanContextNotes.NoAccountNote);
        }

        return paper is PaperBook book
            ? Result.Success(new PlanContext(book.Positions, facts, contextAccounts, correlations, gate, book.Taken, notes, BookFromLedger: true, MarketNote: marketNote))
            : Result.Success(new PlanContext(openBook.AssumedEmpty ? [] : null, facts, contextAccounts, correlations, gate, null, notes, MarketNote: marketNote));
    }

    /// <summary>
    /// The market figures of the latest stored context day before the planned date (P3-01d/e): the VIX, and the breadth of the owner's tracked shares. The context holds the breadth as a percent (0 to 100) and <c>SmartRulesOptions.BreadthMin</c> is a
    /// fraction (0 to 1): the one conversion between them is here. The events figure has no source before P15 and stays unknown, so the gate reads normal only when it is known. No day, a stale day or no usable figure is no gate (unavailable, never a pass).
    /// </summary>
    private async ValueTask<(MarketGateInput? Gate, string? Note)> GateAsync(DateOnly forDate, CancellationToken cancellationToken)
    {
        var read = await contextFigures.AsOfAsync(forDate, cancellationToken);
        if (read.Figures is not { } figures || (figures.Vix is null && figures.Breadth is null))
        {
            return (null, null);
        }

        var gate = new MarketGateInput(figures.AsOf, figures.Vix, figures.Breadth is { } percent ? percent / 100.0 : null, null);
        return (gate, figures.Breadth is null ? null : PlanContextNotes.MarketBreadth(figures.BreadthUniverse));
    }

    /// <summary>A paper account with the ledger's equity, open risk, funds, held symbols and loss carried; an account the ledger has no capital for keeps null numbers (the plan treats its limits as unknown).</summary>
    private static ContextAccount WithLedger(ContextAccount account, PaperBook book) =>
        book.Accounts.FirstOrDefault(b => b.AccountId == account.AccountId) is { } b && account.Mode == TradingMode.Paper
            ? account with { Equity = b.Equity, OpenRiskPct = b.OpenRiskPct, FundsAvailable = b.FundsAvailable, DailyLossPct = b.DailyLossPct, HeldSymbols = b.HeldSymbols }
            : account;

    internal static ContextAccount WithRamp(ContextAccount account, RampState? state, IReadOnlyList<double> factors) =>
        account with { RampScale = RampPolicy.Scale(state, factors), RampEventId = state?.EventId, LiveReady = new LiveReadyFacts(state is not null, null, null, null) };

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
