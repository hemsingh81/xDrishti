using System.Globalization;

namespace XDrishti.Domain.Confidence;

/// <summary>
/// The <c>Confidence:CostStress</c> settings of CF-05 (ADR 0206): the cost stress gate (charges and slippage scaled by a multiplier), the fill-one-minute-later test and whether they decide anything. Every number is a proposal of the spec (D1, D3) and a
/// setting, not a constant; <see cref="Problems"/> lists every value out of its range (the host refuses to start on one).
/// </summary>
/// <param name="Mode"><see cref="ConfidenceMode.Off"/> (nothing computed) or <see cref="ConfidenceMode.ReportOnly"/> (the default). <see cref="ConfidenceMode.Gate"/> is not a mode here: enforcing is <paramref name="Enforce"/>.</param>
/// <param name="Enforce">The owner's switch (default false). On, a failed or unavailable stress gate caps the outcome at MARGINAL and never raises it.</param>
/// <param name="MidMultiplier">The middle multiplier of charges and slippage (default 1.5; above 1, below the high one).</param>
/// <param name="MidFloorR">Out-of-sample net expectancy at the middle multiplier must be at least this (default 0.08 R; -1..1).</param>
/// <param name="HighMultiplier">The high multiplier (default 2; up to 5).</param>
/// <param name="HighFloorR">Out-of-sample net expectancy at the high multiplier must be greater than this (default 0 R; -1..1).</param>
/// <param name="DelayMinutes">Minutes the entry fill is delayed in the delay test (default 1; 1..5).</param>
/// <param name="DelayRetention">Expectancy per signal at the delay must be at least this share of the base (default 0.70; 0.1..1).</param>
/// <param name="TierACutCrore">Tier A: the 20-day median traded value is at least this many crore rupees (default 100, CF-05 D2). Tighten-only in both directions: raising it puts names into lower tiers and can only fail more of row 14, and a value below the default is refused at start-up, so configuration cannot loosen the gate (loosening is a change of this file and an ADR).</param>
/// <param name="TierBCutCrore">Tier B: at least this many crore and below the A cut (default 20; at least the default, below the A cut). Below it is tier C. Tighten-only like the A cut.</param>
/// <param name="CapacityFillShare">The most an order may be of the fill minute's traded value (default 0.01, CF-05 D4). Today no source reads the fill minute, so this cap reads unavailable (ADR 0223) and the figure only travels with the settings. Lowering it is stricter; a value above the default is refused.</param>
/// <param name="CapacityDailyShare">The most an order may be of the 20-day median traded value (default 0.005). Lowering it is stricter; a value above the default is refused.</param>
public sealed record CostStressOptions(
    ConfidenceMode Mode = ConfidenceMode.ReportOnly,
    bool Enforce = false,
    double MidMultiplier = 1.5,
    double MidFloorR = 0.08,
    double HighMultiplier = 2,
    double HighFloorR = 0,
    int DelayMinutes = 1,
    double DelayRetention = 0.70,
    decimal TierACutCrore = CostStressOptions.DefaultTierACutCrore,
    decimal TierBCutCrore = CostStressOptions.DefaultTierBCutCrore,
    decimal CapacityFillShare = CostStressOptions.DefaultCapacityFillShare,
    decimal CapacityDailyShare = CostStressOptions.DefaultCapacityDailyShare)
{
    /// <summary>The spec's tier A cut in crore rupees; the least the setting may be.</summary>
    public const decimal DefaultTierACutCrore = 100m;

    /// <summary>The spec's tier B cut in crore rupees; the least the setting may be.</summary>
    public const decimal DefaultTierBCutCrore = 20m;

    /// <summary>The spec's share of the fill minute's traded value; the most the setting may be.</summary>
    public const decimal DefaultCapacityFillShare = 0.01m;

    /// <summary>The spec's share of the 20-day median; the most the setting may be.</summary>
    public const decimal DefaultCapacityDailyShare = 0.005m;

    /// <summary>The tier A cut in rupees.</summary>
    public decimal TierACut => TierACutCrore * Research.LiquidityTiers.Crore;

    /// <summary>The tier B cut in rupees.</summary>
    public decimal TierBCut => TierBCutCrore * Research.LiquidityTiers.Crore;

    /// <summary>The text of the tier and capacity values, kept with the <c>Tiers</c> block of a verdict. It is separate from <see cref="KeyText"/> so the hash stored with every earlier verdict does not change.</summary>
    public string TierKeyText() => string.Create(CultureInfo.InvariantCulture, $"tierA>={TierACutCrore:0.####}cr/tierB>={TierBCutCrore:0.####}cr/fill<={CapacityFillShare:0.######}/median<={CapacityDailyShare:0.######}");

    /// <summary>The lower-case hex SHA-256 of <see cref="TierKeyText"/>.</summary>
    public string TierSnapshotHash() => Research.CanonicalText.Sha256Hex(TierKeyText());

    /// <summary>True when anything is computed.</summary>
    public bool IsOn => Mode != ConfidenceMode.Off;

    /// <summary>The text of the values, kept with every figure so a stored document says which settings produced it.</summary>
    public string KeyText() => string.Create(CultureInfo.InvariantCulture,
        $"{Mode}/enforce={(Enforce ? "on" : "off")}/{MidMultiplier:R}>={MidFloorR:R}/{HighMultiplier:R}>{HighFloorR:R}/delay={DelayMinutes}min>={DelayRetention:R}");

    /// <summary>The snapshot hash recorded on every verdict: the lower-case hex SHA-256 of <see cref="KeyText"/>.</summary>
    public string SnapshotHash() => Research.CanonicalText.Sha256Hex(KeyText());

    /// <summary>The problems of the values, empty when they are usable.</summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        if (!Enum.IsDefined(Mode) || Mode == ConfidenceMode.Gate)
        {
            problems.Add("CostStress:Mode must be Off or ReportOnly (enforcing is CostStress:Enforce)");
        }

        if (!double.IsFinite(MidMultiplier) || MidMultiplier is <= 1 or > 5)
        {
            problems.Add("CostStress:MidMultiplier must be above 1 and at most 5");
        }

        if (!double.IsFinite(HighMultiplier) || HighMultiplier is <= 1 or > 5 || HighMultiplier <= MidMultiplier)
        {
            problems.Add("CostStress:HighMultiplier must be above MidMultiplier and at most 5");
        }

        if (!double.IsFinite(MidFloorR) || MidFloorR is < -1 or > 1)
        {
            problems.Add("CostStress:MidFloorR must be -1 to 1");
        }

        if (!double.IsFinite(HighFloorR) || HighFloorR is < -1 or > 1)
        {
            problems.Add("CostStress:HighFloorR must be -1 to 1");
        }

        if (DelayMinutes is < 1 or > 5)
        {
            problems.Add("CostStress:DelayMinutes must be 1 to 5");
        }

        if (!double.IsFinite(DelayRetention) || DelayRetention is < 0.1 or > 1)
        {
            problems.Add("CostStress:DelayRetention must be 0.1 to 1");
        }

        // tighten-only (true in both directions: a higher cut or a lower share can only fail more orders): the spec's figures are the floor and the ceiling
        if (TierACutCrore is < DefaultTierACutCrore or > 100_000m)
        {
            problems.Add("CostStress:TierACutCrore must be at least 100 and at most 100000 (crore rupees; it can be raised, not lowered)");
        }

        if (TierBCutCrore < DefaultTierBCutCrore || TierBCutCrore >= TierACutCrore)
        {
            problems.Add("CostStress:TierBCutCrore must be at least 20 (crore rupees; it can be raised, not lowered) and below TierACutCrore");
        }

        if (CapacityFillShare is <= 0 or > DefaultCapacityFillShare)
        {
            problems.Add("CostStress:CapacityFillShare must be above 0 and at most 0.01 (it can be lowered, not raised)");
        }

        if (CapacityDailyShare is <= 0 or > DefaultCapacityDailyShare)
        {
            problems.Add("CostStress:CapacityDailyShare must be above 0 and at most 0.005 (it can be lowered, not raised)");
        }

        return problems.AsReadOnly();
    }
}
