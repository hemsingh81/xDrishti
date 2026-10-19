namespace XDrishti.SimOracle;

/// <summary>The conversions of ADR 0047 R1 and the tick grid in decimal.</summary>
internal static class OracleNumbers
{
    /// <summary>R1: the platform conversion (15 significant digits), then 8 places with the midpoint to even.</summary>
    internal static decimal Conv(double value) => Math.Round((decimal)value, 8, MidpointRounding.ToEven);

    /// <summary>R1: a decimal becomes a stored double by the platform cast; a decimal zero is +0.0.</summary>
    internal static double ToDouble(decimal value) => value == 0m ? 0.0 : (double)value;

    internal static decimal TickDown(decimal x, decimal tick) => Math.Floor(x / tick) * tick;

    internal static decimal TickUp(decimal x, decimal tick) => Math.Ceiling(x / tick) * tick;

    internal static decimal TickNearest(decimal x, decimal tick) => Math.Round(x / tick, MidpointRounding.AwayFromZero) * tick;

    internal static bool OnGrid(decimal x, decimal tick) => x % tick == 0m;
}
