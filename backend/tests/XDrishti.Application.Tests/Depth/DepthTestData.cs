using XDrishti.Application.Abstractions.Depth;

namespace XDrishti.Application.Tests.Depth;

/// <summary>Constructed depth packets (the shapes are written from Dhan's documentation and are UNVERIFIED until the first real session).</summary>
internal static class DepthTestData
{
    public static readonly Guid Reliance = Guid.Parse("00000000-0000-0000-0000-000000000001");

    public static readonly Guid Tcs = Guid.Parse("00000000-0000-0000-0000-000000000002");

    public const int RelianceId = 2885;

    public const int TcsId = 11536;

    /// <summary>A side with <paramref name="levels"/> filled levels starting at <paramref name="best"/> and stepping by <paramref name="step"/> (negative for bids).</summary>
    public static DepthPacket Side(DepthPacketKind kind, int securityId, double best, double step, int levels = 20, uint quantity = 100, uint orders = 3, uint sequence = 7)
    {
        var prices = new double[DepthPacket.Levels];
        var quantities = new uint[DepthPacket.Levels];
        var counts = new uint[DepthPacket.Levels];
        for (var i = 0; i < levels; i++)
        {
            prices[i] = best + (step * i);
            quantities[i] = quantity + (uint)i;
            counts[i] = orders;
        }

        return new DepthPacket(kind, securityId, sequence, prices, quantities, counts);
    }

    public static DepthPacket Bid(int securityId = RelianceId, double best = 100.00, uint quantity = 100, uint sequence = 7, int levels = 20) =>
        Side(DepthPacketKind.Bid, securityId, best, -0.05, levels, quantity, sequence: sequence);

    public static DepthPacket Ask(int securityId = RelianceId, double best = 100.05, uint quantity = 100, uint sequence = 8, int levels = 20) =>
        Side(DepthPacketKind.Ask, securityId, best, 0.05, levels, quantity, sequence: sequence);

    public static IReadOnlyDictionary<int, Guid> Set() => new Dictionary<int, Guid> { [RelianceId] = Reliance, [TcsId] = Tcs };
}
