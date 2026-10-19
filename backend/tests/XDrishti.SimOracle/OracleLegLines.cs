namespace XDrishti.SimOracle;

/// <summary>The eight lines of one leg.</summary>
public sealed record OracleLegLines(
    decimal Brokerage,
    decimal Stt,
    decimal Exchange,
    decimal Sebi,
    decimal Stamp,
    decimal Other,
    decimal Dp,
    decimal Gst)
{
    /// <summary>The sum of the eight lines.</summary>
    public decimal Total => Brokerage + Stt + Exchange + Sebi + Stamp + Other + Dp + Gst;
}
