namespace XDrishti.SimOracle;

/// <summary>The rounding mode of a charge sheet (ADR 0042 D6): each line to the paisa, once.</summary>
public enum OracleRounding
{
    HalfAwayFromZero,
    HalfEven,
    Down,
}
