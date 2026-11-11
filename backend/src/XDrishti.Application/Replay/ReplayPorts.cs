using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Abstractions.Selection;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Selection;
using XDrishti.Domain.Selection.SmartRules;

namespace XDrishti.Application.Replay;

/// <summary>The candidates already read for the session, handed to the live plan builder through its own port.</summary>
internal sealed class FixedTicketSource(TicketSourceResult found) : ITicketSource
{
    public ValueTask<Result<TicketSourceResult>> ReadAsync(DateOnly session, DateOnly forDate, CancellationToken cancellationToken) => ValueTask.FromResult(Result.Success(found));
}

/// <summary>The size-scaling inputs, read as of the session through the replay facts.</summary>
internal sealed class ReplaySizingInputs(IReplayFacts facts) : ISizingInputs
{
    public ValueTask<SizingInputs> ReadAsync(DateOnly session, DateOnly forDate, IReadOnlyList<ContextInstrument> instruments, CancellationToken cancellationToken) =>
        facts.SizingAsync(session, forDate, instruments, cancellationToken);
}

/// <summary>The basket settings of the snapshot: reads only, and a write is refused (the replay changes no setting).</summary>
internal sealed class SnapshotBasketSettings(BasketSettingsState state) : IBasketSettingsStore
{
    public ValueTask<BasketSettingsState> CurrentAsync(CancellationToken cancellationToken) => ValueTask.FromResult(state);

    public ValueTask<IReadOnlyList<BasketSettingChangeRow>> HistoryAsync(int limit, CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<BasketSettingChangeRow>>([]);

    public ValueTask<bool> AppendAsync(IReadOnlyList<BasketSettingChange> rows, long expectedVersion, CancellationToken cancellationToken) => throw new NotSupportedException("The replay changes no setting.");
}

/// <summary>
/// The plan context of a replayed session: the synthetic replay account sized from the replay equity (so sizing compounds), the open positions of the replay book with their sector and baskets, the equity throttle's counted trades from the book, and the market facts as of the session.
/// Nothing is read from the live accounts, holdings or paper ledger.
/// </summary>
internal sealed class ReplayContextSource(IReplayFacts facts, ReplayRunContext run, ReplayBook book, DateOnly session) : IPlanContextSource
{
    public async ValueTask<Result<PlanContext>> ReadAsync(PlanContextRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var figures = book.FiguresAsOf(session);
        var open = book.OpenAt(session);
        var held = open.Select(t => new ContextInstrument(t.InstrumentId, t.Item.Symbol)).ToList();
        var all = request.Instruments.Concat(held.Where(h => !request.Instruments.Any(r => string.Equals(r.Symbol, h.Symbol, StringComparison.Ordinal)))).ToList();
        var market = await facts.MarketAsync(session, request.ForDate, all, request.Instruments, cancellationToken);
        var equity = figures.Equity is > 0 ? (double?)figures.Equity : null;
        var positions = new List<BookPosition>();
        foreach (var t in open)
        {
            var symbol = t.Item.Symbol.ToUpperInvariant();
            var seen = market.Instruments.GetValueOrDefault(symbol);
            var ledger = t.LedgerAsOf(session, ReplayBook.AccountId);
            positions.Add(new BookPosition(symbol, t.Item.Side, t.Swing ? "swing" : "intraday", equity is { } e ? (double)ledger.RiskAmount / e * 100 : 0, seen?.Sector, seen?.Baskets));
        }

        var p = run.Parameters.Profile;
        var account = new ContextAccount(
            ReplayBook.AccountId, TradingMode.Paper, equity, p.RiskPerTradePct, p.MaxCapitalPerTradePct, p.MaxTotalRiskPct,
            equity is null ? null : (double?)figures.OpenRiskPct, equity is null ? null : (double?)figures.FundsAvailable, p.MaxPerSector, p.DailyLossPct, equity is null ? null : (double?)figures.DailyLossPct,
            open.Select(t => t.Item.Symbol.ToUpperInvariant()).ToHashSet(StringComparer.Ordinal));
        var taken = book.LedgerAsOf(session)
            .Where(t => t.Closed)
            .OrderBy(t => t.Exit!.ExitAt).ThenBy(t => t.EntryId)
            .GroupBy(t => t.TradeKey, StringComparer.Ordinal)
            .Select(g => g.First())
            .Select(t => new TakenTrade(t.TradeKey, XDrishti.Domain.Market.IndiaTime.Today(t.Exit!.ExitAt), t.Exit.RSim))
            .OrderBy(t => t.ClosedOn).ThenBy(t => t.TradeId, StringComparer.Ordinal)
            .ToList();
        return Result.Success(new PlanContext(positions, market.Instruments, [account], market.Correlations, market.Gate, taken, ["replay book: the paper ledger of the replay account"], BookFromLedger: true, MarketNote: market.MarketNote));
    }
}
