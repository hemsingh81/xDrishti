namespace XDrishti.Application.Abstractions.OpeningBars;

/// <summary>
/// The numbers of the opening-minute recorder (<c>OpeningBars:*</c>, BK-03b). <see cref="Enabled"/> is the deployment gate, false by default: with it false nothing reads a credential, opens a socket or writes a row. <see cref="RecordUntil"/> is the IST time the
/// recorder closes; <see cref="Grace"/> is how long after a minute ends its bar is written when no later packet comes; <see cref="FormBy"/> is how long after a formation time a basket may still be formed (later: not formed, <c>late</c>);
/// <see cref="Silence"/> is the connection-wide quiet that counts as a feed gap; <see cref="MinFreeBytes"/> is the shared free-disk floor.
/// </summary>
public sealed record OpeningBarsSettings(bool Enabled, int MaxInstruments, TimeOnly RecordUntil, TimeSpan Grace, TimeSpan FormBy, int KeepDays, TimeSpan Silence, long MinFreeBytes)
{
    /// <summary>The first minute that is recorded (the market open, IST).</summary>
    public static readonly TimeOnly Open = new(9, 15);

    /// <summary>Everything off: what a host that does not run the recorder sees.</summary>
    public static readonly OpeningBarsSettings Off = new(false, 500, new TimeOnly(9, 31), TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(2), 60, TimeSpan.FromSeconds(30), 0);
}
