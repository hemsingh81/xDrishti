using XDrishti.Domain.Learning.Extensions;
using XDrishti.Domain.Market.Actions;
using XDrishti.Domain.Options.Iv;
using static XDrishti.Domain.Tests.Options.Iv.IvKit;

namespace XDrishti.Domain.Tests.Options.Iv;

/// <summary>P14-11: the annualised sample standard deviation of the 20 daily log returns before the trade date, on hand values, with a split inside the window and every missing close.</summary>
public sealed class RealisedVolatilityTests
{
    private static Dictionary<DateOnly, double> Alternating() => new(Series(Wed, 21, i => i % 2 == 0 ? 100.0 : 101.0));

    [Fact]
    public void Alternating_one_percent_moves_have_the_hand_value()
    {
        var a = Math.Log(1.01);
        var expected = a * Math.Sqrt(20.0 / 19.0) * Math.Sqrt(252.0);

        var cell = RealisedVolatility.Annualised(Alternating(), Wed, Calendar, []);

        cell.IsKnown.ShouldBeTrue();
        cell.Value.ShouldBe(expected, 1e-12);
    }

    [Fact]
    public void A_missing_close_in_the_21_sessions_is_short_history_not_a_skipped_day()
    {
        var closes = Alternating();
        closes.Remove(new DateOnly(2026, 7, 15));

        var cell = RealisedVolatility.Annualised(closes, Wed, Calendar, []);

        cell.ShouldBe(FeatureCell.Unknown(FeatureReasons.ShortHistory));
    }

    [Fact]
    public void Nothing_of_the_trade_date_or_later_is_read()
    {
        var closes = Alternating();
        var before = RealisedVolatility.Annualised(closes, Wed, Calendar, []);
        closes[Wed] = 1_000_000.0;
        closes[Wed.AddDays(1)] = 0.01;

        RealisedVolatility.Annualised(closes, Wed, Calendar, []).Value.ShouldBe(before.Value);
    }

    [Fact]
    public void A_split_inside_the_window_is_put_on_one_basis_and_does_not_look_like_a_crash()
    {
        // a 2:1 split ex-dated 2026-07-20: closes before it are about twice those after it; the factor 0.5 puts the earlier close on the new basis
        var closes = new Dictionary<DateOnly, double>(Series(Wed, 21, i => 101.0));
        foreach (var date in closes.Keys.Where(d => d < new DateOnly(2026, 7, 20)).ToList())
        {
            closes[date] = 202.0;
        }

        var action = new AppliedAction(new DateOnly(2026, 7, 20), CorporateActionKind.Split, 2, 1, 0.5m);

        var adjusted = RealisedVolatility.Annualised(closes, Wed, Calendar, [action]);
        var raw = RealisedVolatility.Annualised(closes, Wed, Calendar, []);

        adjusted.Value.ShouldBe(0.0, 1e-12, "flat after adjustment");
        raw.Value.ShouldBeGreaterThan(0.1, "the unadjusted drop is a false 69 percent move");
    }

    [Fact]
    public void A_flat_window_is_a_known_zero_volatility_but_a_non_positive_close_is_unknown()
    {
        var flat = new Dictionary<DateOnly, double>(Series(Wed, 21, _ => 100.0));
        RealisedVolatility.Annualised(flat, Wed, Calendar, []).ShouldBe(FeatureCell.Known(0.0));

        flat[new DateOnly(2026, 7, 28)] = 0.0;
        RealisedVolatility.Annualised(flat, Wed, Calendar, []).Reason.ShouldBe(FeatureReasons.ShortHistory);
    }
}
