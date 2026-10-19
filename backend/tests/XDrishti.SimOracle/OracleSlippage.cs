namespace XDrishti.SimOracle;

/// <summary>The slippage numbers: ticks, share of the minute's range, extra ticks of a stop exit.</summary>
public sealed record OracleSlippage(double Ticks, double BarRangeFrac, double StopExitExtraTicks)
{
    /// <summary>No slippage: no fill price is moved or rounded.</summary>
    public static OracleSlippage None { get; } = new(0, 0, 0);

    /// <summary>The configuration defaults of doc 16: 1 tick, 5 % of the range, +2 ticks on stop exits.</summary>
    public static OracleSlippage Default { get; } = new(1, 0.05, 2);

    /// <summary>True when all three numbers are zero.</summary>
    public bool IsNone => Ticks == 0 && BarRangeFrac == 0 && StopExitExtraTicks == 0;
}
