namespace XDrishti.Infrastructure.Brokers;

/// <summary>
/// The one throttle of the option-chain endpoints (P14-02): any two requests of the process - an expiry list or a chain, of any underlying - are at least <c>spacing</c> apart, and never less than <see cref="Floor"/> (Dhan: one unique request every 3
/// seconds; whether that is per user is UNVERIFIED, so the strictest reading is kept). A singleton, so two callers still keep the gap; the logic is <see cref="SpacingThrottle"/>.
/// </summary>
internal sealed class ChainThrottle(TimeProvider clock) : SpacingThrottle(clock, Floor)
{
    /// <summary>The lowest gap there is, whatever a caller asks for.</summary>
    public static readonly TimeSpan Floor = TimeSpan.FromSeconds(3);
}
