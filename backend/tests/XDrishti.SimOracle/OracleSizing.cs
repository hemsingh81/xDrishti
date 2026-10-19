namespace XDrishti.SimOracle;

/// <summary>A sizing profile: the risk per trade and the cap per trade, both in percent of capital.</summary>
public sealed record OracleSizing(decimal RiskPct, decimal MaxCapitalPct)
{
    /// <summary>Risk 0.5 %, at most 25 % of capital.</summary>
    public static OracleSizing Standard { get; } = new(0.5m, 25m);

    /// <summary>Risk 0.25 %, at most 25 % of capital.</summary>
    public static OracleSizing Conservative { get; } = new(0.25m, 25m);
}
