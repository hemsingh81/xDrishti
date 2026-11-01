namespace XDrishti.Infrastructure.Brokers;

/// <summary>
/// The one throttle of the expired-options endpoint (P14-03): any two requests of the process are at least <c>spacing</c> apart, and never less than <see cref="Floor"/> (two a second at most; the default is one a second). A singleton; the logic is <see cref="SpacingThrottle"/>.
/// </summary>
internal sealed class BackfillThrottle(TimeProvider clock) : SpacingThrottle(clock, Floor)
{
    /// <summary>The lowest gap there is, whatever a caller asks for.</summary>
    public static readonly TimeSpan Floor = TimeSpan.FromMilliseconds(500);
}
