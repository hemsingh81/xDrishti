using XDrishti.Application.Abstractions.Live;

namespace XDrishti.Application.Live;

/// <summary>What became of a packet: a price for the sink, ignored on purpose (the previous close, not trusted yet), or dropped because its instrument is not in the active set.</summary>
internal enum PacketOutcome
{
    Price,
    Ignored,
    UnknownInstrument,
}

/// <summary>A mapped packet: the outcome and, for <see cref="PacketOutcome.Price"/>, the price for the sink.</summary>
internal readonly record struct MappedPacket(PacketOutcome Outcome, LivePrice? Price);

/// <summary>
/// Turns a packet into a <see cref="LivePrice"/> (ADR 0024 §3): the security (segment and id) is looked up in the current active set; a tick of an instrument that is not in it is dropped (and counted by the caller), the previous-close packet is
/// parsed but not used until its meaning has been checked in a real session. A trade time later than the clock by more than <see cref="FutureTolerance"/> is replaced by the clock (a feed whose time stamps run ahead would otherwise keep
/// quotes out of the holdings for hours). Prices before 09:15 are passed through: the sink applies the pre-open rule.
/// </summary>
internal static class FeedPacketMapper
{
    public static readonly TimeSpan FutureTolerance = TimeSpan.FromMinutes(1);

    public static MappedPacket Map(FeedPacket packet, IReadOnlyDictionary<FeedSecurity, Guid> instruments, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(packet);
        ArgumentNullException.ThrowIfNull(instruments);
        if (packet.Kind is not (FeedPacketKind.Ticker or FeedPacketKind.Quote))
        {
            return new MappedPacket(PacketOutcome.Ignored, null);
        }

        if (!instruments.TryGetValue(packet.Security, out var instrumentId))
        {
            return new MappedPacket(PacketOutcome.UnknownInstrument, null);
        }

        if (packet.Price <= 0m || packet.TradeTime is not { } time)
        {
            return new MappedPacket(PacketOutcome.Ignored, null);
        }

        return new MappedPacket(PacketOutcome.Price, new LivePrice(instrumentId, packet.Price, time > now + FutureTolerance ? now : time));
    }
}
