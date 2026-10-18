using XDrishti.Domain.Common;

namespace XDrishti.Domain.Research;

/// <summary>
/// The two numbers a simulated trade is sized by (ADR 0042, D10): the share of the simulated capital put at risk in one trade and the share of it that one position may cost. In P3 the values are code defaults that equal the seeded rows of
/// <c>accounts.risk_profiles</c> (a test compares them) and a run stores the numbers it used. The entries per day, open positions, heat and loss limits are portfolio limits, not simulated. Only <see cref="Create"/> and the two named profiles build one.
/// </summary>
public sealed record SizingProfile
{
    /// <summary>The largest risk per trade (the bound of the risk limits of doc 08 §7), in percent.</summary>
    public const decimal MaxRiskPerTradePct = 2m;

    /// <summary>The largest share of capital per trade, in percent.</summary>
    public const decimal MaxCapitalPerTradeLimitPct = 100m;

    private SizingProfile(string name, decimal riskPerTradePct, decimal maxCapitalPerTradePct)
    {
        Name = name;
        RiskPerTradePct = riskPerTradePct;
        MaxCapitalPerTradePct = maxCapitalPerTradePct;
    }

    /// <summary>The profile's name, as in <c>accounts.risk_profiles</c> but lower case; the pattern of a sheet id (it is written into the run key and the labels).</summary>
    public string Name { get; }

    /// <summary>The share of capital put at risk in one trade, in percent (above 0, at most 2).</summary>
    public decimal RiskPerTradePct { get; }

    /// <summary>The largest share of capital one position may cost at its entry, in percent (above 0, at most 100).</summary>
    public decimal MaxCapitalPerTradePct { get; }

    /// <summary>Doc 16 <c>risk_profiles.standard</c>: risk 0.5 %, at most 25 % of capital per trade.</summary>
    public static SizingProfile Standard { get; } = new("standard", 0.5m, 25m);

    /// <summary>The seeded <c>Conservative</c> row: risk 0.25 %, at most 25 % of capital per trade.</summary>
    public static SizingProfile Conservative { get; } = new("conservative", 0.25m, 25m);

    /// <summary>Builds a profile; a name or a number out of range is <c>costs.sizing_input_invalid</c> with the field named.</summary>
    public static Result<SizingProfile> Create(string name, decimal riskPerTradePct, decimal maxCapitalPerTradePct)
    {
        if (name is null || !CostSheet.IsValidId(name))
        {
            return Result.Failure<SizingProfile>(CostErrors.SizingInputInvalid("profile name"));
        }

        if (!(riskPerTradePct > 0 && riskPerTradePct <= MaxRiskPerTradePct))
        {
            return Result.Failure<SizingProfile>(CostErrors.SizingInputInvalid("risk per trade"));
        }

        if (!(maxCapitalPerTradePct > 0 && maxCapitalPerTradePct <= MaxCapitalPerTradeLimitPct))
        {
            return Result.Failure<SizingProfile>(CostErrors.SizingInputInvalid("largest capital per trade"));
        }

        return Result.Success(new SizingProfile(name, CostField.Normal(riskPerTradePct), CostField.Normal(maxCapitalPerTradePct)));
    }
}
