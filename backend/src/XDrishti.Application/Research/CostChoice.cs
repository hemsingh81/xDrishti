namespace XDrishti.Application.Research;

/// <summary>
/// What a run is charged with (spec P3-03f, F1): gross by name, or a stored charge sheet with a simulated capital, a sizing profile and the largest charge in R a kept trade may have. A closed type: the two cases below are the only ways to build
/// one, a gross choice carries no capital, a sheet choice always has an id, and a request cannot leave the choice out (<see cref="SimulationRequest"/> takes it as a required parameter). The values are checked by the request validation, not here,
/// so that a bad number is a field error of <c>simulation.invalid</c> and not an exception.
/// </summary>
public abstract record CostChoice
{
    /// <summary>The default simulated capital (plan question Q4, taken 2026-10-18): 10,00,000 rupees.</summary>
    public const decimal DefaultCapital = 1_000_000m;

    /// <summary>The default sizing profile name.</summary>
    public const string DefaultProfile = "standard";

    /// <summary>The default <c>selection.max_cost_R</c> (doc 16).</summary>
    public const decimal DefaultMaxCostR = 0.10m;

    private CostChoice()
    {
    }

    /// <summary>The explicit gross choice: no charges, no sizing; labelled <c>costs: none</c>.</summary>
    public static CostChoice Gross { get; } = new GrossChoice();

    /// <summary>A stored charge sheet with the capital, the sizing profile (<c>standard</c> or <c>conservative</c>) and <c>max_cost_R</c>.</summary>
    public static CostChoice Sheet(string sheetId, decimal capital = DefaultCapital, string profile = DefaultProfile, decimal maxCostR = DefaultMaxCostR) =>
        new SheetChoice(sheetId, capital, profile, maxCostR);

    /// <summary>The gross case.</summary>
    public sealed record GrossChoice : CostChoice
    {
        internal GrossChoice()
        {
        }
    }

    /// <summary>The sheet case.</summary>
    public sealed record SheetChoice : CostChoice
    {
        internal SheetChoice(string sheetId, decimal capital, string profile, decimal maxCostR)
        {
            SheetId = sheetId;
            Capital = capital;
            Profile = profile;
            MaxCostR = maxCostR;
        }

        /// <summary>The id of the stored sheet (lower case; the pattern of <c>CostSheet</c>).</summary>
        public string SheetId { get; }

        /// <summary>The simulated capital in rupees.</summary>
        public decimal Capital { get; }

        /// <summary>The sizing profile name.</summary>
        public string Profile { get; }

        /// <summary>The largest charge in R a kept trade has.</summary>
        public decimal MaxCostR { get; }
    }
}
