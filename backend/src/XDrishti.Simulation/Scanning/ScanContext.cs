using XDrishti.Indicators;

namespace XDrishti.Simulation.Scanning;

/// <summary>
/// The daily market context a scan reads (P3-lift, ADR 0145), opaque outside the scanning stage: the running and replay stages hand it to <see cref="EntryScanner"/> without naming the indicator library. Made only by <see cref="ContextReader"/>.
/// </summary>
public sealed class ScanContext
{
    internal ScanContext(MarketContext context)
    {
        Context = context;
    }

    internal MarketContext Context { get; }
}
