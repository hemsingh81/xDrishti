using XDrishti.Domain.Options.Quality;

namespace XDrishti.Domain.Tests.Options.Quality;

/// <summary>Hand-made chain points for the checks (paise).</summary>
internal static class ChainBuilder
{
    public static readonly DateOnly Session = new(2026, 10, 12);

    public static readonly DateTimeOffset Slot = new(2026, 10, 12, 9, 20, 0, TimeSpan.FromMinutes(330));

    public static ChainPoint Quote(int strike, bool call, int mid, int half = 5, DateTimeOffset? ts = null, DateOnly? expiry = null, int spot = 10_000, long id = 0, long? oi = null, long? volume = null) =>
        new(ts ?? Slot, expiry ?? Session, strike, call, id == 0 ? (strike * 2L) + (call ? 0 : 1) : id, spot, mid - half, mid + half, oi, volume, null, null, null, null, 0);

    public static ChainPoint Quoted(int strike, bool call, int? bid, int? ask, short flags = 0) =>
        new(Slot, Session, strike, call, strike, 10_000, bid, ask, null, null, null, null, null, null, flags);

    public static ChainPoint Greek(int strike, bool call, double? iv, double? delta, double? gamma = 0.001, double? vega = 1.0, DateTimeOffset? ts = null, DateOnly? expiry = null, short flags = 0, int spot = 10_000) =>
        new(ts ?? Slot, expiry ?? Session, strike, call, strike, spot, null, null, null, null, iv, delta, gamma, vega, flags);
}
