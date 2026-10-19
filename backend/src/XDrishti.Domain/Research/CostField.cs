using XDrishti.Domain.Common;

namespace XDrishti.Domain.Research;

/// <summary>The checks of one number of a charge sheet, written once: not negative, not above its bound, at most 6 decimal places (ADR 0042, D4). Internal; the sheet types and the parser both call it.</summary>
internal static class CostField
{
    /// <summary>The most decimal places a rate may have; more is probably a slipped unit.</summary>
    public const int MaxScale = 6;

    /// <summary>The largest percent line.</summary>
    public const decimal MaxPercent = 5m;

    /// <summary>The largest GST percent.</summary>
    public const decimal MaxGstPercent = 100m;

    /// <summary>The largest brokerage cap or minimum, in rupees.</summary>
    public const decimal MaxBrokerageRupees = 10_000m;

    /// <summary>The largest SEBI fee per crore, in rupees.</summary>
    public const decimal MaxSebiPerCrore = 1_000m;

    /// <summary>The largest DP charge per sale, in rupees.</summary>
    public const decimal MaxDpCharge = 1_000m;

    /// <summary>The number of decimal places the value needs, trailing zeros not counted (<c>0.030</c> has 2).</summary>
    public static int Scale(decimal value)
    {
        var normal = value / 1.0000000000000000000000000000m;
        return (decimal.GetBits(normal)[3] >> 16) & 0xFF;
    }

    /// <summary>Adds the problem of the value, if any, to the list; true when the value is fine.</summary>
    public static bool Check(List<FieldError> errors, string path, decimal value, decimal max)
    {
        if (value < 0)
        {
            errors.Add(new FieldError(path, "must not be negative.", null, CostErrors.Negative));
            return false;
        }

        if (value > max)
        {
            errors.Add(new FieldError(path, "must not be above " + CanonicalText.Number(max) + ".", null, CostErrors.TooLarge));
            return false;
        }

        if (Scale(value) > MaxScale)
        {
            errors.Add(new FieldError(path, "must have at most " + MaxScale + " decimal places.", null, CostErrors.TooPrecise));
            return false;
        }

        return true;
    }

    /// <summary>The value without trailing zeros and with a plain zero, so that two spellings of one number are one value.</summary>
    public static decimal Normal(decimal value) => value == 0 ? 0m : value / 1.0000000000000000000000000000m;
}
