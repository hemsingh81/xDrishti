using XDrishti.Domain.Common;

namespace XDrishti.Domain.Portfolio;

/// <summary>
/// The thresholds of the Action-center rules (ADR 0022, doc 15 §3): percentages are percent numbers (10 = 10 %). The defaults are the ones of the design; the owner edits them in Settings within the fixed ranges below.
/// </summary>
public sealed record ActionThresholds(
    decimal BelowCostPct,
    decimal BelowCostHighPct,
    decimal GainPct,
    decimal OverweightFloorPct,
    decimal OverweightMultiple,
    int MinPositions,
    decimal ConcentrationPct,
    decimal LagPp,
    int LagDays,
    int LongTermWindowDays)
{
    public const int MaxDecimals = 2;

    public static ActionThresholds Defaults { get; } = new(10m, 20m, 25m, 15m, 1.5m, 3, 35m, 10m, 180, 30);

    /// <summary>Checks every value against its range (and that the high band is above the below-cost one); the problems are added to <paramref name="errors"/> under the field's name (camel case).</summary>
    public void Validate(FieldErrors errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        Range(errors, nameof(BelowCostPct), BelowCostPct, 1m, 90m, "%");
        Range(errors, nameof(BelowCostHighPct), BelowCostHighPct, 2m, 95m, "%");
        if (BelowCostHighPct <= BelowCostPct)
        {
            errors.Add(Name(nameof(BelowCostHighPct)), "Must be greater than the below-cost threshold.");
        }

        Range(errors, nameof(GainPct), GainPct, 1m, 1000m, "%");
        Range(errors, nameof(OverweightFloorPct), OverweightFloorPct, 1m, 100m, "%");
        Range(errors, nameof(OverweightMultiple), OverweightMultiple, 1m, 10m, string.Empty);
        Range(errors, nameof(MinPositions), MinPositions, 2m, 50m, string.Empty);
        Range(errors, nameof(ConcentrationPct), ConcentrationPct, 5m, 100m, "%");
        Range(errors, nameof(LagPp), LagPp, 1m, 100m, " percentage points");
        Range(errors, nameof(LagDays), LagDays, 30m, 3650m, " days");
        Range(errors, nameof(LongTermWindowDays), LongTermWindowDays, 1m, 365m, " days");
    }

    private static void Range(FieldErrors errors, string field, decimal value, decimal min, decimal max, string unit)
    {
        if (value < min || value > max)
        {
            errors.Add(Name(field), $"Use {min.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)} to {max.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}{unit}.");
        }
        else if (decimal.Round(value, MaxDecimals) != value)
        {
            errors.Add(Name(field), $"Use at most {MaxDecimals} decimals.");
        }
    }

    private static string Name(string property) => char.ToLowerInvariant(property[0]) + property[1..];
}
