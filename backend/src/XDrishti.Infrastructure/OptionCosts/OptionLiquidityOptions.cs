using System.ComponentModel.DataAnnotations;

namespace XDrishti.Infrastructure.OptionCosts;

/// <summary>The settings <c>Options:Liquidity:*</c> (P14-07, ADR 0194): the two rupee thresholds of the liquidity filter. Both starting values are UNVERIFIED and the owner's to change.</summary>
internal sealed class OptionLiquidityOptions
{
    public const string SectionName = "Options:Liquidity";

    /// <summary>Open interest times spot, in crore of rupees, at least.</summary>
    [Range(0.0, 1000.0)]
    public decimal MinOiNotionalCr { get; set; } = 5;

    /// <summary>The entry bar's volume times spot, in lakh of rupees, at least.</summary>
    [Range(0.0, 10000.0)]
    public decimal MinBarTurnoverLakh { get; set; } = 25;
}
