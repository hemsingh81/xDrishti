namespace XDrishti.Domain.Learning.Extensions;

/// <summary>The pre-registered knobs of protocol F (P15-00, P15-10); the defaults are the rule.</summary>
public sealed record ProtocolFOptions
{
    /// <summary>Skipped trades needed pooled (100).</summary>
    public int MinSkipped { get; init; } = 100;

    /// <summary>A trade at or below this R went through the stop (-1.5).</summary>
    public double TailR { get; init; } = -1.5;

    /// <summary>The skipped tail share must be at least this multiple of the taken one (2).</summary>
    public double TailRatio { get; init; } = 2;

    /// <summary>The lower bound of taken minus skipped mean R, before the factor (0.03).</summary>
    public double MinMeanGapR { get; init; } = 0.03;

    /// <summary>The most the filter may remove when ON, skipped plus unavailable, as a share of the trades (0.5).</summary>
    public double MaxRemovedShare { get; init; } = 0.5;

    /// <summary>The share of judged seasons that must agree in direction (2/3).</summary>
    public double SameSignShare { get; init; } = 2.0 / 3.0;

    /// <summary>Bootstrap draws (10,000).</summary>
    public int Draws { get; init; } = 10_000;

    /// <summary>Bootstrap seed (1337).</summary>
    public ulong Seed { get; init; } = 1337;

    /// <summary>The one-sided level (0.90).</summary>
    public double Level { get; init; } = 0.90;

    /// <summary>Skipped trades the hold-out needs (30) before it can confirm anything.</summary>
    public int MinHoldOutSkipped { get; init; } = 30;

    /// <summary>Taken trades the hold-out needs (30) before it can confirm anything.</summary>
    public int MinHoldOutTaken { get; init; } = 30;

    /// <summary>Development seasons that must have both skipped and taken trades before a KEEP is possible (4).</summary>
    public int MinJudgedSeasons { get; init; } = 4;

    /// <summary>The problems with the values, empty when usable.</summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        if (MinSkipped is < 1 or > 10_000_000 || MinHoldOutSkipped is < 1 or > 10_000_000 || MinHoldOutTaken is < 1 or > 10_000_000 || MinJudgedSeasons is < 1 or > 1000)
        {
            problems.Add("min_skipped and min_hold_out_skipped must be 1 or more");
        }

        if (!double.IsFinite(TailR) || TailR >= 0 || !double.IsFinite(TailRatio) || TailRatio < 1)
        {
            problems.Add("tail_r must be negative and tail_ratio at least 1");
        }

        if (!double.IsFinite(MinMeanGapR) || MinMeanGapR < 0 || !double.IsFinite(MaxRemovedShare) || MaxRemovedShare is <= 0 or > 1 || !double.IsFinite(SameSignShare) || SameSignShare is <= 0 or > 1)
        {
            problems.Add("min_mean_gap_r not negative; max_removed_share and same_sign_share above 0 and at most 1");
        }

        if (Draws is < 100 or > 1_000_000 || !double.IsFinite(Level) || Level is <= 0.5 or >= 1)
        {
            problems.Add("draws 100 to 1000000 and level above 0.5 and below 1");
        }

        return problems;
    }

    /// <summary>The options as text, for the recipe hash of the record.</summary>
    public string Canonical() => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"protocol-f/1;min_skipped={MinSkipped};tail={TailR:R},{TailRatio:R};gap={MinMeanGapR:R};removed={MaxRemovedShare:R};sign={SameSignShare:R};boot={Draws},{Seed},{Level:R};holdout={MinHoldOutSkipped},{MinHoldOutTaken};judged={MinJudgedSeasons}");
}
