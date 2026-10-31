namespace XDrishti.Application.Abstractions.Ticks;

/// <summary>
/// The numbers of the trade-tick recorder (<c>Ticks:*</c>, P7-16b, documented in doc 16), checked at start by the host. <see cref="Enabled"/> is the deployment gate; with it false the recorder never reads credentials or opens a socket.
/// <see cref="MinFreeBytes"/> is the free-space floor shared with the depth recorder (<c>Depth:MinFreeGb</c>); the size cap is the recorder's own.
/// </summary>
public sealed record TickSettings(bool Enabled, TimeSpan Flush, long MaxStoredBytes, long MinFreeBytes)
{
    /// <summary>One gibibyte.</summary>
    public const long Gb = 1024L * 1024 * 1024;

    /// <summary>The defaults: OFF.</summary>
    public static TickSettings Off { get; } = new(false, TimeSpan.FromSeconds(5), 12 * Gb, 20 * Gb);
}
