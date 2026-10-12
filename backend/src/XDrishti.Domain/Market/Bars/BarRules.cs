namespace XDrishti.Domain.Market.Bars;

/// <summary>The outcome of checking one bar (ADR 0016 §4).</summary>
public enum BarVerdict
{
    /// <summary>Stored as it is.</summary>
    Valid,

    /// <summary>High or low did not contain open and close (the opening bar when the open is the pre-open price): high and low were widened.</summary>
    Repaired,

    NotAWholeMinute,
    NonPositivePrice,
    NegativeVolume,
    NotANumber,
}

/// <summary>The row rules shared by the CSV load and the Dhan fetch: a bar is repaired or rejected, never silently changed or dropped.</summary>
public static class BarRules
{
    /// <summary>Checks the bar; a repaired bar is returned with high = max and low = min over open, high, low and close.</summary>
    public static BarVerdict Check(in Bar bar, out Bar checkedBar)
    {
        checkedBar = bar;
        if (bar.EpochSeconds % 60 != 0)
        {
            return BarVerdict.NotAWholeMinute;
        }

        if (!double.IsFinite(bar.Open) || !double.IsFinite(bar.High) || !double.IsFinite(bar.Low) || !double.IsFinite(bar.Close))
        {
            return BarVerdict.NotANumber;
        }

        if (bar.Open <= 0 || bar.High <= 0 || bar.Low <= 0 || bar.Close <= 0)
        {
            return BarVerdict.NonPositivePrice;
        }

        if (bar.Volume < 0)
        {
            return BarVerdict.NegativeVolume;
        }

        var high = Math.Max(Math.Max(bar.Open, bar.High), Math.Max(bar.Low, bar.Close));
        var low = Math.Min(Math.Min(bar.Open, bar.High), Math.Min(bar.Low, bar.Close));
        if (high == bar.High && low == bar.Low)
        {
            return BarVerdict.Valid;
        }

        checkedBar = bar with { High = high, Low = low };
        return BarVerdict.Repaired;
    }
}
