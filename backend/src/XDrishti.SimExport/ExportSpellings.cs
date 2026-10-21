using XDrishti.Domain.Market.Actions;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;

namespace XDrishti.SimExport;

/// <summary>
/// The one table of spellings of the enums of a run in the export (spec P3-04b1, E2): the CSV and the JSON both use it, so that a value is spelt one way everywhere. Every switch is exhaustive and throws
/// <see cref="ArgumentOutOfRangeException"/> for a member it does not know, never a default string, so that a member added by a later slice (a new exit reason, a cost basis) fails a test instead of being written as a guess.
/// The spelling is stable text of the file format and never the name of the C# member by accident; the export format is raised when one changes.
/// </summary>
public static class ExportSpellings
{
    private static readonly Dictionary<Type, Delegate> Spellers = new()
    {
        [typeof(Side)] = (Func<Side, string>)Of,
        [typeof(ExitReason)] = (Func<ExitReason, string>)Of,
        [typeof(FillBasis)] = (Func<FillBasis, string>)Of,
        [typeof(CostBasis)] = (Func<CostBasis, string>)Of,
        [typeof(CostSegment)] = (Func<CostSegment, string>)Of,
        [typeof(EntryFill)] = (Func<EntryFill, string>)Of,
        [typeof(LegKind)] = (Func<LegKind, string>)Of,
        [typeof(Timeframe)] = (Func<Timeframe, string>)Of,
        [typeof(SkipReason)] = (Func<SkipReason, string>)Of,
        [typeof(NoTradeReason)] = (Func<NoTradeReason, string>)Of,
        [typeof(UniverseKind)] = (Func<UniverseKind, string>)Of,
        [typeof(SessionExclusion)] = (Func<SessionExclusion, string>)Of,
        [typeof(CorporateActionKind)] = (Func<CorporateActionKind, string>)Of,
    };

    /// <summary>The enum types that have a spelling here: the reflection test requires every enum of the run record to be one of them.</summary>
    public static IReadOnlyCollection<Type> Enums => Spellers.Keys;

    /// <summary><c>BUY</c> · <c>SELL</c>.</summary>
    public static string Of(Side side) => side switch
    {
        Side.Buy => "BUY",
        Side.Sell => "SELL",
        _ => throw Unknown(side),
    };

    /// <summary><c>stop</c> · <c>target</c> · <c>square_off</c> · <c>end_of_data</c> · <c>max_hold</c>.</summary>
    public static string Of(ExitReason reason) => reason switch
    {
        ExitReason.Stop => "stop",
        ExitReason.Target => "target",
        ExitReason.SquareOff => "square_off",
        ExitReason.EndOfData => "end_of_data",
        ExitReason.MaxHold => "max_hold",
        _ => throw Unknown(reason),
    };

    /// <summary><c>modelled</c> for <see cref="FillBasis.Modelled"/>; <c>no_slippage</c> for <see cref="FillBasis.Exact"/> (the export's own name: the basis has no slippage and no tick rounding of a fill).</summary>
    public static string Of(FillBasis basis) => basis switch
    {
        FillBasis.Modelled => "modelled",
        FillBasis.Exact => "no_slippage",
        _ => throw Unknown(basis),
    };

    /// <summary><c>none</c> · <c>charged</c>.</summary>
    public static string Of(CostBasis basis) => basis switch
    {
        CostBasis.None => "none",
        CostBasis.Charged => "charged",
        _ => throw Unknown(basis),
    };

    /// <summary><c>intraday_equity</c> · <c>delivery_equity</c>, the names of the segments in the charge sheet.</summary>
    public static string Of(CostSegment segment) => segment switch
    {
        CostSegment.IntradayEquity => "intraday_equity",
        CostSegment.Delivery => "delivery_equity",
        _ => throw Unknown(segment),
    };

    /// <summary><c>at_open</c> · <c>at_limit</c>.</summary>
    public static string Of(EntryFill fill) => fill switch
    {
        EntryFill.AtOpen => "at_open",
        EntryFill.AtLimit => "at_limit",
        _ => throw Unknown(fill),
    };

    /// <summary><c>entry</c> · <c>add</c> · <c>exit</c>.</summary>
    public static string Of(LegKind kind) => kind switch
    {
        LegKind.Entry => "entry",
        LegKind.Add => "add",
        LegKind.Exit => "exit",
        _ => throw Unknown(kind),
    };

    /// <summary>The code of the timeframe (<c>15m</c>, <c>1D</c>).</summary>
    public static string Of(Timeframe timeframe) => timeframe.ToCode();

    /// <summary>The code of the skip reason (<see cref="SkipReasons.ToCode"/>).</summary>
    public static string Of(SkipReason reason) => reason.ToCode();

    /// <summary><c>no_fill_bar</c> · <c>after_square_off</c> · <c>entry_through_stop</c> · <c>limit_not_reached</c> · <c>position_open</c> · <c>order_pending_at_end</c>.</summary>
    public static string Of(NoTradeReason reason) => reason switch
    {
        NoTradeReason.NoFillBar => "no_fill_bar",
        NoTradeReason.AfterSquareOff => "after_square_off",
        NoTradeReason.EntryThroughStop => "entry_through_stop",
        NoTradeReason.LimitNotReached => "limit_not_reached",
        NoTradeReason.PositionOpen => "position_open",
        NoTradeReason.OrderPendingAtEnd => "order_pending_at_end",
        _ => throw Unknown(reason),
    };

    /// <summary><c>instrument_list</c> · <c>basket</c>.</summary>
    public static string Of(UniverseKind kind) => kind switch
    {
        UniverseKind.InstrumentList => "instrument_list",
        UniverseKind.Basket => "basket",
        _ => throw Unknown(kind),
    };

    /// <summary>The code of the exclusion (<see cref="SessionExclusions.ToCode"/>).</summary>
    public static string Of(SessionExclusion exclusion) => exclusion.ToCode();

    /// <summary>The code of the kind (<see cref="CorporateActionKindCodes.ToCode"/>): <c>split</c> · <c>bonus</c>.</summary>
    public static string Of(CorporateActionKind kind) => kind.ToCode();

    /// <summary>The spelling function of an enum type, resolved once (the JSON writer calls it per value without boxing).</summary>
    /// <exception cref="ArgumentOutOfRangeException">The enum type has no spelling.</exception>
    public static Func<TEnum, string> SpellerFor<TEnum>()
        where TEnum : struct, Enum =>
        Spellers.TryGetValue(typeof(TEnum), out var speller)
            ? (Func<TEnum, string>)speller
            : throw new ArgumentOutOfRangeException(typeof(TEnum).Name, "The enum " + typeof(TEnum).FullName + " has no export spelling: add one to ExportSpellings.");

    private static ArgumentOutOfRangeException Unknown<TEnum>(TEnum value)
        where TEnum : struct, Enum =>
        new(nameof(value), value, "The export has no spelling for this " + typeof(TEnum).Name + " member.");
}
