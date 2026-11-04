using System.Globalization;

namespace XDrishti.Domain.Lab;

/// <summary>
/// The hold-out pass rule (spec CF-06, D5 and review fix 4): expectancy at least <c>retention</c> (50 %) of the development out-of-sample expectancy, above 0, with at least <c>minTrades</c> (30) hold-out trades. Otherwise <c>inconclusive</c> (few trades or no
/// positive development figure) or <c>fail</c>. A value that is not finite fails; nothing unknown passes.
/// </summary>
public static class HoldOutRule
{
    /// <summary>Judges one look. <paramref name="countsTowardPass"/> is false for a re-used window (or a run that had already seen the window): the numbers stay, the pass flag cannot be true.</summary>
    public static HoldOutResult Evaluate(double developmentExpectancyR, double holdOutExpectancyR, int holdOutTrades, int minTrades, double retention, bool countsTowardPass = true, string? notCountedReason = null)
    {
        var inner = Judge(developmentExpectancyR, holdOutExpectancyR, holdOutTrades, minTrades, retention);
        return countsTowardPass || !inner.Passed ? inner : inner with { Passed = false, Reason = (notCountedReason ?? "the window was already seen") + "; the numbers meet the rule but cannot count toward a pass: " + inner.Reason };
    }

    private static HoldOutResult Judge(double dev, double hold, int trades, int minTrades, double retention)
    {
        if (!double.IsFinite(hold))
        {
            return new HoldOutResult(HoldOutStatus.Fail, false, null, "the hold-out expectancy is not a finite number");
        }

        if (trades < minTrades)
        {
            return new HoldOutResult(HoldOutStatus.Inconclusive, false, null, $"{trades.ToString(CultureInfo.InvariantCulture)} hold-out trades are fewer than the {minTrades.ToString(CultureInfo.InvariantCulture)} needed");
        }

        if (!double.IsFinite(dev) || dev <= 0)
        {
            return new HoldOutResult(HoldOutStatus.Inconclusive, false, null, "the development out-of-sample expectancy is not above 0, so there is nothing to compare with");
        }

        var ratio = hold / dev;
        if (hold <= 0)
        {
            return new HoldOutResult(HoldOutStatus.Fail, false, ratio, "the hold-out expectancy is not above 0");
        }

        // compared as hold >= retention * dev with a tolerance for the last binary digit: 0.10 is exactly half of 0.20
        return ratio + 1e-9 >= retention
            ? new HoldOutResult(HoldOutStatus.Pass, true, ratio, $"the hold-out keeps {(ratio * 100).ToString("0.#", CultureInfo.InvariantCulture)} % of the development expectancy (at least {(retention * 100).ToString("0.#", CultureInfo.InvariantCulture)} % needed)")
            : new HoldOutResult(HoldOutStatus.Fail, false, ratio, $"the hold-out keeps {(ratio * 100).ToString("0.#", CultureInfo.InvariantCulture)} % of the development expectancy (at least {(retention * 100).ToString("0.#", CultureInfo.InvariantCulture)} % needed)");
    }
}
