namespace XDrishti.Application.Abstractions.Depth;

/// <summary>The numbers of the depth recorder (<c>Depth:*</c>, P7-16, documented in doc 16), checked at start by the host. <see cref="Enabled"/> is the deployment gate; with it false the recorder never reads credentials or opens a socket.</summary>
public sealed record DepthSettings(bool Enabled, int MaxInstruments, int MaxArmed, IReadOnlyList<string> PinnedSymbols, TimeSpan Sample, TimeSpan Flush, long MaxStoredBytes, long MinFreeBytes)
{
    /// <summary>One gibibyte.</summary>
    public const long Gb = 1024L * 1024 * 1024;

    /// <summary>The defaults: OFF.</summary>
    public static DepthSettings Off { get; } = new(false, 50, 20, [], TimeSpan.FromMilliseconds(1000), TimeSpan.FromSeconds(5), 40 * Gb, 20 * Gb);
}
