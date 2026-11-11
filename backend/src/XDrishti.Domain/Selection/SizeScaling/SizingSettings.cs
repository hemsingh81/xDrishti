namespace XDrishti.Domain.Selection.SizeScaling;

/// <summary>
/// The five owner settings of the size scaling (spec BK-10). Every one is OFF or neutral by default; nothing but an owner act with a fresh password changes them (<see cref="SizingChanges"/>). There is no setting that scales up.
/// </summary>
/// <param name="VolTargetEnabled"><c>sizing.vol_target.enabled</c>, default false.</param>
/// <param name="Floor"><c>sizing.vol_target.floor</c>, default 0.5, 0.25 to 1.0.</param>
/// <param name="CapacityEnabled"><c>sizing.capacity.enabled</c>, default false.</param>
/// <param name="CapacityPct"><c>sizing.capacity.pct</c>, default 0.005, above 0 and at most the configured capacity share (0.005): tighten only.</param>
/// <param name="MinFit"><c>sizing.budget.min_fit</c>, default 0.5, 0.25 to 1.0: the lowest share of its quantity a candidate may be cut to by a basket budget.</param>
public sealed record SizingSettings(bool VolTargetEnabled, double Floor, bool CapacityEnabled, double CapacityPct, double MinFit)
{
    /// <summary>The key of <see cref="VolTargetEnabled"/>.</summary>
    public const string VolTargetEnabledKey = "sizing.vol_target.enabled";

    /// <summary>The key of <see cref="Floor"/>.</summary>
    public const string FloorKey = "sizing.vol_target.floor";

    /// <summary>The key of <see cref="CapacityEnabled"/>.</summary>
    public const string CapacityEnabledKey = "sizing.capacity.enabled";

    /// <summary>The key of <see cref="CapacityPct"/>.</summary>
    public const string CapacityPctKey = "sizing.capacity.pct";

    /// <summary>The key of <see cref="MinFit"/>.</summary>
    public const string MinFitKey = "sizing.budget.min_fit";

    /// <summary>With no stored change: everything OFF, the neutral numbers.</summary>
    public static SizingSettings Defaults { get; } = new(false, 0.5, false, 0.005, 0.5);

    /// <summary>The numbers the pure kernels use.</summary>
    public SizingOptions Options => new(Floor: Floor, CapacityPct: CapacityPct, MinFit: MinFit);

    /// <summary>The value of a key as stored (invariant text), or null for a key that is not one of the four.</summary>
    public string? ValueOf(string key) => key switch
    {
        VolTargetEnabledKey => SizingChanges.Text(VolTargetEnabled),
        FloorKey => SizingChanges.Text(Floor),
        CapacityEnabledKey => SizingChanges.Text(CapacityEnabled),
        CapacityPctKey => SizingChanges.Text(CapacityPct),
        MinFitKey => SizingChanges.Text(MinFit),
        _ => null,
    };

    /// <summary>The settings with one key set from its stored text; unchanged for an unknown key or text that does not parse.</summary>
    public SizingSettings With(string key, string value) => key switch
    {
        VolTargetEnabledKey when bool.TryParse(value, out var b) => this with { VolTargetEnabled = b },
        CapacityEnabledKey when bool.TryParse(value, out var b) => this with { CapacityEnabled = b },
        FloorKey when double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) => this with { Floor = d },
        CapacityPctKey when double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) => this with { CapacityPct = d },
        MinFitKey when double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) => this with { MinFit = d },
        _ => this,
    };
}
