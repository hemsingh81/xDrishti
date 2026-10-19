namespace XDrishti.SimOracle;

/// <summary>The adversarial series generator: deterministic from the integer seed, one family per category (1 to 16).</summary>
public static class OracleSeries
{
    /// <summary>Builds the series of a category for a seed. Every minute is well-formed (ADR 0047 R14).</summary>
    public static OracleCase Of(int category, long seed) => SeriesBuilder.Build(category, seed);
}
