using XDrishti.Execution.Application.Ports;

namespace XDrishti.Execution.Application.Recon;

/// <summary>An authoritative fill row: the broker's trade with its exchange trade id, matched to our leg.</summary>
public sealed record FillRow(long IntentId, string ClientOrderId, string ExchangeTradeId, int Qty, decimal Price, DateTimeOffset At);

/// <summary>Writes the authoritative rows of <c>execution.fills</c>. Idempotent by exchange trade id; false when it was already there.</summary>
public interface IFillWriter
{
    Task<bool> RecordAsync(FillRow row, CancellationToken cancellationToken);
}

/// <summary>What the last good poll saw.</summary>
public sealed record PollSnapshot(DateTimeOffset At, IReadOnlyList<BrokerOrderView> Orders, IReadOnlyList<BrokerTradeView> Trades, IReadOnlyList<BrokerPositionView>? Positions);

public enum PollOutcome
{
    Done,
    Throttled,
    Unreadable,
}

/// <summary>
/// The poll is the ground truth (spec P12-06): the order book and the trade book (and the positions while exposed) are read, applied to the legs by the same rank rule as the stream, and the trade book's rows become the authoritative fills. A read that fails leaves everything as it
/// was and does not count as a poll, so <see cref="StreamStatus.StreamsAlive"/> stays false. After a reconnect the same method is the gap fill. Reads go through the read budget; they never use the send limiter and write nothing at the broker.
/// </summary>
public sealed class PollService(IBrokerReads reads, UpdateApplier applier, LegBook legs, StreamStatus status, ReadBudget budget, IFillWriter fills, TimeProvider clock)
{
    private PollSnapshot? _latest;

    public PollSnapshot? Latest => Volatile.Read(ref _latest);

    public async Task<PollOutcome> PollOnceAsync(bool withPositions, CancellationToken cancellationToken)
    {
        if (!budget.TryAdmit(withPositions ? 3 : 2))
        {
            return PollOutcome.Throttled;
        }

        var orders = await reads.GetOrderBookAsync(cancellationToken).ConfigureAwait(false);
        var trades = await reads.GetTradeBookAsync(cancellationToken).ConfigureAwait(false);
        BrokerRead<IReadOnlyList<BrokerPositionView>>? positions = withPositions ? await reads.GetPositionsAsync(cancellationToken).ConfigureAwait(false) : null;
        if (!orders.Readable || !trades.Readable || (positions is { Readable: false }))
        {
            return PollOutcome.Unreadable;
        }

        foreach (var order in orders.Value!)
        {
            applier.ApplyPoll(order);
        }

        foreach (var trade in trades.Value!)
        {
            if (trade.ExchangeTradeId is not { Length: > 0 } id || legs.ByBroker(trade.OrderId) is not { } leg)
            {
                continue;
            }

            await fills.RecordAsync(new FillRow(leg.IntentId, leg.ClientOrderId, id, trade.Qty, trade.Price, clock.GetUtcNow()), cancellationToken).ConfigureAwait(false);
        }

        Volatile.Write(ref _latest, new PollSnapshot(clock.GetUtcNow(), orders.Value!, trades.Value!, positions?.Value));
        status.PollDone();
        return PollOutcome.Done;
    }

    /// <summary>The gap fill after a reconnect: one poll, and only then are the streams alive again.</summary>
    public Task<PollOutcome> GapFillAsync(CancellationToken cancellationToken) => PollOnceAsync(true, cancellationToken);
}

/// <summary>Keeps the rows in memory (tests and the CI rollout step).</summary>
public sealed class InMemoryFillWriter : IFillWriter
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, FillRow> _rows = new(StringComparer.Ordinal);

    public IReadOnlyList<FillRow> Rows
    {
        get
        {
            lock (_gate)
            {
                return [.. _rows.Values];
            }
        }
    }

    public Task<bool> RecordAsync(FillRow row, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_rows.TryAdd(row.ExchangeTradeId, row));
        }
    }
}
