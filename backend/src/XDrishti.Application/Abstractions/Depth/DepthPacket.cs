namespace XDrishti.Application.Abstractions.Depth;

/// <summary>What a packet of Dhan's 20-level depth feed is: one side of a book, or the server's disconnect notice.</summary>
public enum DepthPacketKind
{
    /// <summary>The bid side (response code 41).</summary>
    Bid,

    /// <summary>The ask side (response code 51).</summary>
    Ask,

    /// <summary>The server is disconnecting, with a reason code (response code 50).</summary>
    Disconnect,
}

/// <summary>
/// One parsed packet of the depth feed (P7-16): the 20 levels of one side as Dhan sent them (price, quantity, orders), the security id and the packet's sequence number (its meaning is UNVERIFIED). <see cref="Reason"/> belongs to
/// <see cref="DepthPacketKind.Disconnect"/>. The arrays always hold exactly <see cref="Levels"/> entries for a book side and are empty for a disconnect.
/// </summary>
public sealed record DepthPacket(DepthPacketKind Kind, int SecurityId, uint Sequence, double[] Prices, uint[] Quantities, uint[] Orders, int Reason = 0)
{
    /// <summary>The levels of a side.</summary>
    public const int Levels = 20;

    /// <summary>The disconnect notice of a server.</summary>
    public static DepthPacket Disconnect(int reason) => new(DepthPacketKind.Disconnect, 0, 0, [], [], [], reason);
}
