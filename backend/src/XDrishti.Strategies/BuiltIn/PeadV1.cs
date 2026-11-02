using System.Globalization;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Strategies.Model;

namespace XDrishti.Strategies.BuiltIn;

/// <summary>
/// Post-earnings-announcement drift, version 1 (spec P15-09; protocol P of P15-10). A research strategy, not a trading rule: nothing here is evidence of an edge. Id <c>PEAD</c>. 1D only, BUY only (the short side is a research label: a swing SELL needs futures and the simulator refuses it).
/// On the reaction session of a results filing (<c>days_since_results == 0</c>) the strategy enters when the surprise measure (<c>sue</c> = <c>results_sue_q</c>, <c>car3</c> = <c>results_car3_q</c>) falls in the buckets: <c>top</c> is quintile 5, <c>middle</c> is quintiles 2 to 4. The decision is the close of the
/// reaction session, so by ADR 0043 the order fills at the open of the next session and the announcement jump is never traded. The stop is 2 × the daily ATR(14); the exit is a <c>FIXED_RR</c> of 10 (a practical "no target": 20 ATRs in at most 20 sessions; the exits by target are counted and a run
/// whose share of them is above 2 % of the trades is labelled "target not negligible"); the position leaves at the close of its <c>hold</c>-th session at the latest (5 or 20, an event side may hold up to 20). The quintiles come from the breakpoints of the previous complete season (point in time),
/// so the strategy never ranks against a season that has not finished reporting. It does not name <c>not_event_day</c>: under the fail-closed rule that filter would block nearly every entry, right after a results event the next meeting being rarely intimated.
/// <para>
/// The four counted variants of <c>(measure, hold)</c> with <c>buckets = top</c> and the control (<c>buckets = middle</c>) are the same definition but for the buckets. Pure: builds a new definition on every call.
/// </para>
/// </summary>
public static class PeadV1
{
    /// <summary>The id of the strategy.</summary>
    public const string Id = "PEAD";

    /// <summary>The parameter that chooses the surprise measure: <see cref="MeasureSue"/> or <see cref="MeasureCar3"/>.</summary>
    public const string MeasureParam = "measure";

    /// <summary>The parameter that is the hold in sessions: 5 or 20.</summary>
    public const string HoldParam = "hold";

    /// <summary>The parameter that chooses the buckets: <see cref="BucketsTop"/> or <see cref="BucketsMiddle"/>.</summary>
    public const string BucketsParam = "buckets";

    /// <summary><c>measure</c> = 1: <c>sue_yoy</c> (the standardised unexpected earnings).</summary>
    public const double MeasureSue = 1;

    /// <summary><c>measure</c> = 2: <c>results_car3</c> (the three-session abnormal return).</summary>
    public const double MeasureCar3 = 2;

    /// <summary><c>buckets</c> = 1: the top quintile.</summary>
    public const double BucketsTop = 1;

    /// <summary><c>buckets</c> = 2: the middle three quintiles (the control).</summary>
    public const double BucketsMiddle = 2;

    /// <summary>The stop in ATRs.</summary>
    public const double StopAtr = 2;

    /// <summary>The target as a multiple of the risk, a practical "no target".</summary>
    public const double RewardRisk = 10;

    /// <summary>The ATR period of the stop.</summary>
    public const int AtrPeriod = 14;

    /// <summary>The holds of the two counted horizons.</summary>
    public static IReadOnlyList<double> Holds { get; } = [5, 20];

    /// <summary>Builds the definition for the measure (1 or 2), the hold (5 or 20) and the buckets (1 or 2); the caller (the registry) has checked the values.</summary>
    public static StrategyDefinition Create(double measure = MeasureSue, double hold = 20, double buckets = BucketsTop)
    {
        var feature = measure == MeasureCar3 ? "results_car3_q" : "results_sue_q";
        var (low, high) = buckets == BucketsMiddle ? (2.0, 4.0) : (5.0, 5.0);
        return new StrategyDefinition(
            StrategyDefinition.CurrentSchema,
            Id,
            Style.Swing,
            [Timeframe.D1],
            [Side.Buy],
            [new StrategyParam("q_low", low), new StrategyParam("q_high", high), new StrategyParam("stop_atr", StopAtr), new StrategyParam("rr", RewardRisk)],
            new SideRules(
                null,
                new ConditionList(ListMode.All, ["days_since_results == 0", feature + " >= q_low", feature + " <= q_high"]),
                null,
                new EntrySpec(EntryKind.AtClose),
                new StopSpec(StopKind.AtrMult, Mult: Quantity.Named("stop_atr"), AtrPeriod: Quantity.Of(AtrPeriod)),
                [new ExitSpec(ExitPolicy.FixedRr, [new ExitParameter("rr", [Quantity.Named("rr")])])],
                null),
            null,
            new TimeRules(null, null, null, (int)hold),
            []);
    }

    /// <summary>The variant label protocol P uses for a measure and a hold (<c>sue_yoy/5</c>), or null for a value that is not one of the four.</summary>
    public static string? VariantOf(double measure, double hold) =>
        (measure == MeasureSue ? "sue_yoy" : measure == MeasureCar3 ? "results_car3" : null) is { } name && Holds.Contains(hold) ? name + "/" + hold.ToString("0", CultureInfo.InvariantCulture) : null;
}
