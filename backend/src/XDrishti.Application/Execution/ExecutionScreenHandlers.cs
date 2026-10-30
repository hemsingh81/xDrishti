using System.Globalization;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.LiveTrading;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;

namespace XDrishti.Application.Execution;

/// <summary>Today: per account the effective mode, the meters against the sheet in force (the pending raise beside it), the open tickets, halts and alarms, and the evidence line. Read only.</summary>
internal sealed class GetExecutionOverviewHandler(
    IExecutionScreenReader reader,
    IQueryHandler<GetExecutionModesQuery, ExecutionModesView> modes,
    IQueryHandler<GetExecutionStatusQuery, ExecutionStatusView> status,
    IQueryHandler<GetExecutionReadinessQuery, Result<ExecutionReadinessView>> readiness,
    IQueryHandler<GetExecutionPositionsQuery, Result<IReadOnlyList<PositionRow>>> positions,
    IExecutionLimitsAccess limits,
    IExecutionHaltReader halts,
    IExecutionAlarmReader alarms,
    IExecutionReconReader recon,
    TimeProvider clock,
    ILogger<GetExecutionOverviewHandler> logger) : IQueryHandler<GetExecutionOverviewQuery, Result<ExecutionOverview>>
{
    public async ValueTask<Result<ExecutionOverview>> HandleAsync(GetExecutionOverviewQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var now = clock.GetUtcNow();
        var today = IndiaTime.Today(now);
        var date = query.Date ?? today;
        var modeView = await modes.HandleAsync(new GetExecutionModesQuery(), cancellationToken);
        var statusView = await status.HandleAsync(new GetExecutionStatusQuery(), cancellationToken);
        var readinessView = await readiness.HandleAsync(new GetExecutionReadinessQuery(date), cancellationToken);
        var day = (await reader.DayAsync(date, now, cancellationToken)).ToDictionary(d => d.AccountId);
        var (canary, enabled) = await reader.SwitchesAsync(cancellationToken);
        var activeHalts = await halts.ReadAsync(true, null, GetExecutionHaltsHandler.MaxTake, cancellationToken);
        var openAlarms = await alarms.ReadAsync(true, null, GetExecutionAlarmsHandler.MaxTake, cancellationToken);
        var reconView = await recon.ReadAsync(date, null, GetExecutionAlarmsHandler.MaxTake, cancellationToken);
        var open = await positions.HandleAsync(new GetExecutionPositionsQuery(null), cancellationToken);
        var gates = statusView.Accounts.ToDictionary(a => a.AccountId, a => a.Gate);
        var ready = readinessView.IsSuccess ? readinessView.Value.Accounts.ToDictionary(a => a.AccountId) : [];
        var accounts = new List<OverviewAccount>();
        foreach (var account in modeView.Accounts)
        {
            var rows = await limits.RowsAsync(account.AccountId, LimitsReading.HistorySize, cancellationToken);
            var (inForce, pending) = LimitsReading.Read(rows, await limits.NowAsync(cancellationToken));
            day.TryGetValue(account.AccountId, out var raw);
            var effectiveFrom = account.Trail.FirstOrDefault(t => t.EffectiveFrom <= now)?.EffectiveFrom;
            var atRisk = open.IsSuccess ? open.Value.Count(p => p.AccountId == account.AccountId && p.Protection != "confirmed") : 0;
            accounts.Add(new OverviewAccount(
                account.AccountId, account.Name, account.EffectiveMode, effectiveFrom, account.Pending?.To, account.Pending?.EffectiveFrom,
                gates.TryGetValue(account.AccountId, out var gate) ? gate : new ExecutionGateView(false, "no_mode_answer"),
                ready.TryGetValue(account.AccountId, out var r) ? new OverviewReadiness(r.State, r.CheckedAt, r.TokenExpiresAt) : new OverviewReadiness("unavailable", null, null),
                Caps(raw, inForce?.Limits, pending is null ? null : LimitsReading.Row(pending), inForce?.Id, date == today),
                raw?.OpenTickets ?? 0,
                [.. activeHalts.Where(h => h.AccountId is null || h.AccountId == account.AccountId)],
                [.. openAlarms.Where(a => a.AccountId is null || a.AccountId == account.AccountId)],
                atRisk));
        }

        ExecutionScreens.OverviewRead(logger, accounts.Count);
        return Result.Success(new ExecutionOverview(date, canary, enabled, accounts, Evidence(reconView)));
    }

    private static OverviewCaps Caps(DayAccountRaw? raw, XDrishti.Domain.Execution.Limits.ExecutionLimits? sheet, LimitsRowView? pending, long? limitsId, bool isToday)
    {
        var none = sheet is null ? "no_limits_sheet" : null;
        ExecMetric Count(decimal used, decimal? cap) => cap is null ? ExecMetric.Of(used, null, none ?? "cap_unset") : ExecMetric.Of(used, cap);
        var orders = Count(raw?.Orders ?? 0, sheet?.MaxOrdersPerDay);
        var notional = Count(raw?.Notional ?? 0m, sheet?.MaxNotionalPerDay);
        var positions = isToday ? Count(raw?.OpenPositions ?? 0, sheet?.MaxOpenPositions) : ExecMetric.Unavailable("positions_are_current_only");
        return new OverviewCaps(orders, notional, WorstCase(raw, sheet?.MaxLossPerDay, none), positions, limitsId, pending);
    }

    /// <summary>The day's worst case as P12-09 counts it: the realised loss (a profit gives no room) plus the open risk to the stops, stops assumed to fill at their price.</summary>
    internal static ExecMetric WorstCase(DayAccountRaw? raw, decimal? cap, string? none)
    {
        if (raw is null || raw.FilledEntries == 0)
        {
            return cap is null ? ExecMetric.Of(0m, null, none ?? "cap_unset", 0) : ExecMetric.Of(0m, cap, null, 0);
        }

        if (raw.FillsInconsistent)
        {
            return ExecMetric.Unavailable("fills_inconsistent");
        }

        if (raw.Realised is not { } realised)
        {
            return ExecMetric.Unavailable("no_day_mark");
        }

        var worst = Math.Max(0m, -realised) + raw.OpenRiskToStops;
        return cap is null ? ExecMetric.Of(worst, null, none ?? "cap_unset", raw.FilledEntries) : ExecMetric.Of(worst, cap, null, raw.FilledEntries);
    }

    private static List<string> Evidence(ExecutionReconciliationView recon)
    {
        var lines = new List<string>
        {
            "Basis: our own records and the broker's order stream; marks are at the last price, costs are not included, and risk to the stop assumes the stop fills at its price.",
        };
        var lastRead = recon.Runs.Where(r => r.Readable).Select(r => (DateTimeOffset?)r.At).DefaultIfEmpty().Max();
        lines.Add(lastRead is { } at ? "Last broker read: " + IndiaTime.ToIst(at).ToString("HH:mm:ss", CultureInfo.InvariantCulture) + " IST." : "Last broker read: none recorded for this day.");
        var breaks = recon.Breaks.Count(b => b.Resolution is null);
        lines.Add(recon.Runs.Count == 0 ? "Reconciliation: not run for this day." : breaks == 0 ? "Reconciliation: " + recon.Runs.Count.ToString(CultureInfo.InvariantCulture) + " run(s), no open break." : "Reconciliation: " + breaks.ToString(CultureInfo.InvariantCulture) + " break(s) not yet resolved.");
        return lines;
    }
}

/// <summary>The orders of a day, newest first, 50 a page, in P12-01's state vocabulary. Slippage is per fill against the ticket price.</summary>
internal sealed class GetExecutionBlotterHandler(IExecutionScreenReader reader, ILiveExecutionMarket market, TimeProvider clock, ILogger<GetExecutionBlotterHandler> logger) : IQueryHandler<GetExecutionBlotterQuery, Result<IReadOnlyList<BlotterRow>>>
{
    public async ValueTask<Result<IReadOnlyList<BlotterRow>>> HandleAsync(GetExecutionBlotterQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.StateGroup is not null && !ExecutionScreens.StateGroups.Contains(query.StateGroup))
        {
            ExecutionScreens.Refused(logger, "state_group");
            return Result.Failure<IReadOnlyList<BlotterRow>>(ExecutionScreens.Invalid("stateGroup must be one of " + string.Join(", ", ExecutionScreens.StateGroups) + "."));
        }

        if (query.Page is < 1 or > ExecutionScreens.MaxPage)
        {
            ExecutionScreens.Refused(logger, "page");
            return Result.Failure<IReadOnlyList<BlotterRow>>(ExecutionScreens.Invalid("page must be 1 to " + ExecutionScreens.MaxPage.ToString(CultureInfo.InvariantCulture) + "."));
        }

        var date = query.Date ?? IndiaTime.Today(clock.GetUtcNow());
        var raw = await reader.BlotterAsync(date, query.AccountId, query.StateGroup is null ? null : ExecutionScreens.StatesOf(query.StateGroup), (query.Page - 1) * ExecutionScreens.PageSize, ExecutionScreens.PageSize, cancellationToken);
        var ticks = new Dictionary<string, decimal?>(StringComparer.Ordinal);
        var rows = new List<BlotterRow>();
        foreach (var o in raw)
        {
            if (!ticks.TryGetValue(o.Symbol, out var tick))
            {
                ticks[o.Symbol] = tick = await market.TickAsync(o.Symbol, cancellationToken);
            }

            var fills = o.Fills.Select(f => Slip(o.Side, o.EntryPrice, f, tick)).ToList();
            var fillQty = o.Fills.Sum(f => f.Quantity);
            decimal? vwap = fillQty == 0 ? null : decimal.Round(o.Fills.Sum(f => f.Quantity * f.Price) / fillQty, 4);
            var overall = vwap is { } v ? Slip(o.Side, o.EntryPrice, new BlotterFillRaw(o.At, fillQty, v), tick) : null;
            var known = ExecutionScreens.StateWords.Contains(o.State);
            rows.Add(new BlotterRow(
                o.IntentId, o.At, o.AccountId, o.Account, o.Symbol, o.Side, o.Quantity, "limit", o.EntryPrice, o.State, ExecutionScreens.GroupOf(o.State), known,
                o.ActionBy ?? (o.Assisted ? "owner" : "system"), o.Quantity == 1, o.ClientOrderId, o.BrokerOrderId, o.AlgoId, o.FilledQuantity, vwap,
                overall?.SlippageTicks ?? ExecMetric.Empty("unfilled"), overall?.SlippagePercent ?? ExecMetric.Empty("unfilled"), fills, o.RejectionCodes, o.BrokerCode));
        }

        ExecutionScreens.BlotterRead(logger, rows.Count, query.Page);
        return Result.Success<IReadOnlyList<BlotterRow>>(rows);
    }

    /// <summary>Positive when the fill is worse for the owner than the ticket price (a buy above it, a sell below it).</summary>
    private static BlotterFill Slip(string side, decimal ticket, BlotterFillRaw fill, decimal? tick)
    {
        var diff = (fill.Price - ticket) * (string.Equals(side, "buy", StringComparison.Ordinal) ? 1m : -1m);
        var percent = ExecMetric.Of(decimal.Round(diff / ticket * 100m, 4));
        var ticks = tick is { } t and > 0m ? ExecMetric.Of(decimal.Round(diff / t, 2)) : ExecMetric.Unavailable("no_tick_size");
        return new BlotterFill(fill.At, fill.Quantity, fill.Price, ticks, percent);
    }
}

/// <summary>The open positions with protection, source and reconciliation. Protection is <c>confirmed</c> only from a recent, clean read of the broker's order book.</summary>
internal sealed class GetExecutionPositionsHandler(IExecutionScreenReader reader, IExecutionReconReader recon, ILiveExecutionMarket market, TimeProvider clock, ILogger<GetExecutionPositionsHandler> logger) : IQueryHandler<GetExecutionPositionsQuery, Result<IReadOnlyList<PositionRow>>>
{
    /// <summary>The registry default of <c>exec.feed_max_age_s</c>; a minute bar is the finest price on record, so a bar inside this age plus one minute counts as current.</summary>
    private static readonly TimeSpan FeedMaxAge = TimeSpan.FromSeconds(5);

    public async ValueTask<Result<IReadOnlyList<PositionRow>>> HandleAsync(GetExecutionPositionsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var now = clock.GetUtcNow();
        var today = IndiaTime.Today(now);
        var raw = await reader.PositionsAsync(query.AccountId, cancellationToken);
        var reconView = raw.Count == 0 ? new ExecutionReconciliationView([], []) : await recon.ReadAsync(today, query.AccountId, GetExecutionAlarmsHandler.MaxTake, cancellationToken);
        var rows = new List<PositionRow>();
        foreach (var p in raw)
        {
            var run = reconView.Runs.Where(r => r.AccountId == p.AccountId).MaxBy(r => r.Id);
            var runBreaks = run is null ? [] : reconView.Breaks.Where(b => b.RunId == run.Id && string.Equals(b.Symbol, p.Symbol, StringComparison.OrdinalIgnoreCase)).ToList();
            var reconciliation = run is null ? "not_run" : !run.Readable ? "unavailable" : runBreaks.Count > 0 ? "break" : "matched";
            var fresh = run is { Readable: true } && now - run.At <= ExecutionScreens.ProtectionReadLimit && runBreaks.Count == 0;
            var protection = p.BreachOpen ? "missing" : p.HasIntent && p.IntentState is "protected" or "managing" && fresh ? "confirmed" : "unknown";
            var last = await PriceAsync(p.Symbol, today, now, cancellationToken);
            var unrealised = last.Value is { } price ? ExecMetric.Of(decimal.Round((price - p.AveragePrice) * p.NetQuantity, 2)) : ExecMetric.Unavailable(last.Reason ?? "no_price");
            var risk = p.StopPrice is { } stop ? ExecMetric.Of(decimal.Round(Math.Max(0m, (p.AveragePrice - stop) * p.NetQuantity), 2)) : ExecMetric.Unavailable("no_stop_on_record");
            rows.Add(new PositionRow(p.AccountId, p.Account, p.Symbol, p.NetQuantity, p.AveragePrice, last, unrealised, risk, p.StopPrice, protection, p.HasIntent ? "ours" : "foreign", reconciliation));
        }

        ExecutionScreens.PositionsRead(logger, rows.Count);
        return Result.Success<IReadOnlyList<PositionRow>>(rows);
    }

    private async ValueTask<ExecMetric> PriceAsync(string symbol, DateOnly today, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var window = await market.MinuteExtremesAsync(symbol, now - FeedMaxAge - TimeSpan.FromMinutes(1), now, cancellationToken);
        if (window.High is null)
        {
            return ExecMetric.Unavailable("price_older_than_feed_max_age");
        }

        return await market.LastPriceAsync(symbol, today, now, cancellationToken) is { } price ? ExecMetric.Of(price) : ExecMetric.Unavailable("no_price");
    }
}

/// <summary>The action log with the alarms, halts, drills, mode and settings changes, newest first, 50 a page. Read only; the log is insert-only.</summary>
internal sealed class GetExecutionActivityHandler(IExecutionScreenReader reader, ILogger<GetExecutionActivityHandler> logger) : IQueryHandler<GetExecutionActivityQuery, Result<IReadOnlyList<ActivityRow>>>
{
    public async ValueTask<Result<IReadOnlyList<ActivityRow>>> HandleAsync(GetExecutionActivityQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Kind is not null && !ExecutionScreens.Kinds.Contains(query.Kind))
        {
            ExecutionScreens.Refused(logger, "kind");
            return Result.Failure<IReadOnlyList<ActivityRow>>(ExecutionScreens.Invalid("kind must be one of " + string.Join(", ", ExecutionScreens.Kinds) + "."));
        }

        if (query.By is not null && !ExecutionScreens.Actors.Contains(query.By))
        {
            ExecutionScreens.Refused(logger, "by");
            return Result.Failure<IReadOnlyList<ActivityRow>>(ExecutionScreens.Invalid("by must be one of " + string.Join(", ", ExecutionScreens.Actors) + "."));
        }

        if (query.Page is < 1 or > ExecutionScreens.MaxPage)
        {
            ExecutionScreens.Refused(logger, "page");
            return Result.Failure<IReadOnlyList<ActivityRow>>(ExecutionScreens.Invalid("page must be 1 to " + ExecutionScreens.MaxPage.ToString(CultureInfo.InvariantCulture) + "."));
        }

        var rows = await reader.ActivityAsync(query.Date, query.Kind, query.By, (query.Page - 1) * ExecutionScreens.PageSize, ExecutionScreens.PageSize, cancellationToken);
        ExecutionScreens.ActivityRead(logger, rows.Count, query.Page);
        return Result.Success(rows);
    }
}

/// <summary>The bell's execution items, derived on every read from insert-only rows (spec P12-10 D5). Nothing is stored.</summary>
internal sealed class GetExecutionNoticesHandler(
    IExecutionScreenReader reader,
    IQueryHandler<GetExecutionModesQuery, ExecutionModesView> modes,
    IQueryHandler<GetExecutionReadinessQuery, Result<ExecutionReadinessView>> readiness,
    IQueryHandler<GetExecutionOverviewQuery, Result<ExecutionOverview>> overview,
    IExecutionHaltReader halts,
    IExecutionAlarmReader alarms,
    IExecutionReconReader recon,
    ITradingCalendar calendar,
    TimeProvider clock,
    ILogger<GetExecutionNoticesHandler> logger) : IQueryHandler<GetExecutionNoticesQuery, ExecutionNotices>
{
    public async ValueTask<ExecutionNotices> HandleAsync(GetExecutionNoticesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var now = clock.GetUtcNow();
        var today = IndiaTime.Today(now);
        var raw = await reader.NoticesAsync(now - ExecutionNoticeDeriver.Keep, now, cancellationToken);
        var overviewResult = await overview.HandleAsync(new GetExecutionOverviewQuery(today), cancellationToken);
        var readinessResult = await readiness.HandleAsync(new GetExecutionReadinessQuery(today), cancellationToken);
        var input = new ExecutionNoticeInput(
            now, (await calendar.GetAsync(cancellationToken)).IsTradingDay(today), raw,
            await alarms.ReadAsync(false, null, GetExecutionAlarmsHandler.MaxTake, cancellationToken),
            await halts.ReadAsync(false, null, GetExecutionHaltsHandler.MaxTake, cancellationToken),
            (await recon.ReadAsync(null, null, GetExecutionAlarmsHandler.MaxTake, cancellationToken)).Breaks,
            (await modes.HandleAsync(new GetExecutionModesQuery(), cancellationToken)).Accounts,
            overviewResult.IsSuccess ? overviewResult.Value.Accounts : [],
            readinessResult.IsSuccess ? readinessResult.Value.Accounts : []);
        var items = ExecutionNoticeDeriver.Derive(input);
        var unresolved = items.Count(i => !i.Resolved);
        ExecutionScreens.NoticesRead(logger, items.Count, unresolved);
        return new ExecutionNotices(items);
    }
}
