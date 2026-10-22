using XDrishti.Domain.Research;
using XDrishti.Strategies.Model;

namespace XDrishti.Strategies.Canonical;

/// <summary>How the enums of the model are spelt in a strategy file and in the canonical text (doc 11 section 2): <c>BUY</c>, <c>FIXED_RR</c>, <c>swing_low</c>, <c>at_close</c>, <c>intraday</c>, <c>all</c>, <c>time_stop</c>.</summary>
public static class StrategyNames
{
    /// <summary>Styles.</summary>
    public static readonly NameTable<Style> Styles = new(("intraday", Style.Intraday), ("swing", Style.Swing));

    /// <summary>Sides.</summary>
    public static readonly NameTable<Side> Sides = new(("BUY", Side.Buy), ("SELL", Side.Sell));

    /// <summary>List modes.</summary>
    public static readonly NameTable<ListMode> Modes = new(("all", ListMode.All), ("any", ListMode.Any));

    /// <summary>Entry kinds.</summary>
    public static readonly NameTable<EntryKind> Entries = new(("at_close", EntryKind.AtClose), ("stop_above_high", EntryKind.StopAboveHigh), ("limit_pullback", EntryKind.LimitPullback));

    /// <summary>Stop kinds.</summary>
    public static readonly NameTable<StopKind> Stops = new(
        ("fixed_pct", StopKind.FixedPct), ("atr_mult", StopKind.AtrMult), ("swing_low", StopKind.SwingLow), ("swing_high", StopKind.SwingHigh),
        ("bar_low", StopKind.BarLow), ("bar_high", StopKind.BarHigh), ("or_opposite", StopKind.OrOpposite));

    /// <summary>Exit policies.</summary>
    public static readonly NameTable<ExitPolicy> Policies = new(
        ("FIXED_RR", ExitPolicy.FixedRr), ("PARTIAL_BE", ExitPolicy.PartialBe), ("PARTIAL_TRAIL", ExitPolicy.PartialTrail), ("ATR_TRAIL", ExitPolicy.AtrTrail),
        ("STRUCTURE_TRAIL", ExitPolicy.StructureTrail), ("EMA_TRAIL", ExitPolicy.EmaTrail), ("PYRAMID", ExitPolicy.Pyramid));

    /// <summary>Overlay kinds.</summary>
    public static readonly NameTable<OverlayKind> Overlays = new(("time_stop", OverlayKind.TimeStop), ("event_exit", OverlayKind.EventExit));
}
