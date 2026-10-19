namespace XDrishti.SimOracle;

/// <summary>One effective-dated schedule: the rates in force from a date, for both segments.</summary>
public sealed record OracleSchedule(
    DateOnly EffectiveFrom,
    decimal GstPct,
    OracleRounding Rounding,
    OracleRates Intraday,
    OracleRates Delivery)
{
    /// <summary>The rates of a segment.</summary>
    public OracleRates RatesOf(OracleCostSegment segment) =>
        segment == OracleCostSegment.Delivery ? Delivery : Intraday;
}
