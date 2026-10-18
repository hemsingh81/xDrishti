using XDrishti.Domain.Common;

namespace XDrishti.Domain.Research;

/// <summary>
/// Everything costing a trade needs besides the trade (ADR 0042, D2 and D13): the sheet, the sizing profile, the simulated capital (fixed: every trade is sized from the same capital) and the largest charge in R a trade may have
/// (doc 08 §6, <c>selection.max_cost_R</c>). Only <see cref="Create"/> builds one.
/// </summary>
public sealed record CostContext
{
    /// <summary>The largest capital, in rupees.</summary>
    public const decimal MaxCapital = 10_000_000_000m;

    /// <summary>The largest <c>max_cost_R</c>.</summary>
    public const decimal MaxCostRLimit = 100m;

    private CostContext(CostSheet sheet, SizingProfile profile, decimal capital, decimal maxCostR)
    {
        Sheet = sheet;
        Profile = profile;
        Capital = capital;
        MaxCostR = maxCostR;
    }

    /// <summary>The charge sheet.</summary>
    public CostSheet Sheet { get; }

    /// <summary>The sizing profile.</summary>
    public SizingProfile Profile { get; }

    /// <summary>The simulated capital in rupees, above 0 and at most 10,000,000,000.</summary>
    public decimal Capital { get; }

    /// <summary>A trade whose charge in R is above this is skipped (above 0, at most 100).</summary>
    public decimal MaxCostR { get; }

    /// <summary>Builds the context.</summary>
    /// <exception cref="ArgumentNullException">The sheet or the profile is null.</exception>
    /// <returns>The context, or <c>costs.sizing_input_invalid</c> for a capital outside (0, 10,000,000,000] or a <c>MaxCostR</c> outside (0, 100].</returns>
    public static Result<CostContext> Create(CostSheet sheet, SizingProfile profile, decimal capital, decimal maxCostR)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(profile);
        if (!(capital > 0 && capital <= MaxCapital))
        {
            return Result.Failure<CostContext>(CostErrors.SizingInputInvalid("capital"));
        }

        if (!(maxCostR > 0 && maxCostR <= MaxCostRLimit))
        {
            return Result.Failure<CostContext>(CostErrors.SizingInputInvalid("max cost in R"));
        }

        return Result.Success(new CostContext(sheet, profile, CostField.Normal(capital), CostField.Normal(maxCostR)));
    }
}
