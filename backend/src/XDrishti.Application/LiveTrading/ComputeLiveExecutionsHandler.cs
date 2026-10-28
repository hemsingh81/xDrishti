using System.Globalization;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.LiveTrading;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.LiveTrading;
using XDrishti.Domain.Market;
using XDrishti.Domain.Planning;

namespace XDrishti.Application.LiveTrading;

/// <summary>
/// The step of the live settle that measures what the owner did (spec P11-02, ADR 0150): for each settled session of each manual-live account, one <c>live_executions</c> row per entry outcome (filled, partial, over_allocation) - the entry against the
/// ticket's level or the plan grade, split into delay and execution, in its liquidity bucket - and one class per entry order for the capacity score (full, partial, rejected, queue miss, price miss, unclassified). It reads stored fills, orders, grades, bars and
/// the instrument master; it writes insert-only rows once (idempotent). It changes no trading behaviour and nothing here reaches a broker.
/// </summary>
internal sealed class ComputeLiveExecutionsHandler(
    IAccountStore accounts,
    ILiveCaptureStore capture,
    ILiveExecutionStore store,
    ILiveExecutionMarket market,
    LiveTicketSource tickets,
    IGradeStore grades,
    ILiveEstimateSettings settings,
    ITradingCalendar calendars,
    TimeProvider time,
    ILogger<ComputeLiveExecutionsHandler> logger) : ICommandHandler<ComputeLiveExecutionsCommand, LiveExecutionsOutcome>
{
    private static readonly string[] EntryOutcomes = [OutcomeCodes.Filled, OutcomeCodes.Partial, OutcomeCodes.OverAllocation];

    public async ValueTask<Result<LiveExecutionsOutcome>> HandleAsync(ComputeLiveExecutionsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var now = time.GetUtcNow();
        var today = IndiaTime.Today(now);
        if (command.Date is { } d && d > today)
        {
            return Result.Failure<LiveExecutionsOutcome>(Error.Validation(LiveErrors.Invalid, "date must not be after today"));
        }

        var calendar = await calendars.GetAsync(cancellationToken);
        var dates = command.Date is { } one
            ? new List<DateOnly> { one }
            : [.. Enumerable.Range(0, SettleLiveSessionHandler.SweepDays + 1).Select(i => today.AddDays(i - SettleLiveSessionHandler.SweepDays)).Where(calendar.IsTradingDay)];
        var live = (await accounts.ListActiveAsync(cancellationToken)).Where(a => a.Trading is { Enabled: true, Mode: TradingMode.ManualLive } && (command.AccountId is null || a.Id == command.AccountId)).ToList();
        int sessions = 0, executions = 0, classes = 0, failed = 0;
        foreach (var account in live)
        {
            foreach (var date in dates)
            {
                try
                {
                    var (e, c, ran) = await SessionAsync(account.Id, date, cancellationToken);
                    sessions += ran ? 1 : 0;
                    executions += e;
                    classes += c;
                }
                catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
                {
                    failed++;
                    LiveCaptureLog.ExecutionsSkipped(logger, "session_failed");
                }
            }
        }

        LiveCaptureLog.Executions(logger, sessions, executions, classes);
        var summary = string.Create(CultureInfo.InvariantCulture, $"{sessions} sessions measured: {executions} executions, {classes} order classes") + (failed > 0 ? $"; {failed} failed" : string.Empty);
        return Result.Success(new LiveExecutionsOutcome(sessions, executions, classes, failed, summary));
    }

    private async Task<(int Executions, int Classes, bool Ran)> SessionAsync(Guid accountId, DateOnly date, CancellationToken cancellationToken)
    {
        var outcomes = await capture.OutcomesOfDateAsync(accountId, date, cancellationToken);
        if (outcomes.Count == 0)
        {
            return (0, 0, false);
        }

        var haveExecution = await store.ExecutionOutcomeIdsAsync(accountId, date, cancellationToken);
        var haveClass = await store.ClassifiedOrderIdsAsync(accountId, date, cancellationToken);
        var needExecution = outcomes.Where(o => EntryOutcomes.Contains(o.Outcome.Outcome) && !haveExecution.Contains(o.Id)).ToList();
        var evidence = outcomes.Any(o => o.Outcome.OrderEvidence == OutcomeCodes.EvidencePresent);
        var orders = evidence ? await capture.LatestOrdersAsync(accountId, date, cancellationToken) : [];
        var unclassifiedOrders = orders.Where(o => !haveClass.Contains(o.BrokerOrderId)).ToList();
        if (needExecution.Count == 0 && unclassifiedOrders.Count == 0)
        {
            return (0, 0, true);
        }

        var approved = (await tickets.ForDateAsync(date, accountId, cancellationToken)).ToDictionary(t => t.ItemId);
        var fills = await capture.FillsAsync(accountId, date, cancellationToken);
        var charges = await capture.ChargesAsync(accountId, date, cancellationToken);
        var links = LiveFillMatcher.Match(date, [.. approved.Values.Select(t => t.Journal)], fills);
        var stored = await grades.ForPlansAsync([.. approved.Values.Select(t => t.PlanId).Distinct()], cancellationToken);
        var cache = new MarketCache(market, date);

        var rows = new List<NewLiveExecution>();
        foreach (var outcome in needExecution)
        {
            if (!approved.TryGetValue(outcome.ItemId, out var ticket))
            {
                continue;
            }

            var entry = links.Where(l => l.Role == JournalRole.Entry && l.ItemId == ticket.ItemId).Select(l => l.Fill).ToList();
            if (entry.Count == 0)
            {
                continue;
            }

            var exits = links.Where(l => l.Role == JournalRole.Exit && l.ItemId == ticket.ItemId).Select(l => l.Fill).ToList();
            var entryOrders = outcome.Outcome.OrderEvidence == OutcomeCodes.EvidencePresent ? orders.Where(o => o.Symbol == ticket.Symbol && o.Side == ticket.Plan.Side && o.FilledQty > 0).ToList() : [];
            rows.Add(LiveExecutionBuilder.Build(
                new LiveExecutionInput(
                    outcome.Id, accountId, ticket.ItemId, date, ticket.Symbol, ticket.Plan.Side, entry, exits, ticket.Plan.PlanEntry, ticket.Plan.PlanStop,
                    stored.GetValueOrDefault(ticket.ItemId)?.Grade.Grade, await ArrivalAsync(entryOrders, cancellationToken), entryOrders.Count > 0 ? entryOrders.Min(o => o.CreateTime) : null,
                    await cache.TickAsync(ticket.Symbol, cancellationToken), await cache.AdvAsync(ticket.Symbol, cancellationToken), charges),
                settings.Edges));
        }

        var classRows = new List<LiveOrderClassRow>();
        var close = new DateTimeOffset(date.ToDateTime(new TimeOnly(15, 30)), IndiaTime.Offset);
        foreach (var order in unclassifiedOrders)
        {
            var match = outcomes.Select(o => approved.GetValueOrDefault(o.ItemId)).FirstOrDefault(t => t is not null && t.Symbol == order.Symbol && t.Plan.Side == order.Side);
            var outcomeId = match is null ? (long?)null : outcomes.First(o => o.ItemId == match.ItemId).Id;
            if (match is null || outcomeId is null)
            {
                continue; // not an entry order of an approved ticket (a stop, an exit, the owner's own trade)
            }

            decimal? low = null, high = null;
            if (order.FilledQty < order.Qty && order.Status != LiveOrderStatus.Rejected && string.Equals(order.OrderType, "LIMIT", StringComparison.OrdinalIgnoreCase))
            {
                (low, high) = await market.MinuteExtremesAsync(order.Symbol, order.CreateTime, order.UpdateTime is { } u && u > order.CreateTime ? u : close, cancellationToken);
            }

            classRows.Add(new LiveOrderClassRow(accountId, order.BrokerOrderId, outcomeId.Value, date, order.Symbol, settings.Edges.Classify(await cache.AdvAsync(order.Symbol, cancellationToken)), order.OrderType.ToUpperInvariant(), order.Qty, order.FilledQty, QueueMissClassifier.ClassOf(order, low, high)));
        }

        var written = rows.Count > 0 ? await store.InsertExecutionsAsync(rows, cancellationToken) : 0;
        var classed = classRows.Count > 0 ? await store.InsertOrderClassesAsync(classRows, cancellationToken) : 0;
        return (written, classed, true);
    }

    /// <summary>The quantity-weighted open of the 1-minute bar each filled entry order was created in; null when there is none or any order has no bar (never a partial average).</summary>
    private async Task<decimal?> ArrivalAsync(List<LiveOrder> entryOrders, CancellationToken cancellationToken)
    {
        if (entryOrders.Count == 0)
        {
            return null;
        }

        decimal weighted = 0;
        long total = 0;
        foreach (var order in entryOrders)
        {
            if (await market.MinuteOpenAsync(order.Symbol, order.CreateTime, cancellationToken) is not { } open)
            {
                return null;
            }

            weighted += open * order.FilledQty;
            total += order.FilledQty;
        }

        return Math.Round(weighted / total, 4, MidpointRounding.AwayFromZero);
    }

    /// <summary>The tick and the turnover of a symbol are read once per session.</summary>
    private sealed class MarketCache(ILiveExecutionMarket market, DateOnly date)
    {
        private readonly Dictionary<string, decimal?> _ticks = [];
        private readonly Dictionary<string, decimal?> _adv = [];

        public async ValueTask<decimal?> TickAsync(string symbol, CancellationToken cancellationToken)
        {
            if (!_ticks.TryGetValue(symbol, out var tick))
            {
                tick = await market.TickAsync(symbol, cancellationToken);
                _ticks[symbol] = tick;
            }

            return tick;
        }

        public async ValueTask<decimal?> AdvAsync(string symbol, CancellationToken cancellationToken)
        {
            if (!_adv.TryGetValue(symbol, out var adv))
            {
                adv = await market.AdvTurnoverCrAsync(symbol, date, cancellationToken);
                _adv[symbol] = adv;
            }

            return adv;
        }
    }
}
