using System.Globalization;
using XDrishti.Domain.Common;

namespace XDrishti.Indicators;

/// <summary>
/// The numbers behind the market context (ADR 0033; they will be trading configuration in the database, ADR 0002). The index trend is <b>up</b> when the close is above its <see cref="TrendSmaPeriod"/>-day average,
/// that average is higher than <see cref="TrendSlopeDays"/> days ago and the index's ADX (<see cref="AdxPeriod"/>) is at least <see cref="AdxMin"/>; <b>down</b> for the mirror image; else <b>range</b>. The
/// volatility score of a day is the mean of the percentile ranks of the VIX and of the index's ATR (<see cref="AtrPeriod"/>) as a percent of its close, each over the last <see cref="PercentileWindow"/> values; it
/// is <b>low</b> below <see cref="LowVolBelow"/>, <b>high</b> from <see cref="HighVolFrom"/> and <b>normal</b> in between (the defaults are exactly one third and two thirds of 0 to 100, the doubles <c>100.0 / 3</c> and
/// <c>200.0 / 3</c>: a score of exactly one third is normal and of exactly two thirds high, whatever the rounding of the two percentiles — the score is computed from integer ranks; a configuration must carry these values,
/// not 33.33 and 66.67). A date more than <see cref="MaxAgeDays"/> calendar days after the last day it would read is not ready (ADR 0033 amendment 14: 7 days is the longest NSE closure plus a missing session). A violation is reported by
/// <see cref="MarketContext.Create"/> with the code <see cref="MarketContext.InvalidOptionsCode"/>.
/// </summary>
public sealed record ContextOptions(
    int TrendSmaPeriod = 50,
    int TrendSlopeDays = 5,
    int AdxPeriod = 14,
    double AdxMin = 20,
    int AtrPeriod = 14,
    int PercentileWindow = 252,
    double LowVolBelow = 100.0 / 3,
    double HighVolFrom = 200.0 / 3,
    int MaxAgeDays = 7)
{
    /// <summary>A validation error naming every offending field, or null when the options can be used.</summary>
    internal Error? Problem()
    {
        var errors = new FieldErrors();
        CheckWhole(errors, nameof(TrendSmaPeriod), TrendSmaPeriod);
        CheckWhole(errors, nameof(TrendSlopeDays), TrendSlopeDays);
        CheckWhole(errors, nameof(AdxPeriod), AdxPeriod);
        CheckWhole(errors, nameof(AtrPeriod), AtrPeriod);
        CheckWhole(errors, nameof(PercentileWindow), PercentileWindow);
        if (MaxAgeDays is < 1 or > 3650)
        {
            errors.Add(Field(nameof(MaxAgeDays)), "Must be between 1 and 3650.");
        }

        CheckPercent(errors, nameof(AdxMin), AdxMin);
        var lowOk = CheckPercent(errors, nameof(LowVolBelow), LowVolBelow);
        var highOk = CheckPercent(errors, nameof(HighVolFrom), HighVolFrom);
        if (lowOk && highOk && LowVolBelow > HighVolFrom)
        {
            errors.Add(Field(nameof(LowVolBelow)), "The low-volatility limit must not be above the high-volatility limit.");
        }

        return errors.HasErrors ? errors.ToError(MarketContext.InvalidOptionsCode, "The market context options are invalid.") : null;
    }

    private static string Field(string name) => "options." + char.ToLowerInvariant(name[0]) + name[1..];

    private static void CheckWhole(FieldErrors errors, string name, int value)
    {
        if (value is < 1 or > IndicatorCatalog.MaxPeriod)
        {
            errors.Add(Field(name), string.Create(CultureInfo.InvariantCulture, $"Must be between 1 and {IndicatorCatalog.MaxPeriod}."));
        }
    }

    private static bool CheckPercent(FieldErrors errors, string name, double value)
    {
        if (double.IsFinite(value) && value is >= 0 and <= 100)
        {
            return true;
        }

        errors.Add(Field(name), "Must be a number between 0 and 100.");
        return false;
    }
}
