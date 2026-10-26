using XDrishti.Application.Abstractions.Depth;

namespace XDrishti.Application.Depth;

/// <summary>
/// Turns the two packets per instrument (a bid and an ask) into at most one stored snapshot per instrument per sample interval (P7-16 criterion 3) - pure, no clock of its own: the caller gives the receive time and the monotonic milliseconds.
/// A snapshot is made only when a side changed since the last one and both sides were seen. A side older than five seconds sets <see cref="DepthFlags.StaleSide"/>; prices become integer paise (half away from zero); a quantity or an order
/// count above <see cref="int.MaxValue"/> is clamped (<see cref="DepthFlags.QuantityClamped"/>); a best bid at or above the best ask sets <see cref="DepthFlags.Crossed"/>; a ladder out of price order sets <see cref="DepthFlags.UnorderedLadder"/>.
/// <see cref="Offer"/> emits when a packet makes a snapshot due; <see cref="Due"/> collects the ones whose interval ran out without a further packet. The time of a snapshot is the receive time of its newest packet (the packets carry no exchange time).
/// </summary>
internal sealed class DepthSampler(IReadOnlyDictionary<int, Guid> instruments, TimeSpan interval)
{
    /// <summary>A side older than this when the row is made is flagged.</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(5);

    private readonly Dictionary<int, State> _states = [];
    private readonly double _intervalMs = interval.TotalMilliseconds;

    /// <summary>Packets of securities that are not in the set (not an error: counted by the caller).</summary>
    public long Foreign { get; private set; }

    /// <summary>The packet in; the snapshot it made due, if any.</summary>
    public DepthSnapshot? Offer(DepthPacket packet, DateTimeOffset receivedAt, long monoMs)
    {
        ArgumentNullException.ThrowIfNull(packet);
        if (packet.Kind == DepthPacketKind.Disconnect || !instruments.ContainsKey(packet.SecurityId))
        {
            Foreign++;
            return null;
        }

        if (!_states.TryGetValue(packet.SecurityId, out var state))
        {
            state = new State(instruments[packet.SecurityId]);
            _states[packet.SecurityId] = state;
        }

        if (packet.Kind == DepthPacketKind.Bid)
        {
            state.Changed |= state.Bid is null || !Same(state.Bid, packet);
            state.Bid = packet;
            state.BidAt = receivedAt;
            state.BidMono = monoMs;
        }
        else
        {
            state.Changed |= state.Ask is null || !Same(state.Ask, packet);
            state.Ask = packet;
            state.AskAt = receivedAt;
            state.AskMono = monoMs;
        }

        return TryEmit(state, receivedAt, monoMs, fromPacket: true);
    }

    /// <summary>The snapshots that became due with time alone: changed since the last one, both sides seen, the interval over.</summary>
    public IReadOnlyList<DepthSnapshot> Due(DateTimeOffset now, long monoMs)
    {
        List<DepthSnapshot>? due = null;
        foreach (var state in _states.Values)
        {
            if (TryEmit(state, now, monoMs, fromPacket: false) is { } snapshot)
            {
                (due ??= []).Add(snapshot);
            }
        }

        return due ?? [];
    }

    private DepthSnapshot? TryEmit(State state, DateTimeOffset now, long monoMs, bool fromPacket)
    {
        if (!state.Changed || state.Bid is null || state.Ask is null || (state.LastEmitMono is { } last && monoMs - last < _intervalMs))
        {
            return null;
        }

        state.Changed = false;
        state.LastEmitMono = monoMs;
        var newestIsBid = state.BidAt >= state.AskAt;
        var stamp = fromPacket ? now : (newestIsBid ? state.BidAt : state.AskAt);
        var stampMono = fromPacket ? monoMs : (newestIsBid ? state.BidMono : state.AskMono);
        var flags = (short)0;
        var (bidPx, bidQty, bidOrd, bidClamped) = ToPaise(state.Bid);
        var (askPx, askQty, askOrd, askClamped) = ToPaise(state.Ask);
        if (bidClamped || askClamped)
        {
            flags |= DepthFlags.QuantityClamped;
        }

        if (now - state.BidAt > StaleAfter || now - state.AskAt > StaleAfter)
        {
            flags |= DepthFlags.StaleSide;
        }

        if (bidPx[0] > 0 && askPx[0] > 0 && bidPx[0] >= askPx[0])
        {
            flags |= DepthFlags.Crossed;
        }

        if (!Ordered(bidPx, descending: true) || !Ordered(askPx, descending: false))
        {
            flags |= DepthFlags.UnorderedLadder;
        }

        return new DepthSnapshot(stamp, state.InstrumentId, unchecked((int)state.Bid.Sequence), stampMono, flags, bidPx, bidQty, bidOrd, askPx, askQty, askOrd);
    }

    private static bool Same(DepthPacket a, DepthPacket b) =>
        a.Prices.AsSpan().SequenceEqual(b.Prices) && a.Quantities.AsSpan().SequenceEqual(b.Quantities) && a.Orders.AsSpan().SequenceEqual(b.Orders);

    /// <summary>Prices in paise, half away from zero (through decimal, so that 0.285 is 29 and not 28); quantities clamped.</summary>
    private static (int[] Px, int[] Qty, int[] Ord, bool Clamped) ToPaise(DepthPacket packet)
    {
        var px = new int[DepthPacket.Levels];
        var qty = new int[DepthPacket.Levels];
        var ord = new int[DepthPacket.Levels];
        var clamped = false;
        for (var i = 0; i < DepthPacket.Levels; i++)
        {
            px[i] = (int)Math.Round((decimal)packet.Prices[i] * 100m, 0, MidpointRounding.AwayFromZero);
            (qty[i], var q) = Clamp(packet.Quantities[i]);
            (ord[i], var o) = Clamp(packet.Orders[i]);
            clamped |= q || o;
        }

        return (px, qty, ord, clamped);
    }

    private static (int Value, bool Clamped) Clamp(uint value) => value > int.MaxValue ? (int.MaxValue, true) : ((int)value, false);

    /// <summary>The filled levels (price above zero) run in price order; empty levels at the end are fine.</summary>
    private static bool Ordered(int[] prices, bool descending)
    {
        var previous = 0;
        var any = false;
        foreach (var price in prices)
        {
            if (price <= 0)
            {
                continue;
            }

            if (any && (descending ? price > previous : price < previous))
            {
                return false;
            }

            previous = price;
            any = true;
        }

        return true;
    }

    private sealed class State(Guid instrumentId)
    {
        public Guid InstrumentId { get; } = instrumentId;

        public DepthPacket? Bid { get; set; }

        public DepthPacket? Ask { get; set; }

        public DateTimeOffset BidAt { get; set; }

        public DateTimeOffset AskAt { get; set; }

        public long BidMono { get; set; }

        public long AskMono { get; set; }

        public bool Changed { get; set; }

        public long? LastEmitMono { get; set; }
    }
}
