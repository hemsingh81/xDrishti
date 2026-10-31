using XDrishti.Application.Abstractions.Live;
using XDrishti.Application.Abstractions.Ticks;
using XDrishti.Application.Depth;

namespace XDrishti.Application.Ticks;

/// <summary>
/// Turns the quote packets of the day's set into stored trade-tick rows (P7-16b criteria 3 and 4) - pure, no clock of its own: the caller gives the receive time, the monotonic milliseconds and the packet counter.
/// <list type="bullet">
/// <item>Only quote packets of the set, received in 09:15:00 to 15:30:00 India time (<see cref="DepthWindow"/>), with a cumulative volume and a price that fits a paise column, are usable; the rest is counted and ignored.</item>
/// <item>A row is written <b>only when the cumulative volume rose</b> since the last stored row of the instrument in this run; an unchanged volume is counted (<see cref="Unchanged"/>), not written.</item>
/// <item>The first usable packet of an instrument in a run and after <see cref="Rebase"/> (a reconnect) is written with <see cref="TickFlags.Baseline"/>, whatever its volume.</item>
/// <item>A volume below the last stored one is written with <see cref="TickFlags.VolumeDown"/>; a trade time earlier than the last stored one sets <see cref="TickFlags.LttBack"/>; a last quantity of 65,535 sets <see cref="TickFlags.LtqCapped"/>.</item>
/// <item>Prices become integer paise, half away from zero (float32 resolves a paisa only below 167,772.16 rupees; above it a few paise of error are accepted, ADR).</item>
/// </list>
/// </summary>
internal sealed class TickSampler(IReadOnlyDictionary<int, Guid> instruments)
{
    /// <summary>The largest quantity a uint16 carries.</summary>
    public const int LtqCap = ushort.MaxValue;

    private readonly Dictionary<int, State> _states = [];

    /// <summary>Quote packets whose cumulative volume did not move (not written).</summary>
    public long Unchanged { get; private set; }

    /// <summary>Packets that are not quotes (ticker, previous close, anything else) or not of the set: counted and ignored.</summary>
    public long Ignored { get; private set; }

    /// <summary>Quote packets outside the stored part of the day or without a usable volume or price.</summary>
    public long Unusable { get; private set; }

    /// <summary>Makes the next usable packet of every instrument a baseline (after a reconnect: the volume traded in the hole shows as one jump).</summary>
    public void Rebase()
    {
        foreach (var state in _states.Values)
        {
            state.Baseline = true;
        }
    }

    /// <summary>The packet in; the row to store, if any.</summary>
    public TradeTick? Offer(FeedPacket packet, DateTimeOffset receivedAt, long monoMs, long rxSeq)
    {
        ArgumentNullException.ThrowIfNull(packet);
        if (packet.Kind != FeedPacketKind.Quote || packet.Security.Segment != FeedSegments.NseEquity || !instruments.TryGetValue(packet.Security.SecurityId, out var instrumentId))
        {
            Ignored++;
            return null;
        }

        if (!DepthWindow.Contains(receivedAt) || packet.Volume is not { } volume || volume <= 0 || packet.TradeTime is not { } tradeTime || !TryPaise(packet.Price, out var paise))
        {
            Unusable++;
            return null;
        }

        if (!_states.TryGetValue(packet.Security.SecurityId, out var state))
        {
            state = new State();
            _states[packet.Security.SecurityId] = state;
        }

        var ltt = (int)Math.Clamp(tradeTime.ToUnixTimeSeconds(), int.MinValue, int.MaxValue);
        short flags = 0;
        if (state.Baseline)
        {
            flags |= TickFlags.Baseline;
        }
        else if (volume == state.LastVolume)
        {
            Unchanged++;
            return null;
        }
        else if (volume < state.LastVolume)
        {
            flags |= TickFlags.VolumeDown;
        }

        if (state.LastLtt is { } lastLtt && ltt < lastLtt)
        {
            flags |= TickFlags.LttBack;
        }

        var ltq = packet.LastQuantity ?? 0;
        if (ltq >= LtqCap)
        {
            flags |= TickFlags.LtqCapped;
        }

        state.Baseline = false;
        state.LastVolume = volume;
        state.LastLtt = ltt;
        return new TradeTick(receivedAt, instrumentId, monoMs, rxSeq, ltt, paise, ltq, volume, flags);
    }

    /// <summary>Paise, half away from zero; false when the price is not positive or does not fit the column.</summary>
    internal static bool TryPaise(decimal price, out int paise)
    {
        paise = 0;
        var value = Math.Round(price * 100m, 0, MidpointRounding.AwayFromZero);
        if (value <= 0m || value > int.MaxValue)
        {
            return false;
        }

        paise = (int)value;
        return true;
    }

    private sealed class State
    {
        public bool Baseline { get; set; } = true;

        public long LastVolume { get; set; }

        public int? LastLtt { get; set; }
    }
}
