using XDrishti.Domain.Options.Remark;
using XDrishti.Domain.Options.Structures;
using XDrishti.Domain.Tests.Options.Remark;

namespace XDrishti.Domain.Tests.Options.Structures;

/// <summary>
/// P14-10 criterion 1 (property) and criterion 6. The paths are consistent: one random underlying path, every leg priced from it by a monotone model (intrinsic value plus a time value that falls with the distance from the money), so a spread's value stays between 0 and its width by construction.
/// Over 200 seeds the gross result of a paired structure is never below minus its stored maximum loss nor above its stored maximum gain, with no clamp anywhere to lean on; nothing after the exit bar moves the result; every short leg stands beside a long leg.
/// </summary>
public sealed class StructureRemarkPropertyTests
{
    private const int Step = RemarkKit.Step;

    private static int TimeValue(double distanceInSteps) => Math.Max(6000 - (int)(600 * distanceInSteps), 300);

    private static int Price(bool call, int strike, int spot) => (call ? Math.Max(spot - strike, 0) : Math.Max(strike - spot, 0)) + TimeValue(Math.Abs(spot - strike) / (double)Step);

    private static int[] Walk(int seed)
    {
        var rng = new Random(seed);
        var spots = new int[76];
        spots[0] = RemarkKit.BaseSpot;
        for (var i = 1; i < spots.Length; i++)
        {
            spots[i] = spots[i - 1] + rng.Next(-1_200, 1_201);
        }

        return spots;
    }

    /// <summary>The bars of one day: the offsets are steps from the ATM of the bar's spot, the strike of an offset follows it, the open prices at the bar's spot and the close at the next bar's.</summary>
    private static List<OptionBar> Bars(DateOnly day, bool call, int[] spots)
    {
        var bars = new List<OptionBar>();
        for (var i = 0; i < 75; i++)
        {
            var ts = RemarkKit.At(day, 9, 15).AddMinutes(i * 5);
            var atm = (int)(Math.Round(spots[i] / (double)Step) * Step);
            for (var offset = -10; offset <= 10; offset++)
            {
                var strike = atm + (offset * Step);
                var open = Price(call, strike, spots[i]);
                var close = Price(call, strike, spots[i + 1]);
                bars.Add(new OptionBar(ts, offset, strike, spots[i], open, Math.Max(open, close) + 50, Math.Max(Math.Min(open, close) - 50, 1), close, 1000, 10_000, 15f, false, true));
            }
        }

        return bars;
    }

    private static RemarkTrade TradeFor(Random rng) => RemarkKit.Trade(buy: rng.Next(2) == 0, entryMinute: 15 + (5 * rng.Next(0, 6)), exitHour: 10 + rng.Next(0, 4), exitMinute: rng.Next(0, 60));

    [Fact]
    public void A_paired_debit_or_credit_spread_never_loses_more_than_its_stored_maximum_loss_nor_gains_more_than_its_stored_maximum_gain_over_200_consistent_paths()
    {
        var paired = 0;
        for (var seed = 0; seed < 200; seed++)
        {
            var rng = new Random(seed);
            var trade = TradeFor(rng);
            var profile = (seed % 3) switch { 0 => StructureKit.Debit(), 1 => StructureKit.Debit(stop: 40m), _ => StructureKit.Credit("itm1") };
            var spots = Walk(seed);

            var result = StructureKit.Spread(trade, Bars(RemarkKit.Tuesday, trade.IsBuy != (seed % 3 == 2), spots), profile);

            if (!result.Row.IsPaired)
            {
                continue;
            }

            paired++;
            var units = (decimal)result.Row.Units!.Value;
            var gross = result.Row.GrossInr!.Value;
            gross.ShouldBeGreaterThanOrEqualTo((-result.MaxLossPx!.Value / 100m * units) - 0.01m, $"seed {seed}");
            gross.ShouldBeLessThanOrEqualTo((result.MaxGainPx!.Value / 100m * units) + 0.01m, $"seed {seed}");
            result.Row.REquiv!.Value.ShouldBeGreaterThanOrEqualTo(-1.0 - 1e-6, $"seed {seed}"); // the maximum loss is the cash trade's rupee risk
            result.Row.ExitTs.ShouldBeGreaterThanOrEqualTo(result.Row.EntryTs);
            result.Legs.Count.ShouldBe(2);
            result.Legs.Count(l => l.Position == "short").ShouldBe(1, "a short leg is always beside a long leg of the same side");
            result.Legs.Select(l => l.Side).Distinct().Count().ShouldBe(1);
        }

        paired.ShouldBeGreaterThan(80);
    }

    [Fact]
    public void A_paired_condor_never_loses_more_than_its_stored_maximum_loss_over_200_consistent_paths()
    {
        var paired = 0;
        for (var seed = 0; seed < 200; seed++)
        {
            var spots = Walk(seed);
            var next = Walk(seed + 1_000);
            var calls = BarGrid.Build([.. Bars(RemarkKit.Tuesday, true, spots), .. Bars(StructureKit.Wednesday, true, next)]);
            var puts = BarGrid.Build([.. Bars(RemarkKit.Tuesday, false, spots), .. Bars(StructureKit.Wednesday, false, next)]);

            var result = StructureRemark.EvaluateCondor(StructureKit.CondorTrade(), RemarkKit.Plan, CondorParams.Variants[seed % 4], calls, puts, RemarkKit.Context());

            if (!result.Row.IsPaired)
            {
                continue;
            }

            paired++;
            result.Row.GrossInr!.Value.ShouldBeGreaterThanOrEqualTo((-result.MaxLossPx!.Value / 100m * 75m) - 0.01m, $"seed {seed}");
            result.Row.GrossInr.Value.ShouldBeLessThanOrEqualTo((result.MaxGainPx!.Value / 100m * 75m) + 0.01m, $"seed {seed}");
            result.Legs.Count.ShouldBe(4);
            foreach (var side in new[] { "CE", "PE" })
            {
                var wing = result.Legs.Single(l => l.Side == side && l.Position == "long");
                var body = result.Legs.Single(l => l.Side == side && l.Position == "short");
                Math.Abs(wing.StrikePx - RemarkKit.BaseSpot).ShouldBeGreaterThan(Math.Abs(body.StrikePx - RemarkKit.BaseSpot), "the wing is beyond the short strike");
            }
        }

        paired.ShouldBeGreaterThan(20);
    }

    [Fact]
    public void Bars_after_the_exit_bar_change_nothing_so_no_row_looks_ahead()
    {
        for (var seed = 0; seed < 50; seed++)
        {
            var trade = RemarkKit.Trade(exitHour: 11, exitMinute: 0);
            var profile = seed % 2 == 0 ? StructureKit.Debit(stop: 40m) : StructureKit.Debit();
            var path = Walk(seed);
            var other = Walk(seed + 5_000);
            var changed = path.Select((s, i) => i > 22 ? other[i] : s).ToArray();

            var baseline = StructureKit.Spread(trade, Bars(RemarkKit.Tuesday, true, path), profile);
            var later = StructureKit.Spread(trade, Bars(RemarkKit.Tuesday, true, changed), profile);

            // 11:00 is bar 21; its open is the exit price: bar 21's own close and the bars after it are the future of the trade
            (later.Row.Fate, later.Row.ExitTs, later.Row.GrossInr, later.Row.ExitPremiumPx).ShouldBe((baseline.Row.Fate, baseline.Row.ExitTs, baseline.Row.GrossInr, baseline.Row.ExitPremiumPx), $"seed {seed}");
        }
    }
}
