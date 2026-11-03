using XDrishti.Domain.Filings;
using XDrishti.Domain.Learning.Extensions;
using XDrishti.Domain.Options.Iv;
using XDrishti.Domain.Options.Remark;
using XDrishti.Domain.Tests.Filings;
using static XDrishti.Domain.Tests.Options.Iv.IvKit;

namespace XDrishti.Domain.Tests.Options.Iv;

/// <summary>
/// P14-11 criterion 2, a property over 200 seeds: changing any bar that ends after the decision instant, any daily value or close of the trade date or later, or any calendar row intimated after the instant changes no feature of the trade; changing a value before it can (the control, so the property is
/// not vacuous). The inputs are random: a few bars missing, some IVs and open interests null, some key-sessions unusable, daily values with gaps.
/// </summary>
public sealed class IvNoLookAheadTests
{
    private const int Seeds = 200;

    private static readonly DateOnly Tue = new(2026, 7, 28);

    private static readonly DateOnly Thu = new(2026, 7, 30);

    private static double Draw(int seed, int salt, int slot, int bits)
    {
        var rng = new Random(HashCode.Combine(seed, salt, slot, bits));
        return rng.NextDouble();
    }

    private static List<OptionBar> Bars(int seed, DateTimeOffset t, int afterSalt, int beforeSalt = 0)
    {
        var bars = new List<OptionBar>();
        var slot = 0;
        foreach (var day in new[] { Tue, Wed, Thu })
        {
            for (var minute = 9 * 60 + 15; minute <= 15 * 60 + 25; minute += 5)
            {
                var ts = At(minute / 60, minute % 60, day);
                for (var offset = -3; offset <= 3; offset++)
                {
                    slot++;
                    var salt = ts + TimeSpan.FromMinutes(5) <= t ? beforeSalt : afterSalt;
                    var gap = Draw(seed, salt, slot, 1);
                    if (gap < 0.05)
                    {
                        continue;
                    }

                    float? iv = Draw(seed, salt, slot, 2) < 0.1 ? null : (float)(5 + (Draw(seed, salt, slot, 3) * 40));
                    long? oi = Draw(seed, salt, slot, 4) < 0.05 ? null : (long)(Draw(seed, salt, slot, 5) * 1000);
                    bars.Add(Bar(ts, offset, iv, oi, usable: Draw(seed, salt, slot, 6) > 0.03));
                }
            }
        }

        return bars;
    }

    private static IvBarBook Book(int seed, DateTimeOffset t, int afterSalt, int beforeSalt = 0)
    {
        var book = new IvBarBook();
        foreach (var code in new[] { 1, 2 })
        {
            foreach (var side in new[] { "CE", "PE" })
            {
                book.Add(code, side, 5, Bars(seed + (code * 7) + (side == "CE" ? 0 : 3), t, afterSalt, beforeSalt));
            }
        }

        return book;
    }

    private static Dictionary<DateOnly, double> Daily(int seed, DateOnly tradeDate, int afterSalt, int beforeSalt = 0, int offsetSeed = 0)
    {
        var map = new Dictionary<DateOnly, double>();
        for (var back = -3; back < 380; back++)
        {
            var date = tradeDate.AddDays(-back);
            var salt = date >= tradeDate ? afterSalt : beforeSalt;
            if (Draw(seed + offsetSeed, salt, back + 10, 7) < 0.08)
            {
                continue;
            }

            map[date] = 0.1 + (Draw(seed + offsetSeed, salt, back + 10, 8) * 0.5);
        }

        return map;
    }

    private static Dictionary<DateOnly, double> Closes(int seed, DateOnly tradeDate, int afterSalt)
    {
        var map = new Dictionary<DateOnly, double>();
        for (var back = -3; back < 60; back++)
        {
            var date = tradeDate.AddDays(-back);
            var salt = date >= tradeDate ? afterSalt : 0;
            map[date] = 90 + (Draw(seed, salt, back + 10, 9) * 20);
        }

        return map;
    }

    private static List<CalendarRow> Rows(int seed, DateTimeOffset t, int afterSalt)
    {
        var rows = new List<CalendarRow>();
        for (var n = 0; n < 6; n++)
        {
            var before = n < 3;
            var salt = before ? 0 : afterSalt;
            var meeting = new DateOnly(2026, 7, 30).AddDays((int)(Draw(seed, salt, n, 10) * 14));
            var intimated = before ? t.AddDays(-1 - (int)(Draw(seed, 0, n, 11) * 20)) : t.AddMinutes(1 + (int)(Draw(seed, salt, n, 12) * 5000));
            rows.Add(CalendarSamples.Row(n + 1 + (before ? 0 : 100 * afterSalt), meeting, intimated, period: new DateOnly(2026, 6, 30).AddDays(-n)));
        }

        return rows;
    }

    private static IvFeatureInput Build(int seed, DateTimeOffset t, int afterSalt, bool controlChange = false)
    {
        var tradeDate = Wed;
        var plan = Draw(seed, 0, 0, 20) < 0.9 ? Plan(front: 1 + (int)(Draw(seed, 0, 0, 21) * 2), back: Draw(seed, 0, 0, 22) < 0.7 ? 2 : null, expiry: new DateOnly(2026, 8, 4).AddDays((int)(Draw(seed, 0, 0, 23) * 12))) : null;
        var own = Daily(seed, tradeDate, afterSalt, controlChange ? 9 : 0);
        return Input(t, plan, Book(seed, t, afterSalt, controlChange ? 9 : 0), own, Daily(seed, tradeDate, afterSalt, 0, 1_000), Closes(seed, tradeDate, afterSalt), Rows(seed, t, afterSalt));
    }

    private static string Show(FeatureCell[] cells) => string.Join(";", cells.Select(c => BitConverter.DoubleToInt64Bits(c.Value) + "/" + c.Reason));

    [Fact]
    public void Changing_anything_after_the_instant_changes_no_feature_over_200_seeds()
    {
        var known = 0;
        for (var seed = 1; seed <= Seeds; seed++)
        {
            var t = At(10 + (seed % 5), (seed * 5) % 60);
            var baseline = IvFeatures.Compute(Build(seed, t, 1));
            var changed = IvFeatures.Compute(Build(seed, t, 2));

            Show(changed).ShouldBe(Show(baseline), "seed " + seed);
            known += baseline.Count(c => c.IsKnown);
        }

        known.ShouldBeGreaterThan(Seeds * 3, "the property is not vacuous: many values are known in the fixtures");
    }

    [Fact]
    public void A_control_change_before_the_instant_can_change_a_feature()
    {
        var changed = 0;
        for (var seed = 1; seed <= Seeds; seed++)
        {
            var t = At(10 + (seed % 5), (seed * 5) % 60);
            if (Show(IvFeatures.Compute(Build(seed, t, 1))) != Show(IvFeatures.Compute(Build(seed, t, 1, controlChange: true))))
            {
                changed++;
            }
        }

        changed.ShouldBeGreaterThan(Seeds / 2);
    }

    [Fact]
    public void The_features_do_not_depend_on_the_order_the_bars_are_added()
    {
        for (var seed = 1; seed <= 20; seed++)
        {
            var t = At(11, 0);
            var forward = new IvBarBook();
            var reversed = new IvBarBook();
            var bars = Bars(seed, t, 1);
            forward.Add(1, "CE", 5, bars);
            reversed.Add(1, "CE", 5, Enumerable.Reverse(bars));
            var pe = Bars(seed + 1, t, 1);
            forward.Add(1, "PE", 5, pe);
            reversed.Add(1, "PE", 5, Enumerable.Reverse(pe));

            Show(IvFeatures.Compute(Input(t, Plan(), forward))).ShouldBe(Show(IvFeatures.Compute(Input(t, Plan(), reversed))), "seed " + seed);
        }
    }
}
