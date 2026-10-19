using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Running;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Strategies.BuiltIn;

namespace XDrishti.Simulation.Tests.Leaks;

/// <summary>
/// Scale invariance through <c>Simulator.Run</c> (spec P3-08b, Annex B (a)). <b>What is invariant, and why (read from the definitions, 2026-10-19):</b> ORB v1 compares prices with prices only (<c>close &gt; or_high</c>, the stop is the opposite side of the opening
/// range, the target is <c>entry ± rr × risk</c>), the entry limit and the slippage are fractions of the price or whole ticks, and the minutes of its windows are minutes; <c>VWAP_PULLBACK</c> compares <c>close</c>, <c>vwap</c>, <c>low</c> and
/// <c>vwap + pull_atr × atr(14)</c> (a multiple of a price range); <c>BASE_BREAKOUT</c> compares a range as a percentage of a price and <c>rel_volume</c> (a ratio of volumes). None of the three has a rupee threshold, a volume threshold or a price floor, so they are
/// scale-invariant up to the grid: the lists of numeric literals in their condition texts are asserted below to be periods, lags and multiples only. Prices and volumes are multiplied by 2 and 4 (powers of two, so every double is exact) and the tick by the same
/// factor (the grid scales with the price); the candidates, the trades, the ratios (R, MAE, MFE, slippage in R), the counters and the dates are the base run's, and every price is the base run's times the factor, bit for bit. The negative control keeps the old
/// tick of 0.05 for the scaled prices (a fill rounded to a fixed 0.05 grid): the limit and the slippage land on other levels. Not invariant, and not claimed: a scale that is not a power of two (the decimal rounding to 8 places and the tick grid are not
/// homogeneous for 3 or 10 in general), a fixed absolute tick, and the cost chain (charges are in rupees: the cost in R is scale-invariant only at a quantity that scales, and the quantity is floored).
/// </summary>
public sealed class ScaleInvarianceTests(ITestOutputHelper output)
{
    private static readonly int[] Factors = [2, 4];

    private sealed record Subject(string Name, int Seed, Bar[] Bars, Simulator Simulator, decimal Tick, TradingCalendar? Calendar);

    private static readonly Lazy<IReadOnlyList<Subject>> All = new(Build);

    private static List<Subject> Build()
    {
        var subjects = new List<Subject>();
        var orbExact = Sim.Create(OrbV1.Create(), Timeframe.M15, Cand.NoSlippage);
        var orbModelled = Sim.Create(OrbV1.Create(), Timeframe.M15, FillRules.Default);
        var swing1h = Sim.Create(TestStrategies.Swing1h(), Timeframe.H1, FillRules.Default);
        var vwap = Sim.Create(VwapPullback.Create(), Timeframe.M15, Cand.NoSlippage);
        var daily = Sim.Create(TestStrategies.SwingDaily(DailyFixture.Trigger), Timeframe.D1, FillRules.Default);
        foreach (var seed in Enumerable.Range(1, 200))
        {
            var bars = SeededSeries.Of((ulong)seed);
            subjects.Add(new Subject("ORB exact", seed, bars, orbExact, 0.05m, null));
            subjects.Add(new Subject("ORB modelled", seed, bars, orbModelled, 0.05m, null));
        }

        foreach (var seed in Enumerable.Range(1, 60))
        {
            subjects.Add(new Subject("SWING_1H", seed, SeededSeries.Of((ulong)seed), swing1h, 0.05m, null));
        }

        foreach (var seed in Enumerable.Range(1, 12))
        {
            subjects.Add(new Subject("VWAP_PULLBACK", seed, VwapSeeded.Of((ulong)seed), vwap, 0.05m, null));
        }

        foreach (var series in Enumerable.Range(1, 60).Select(SeededDaily.Make))
        {
            subjects.Add(new Subject("SWING_DAILY", series.Seed, [.. series.Bars], daily, series.Tick, series.Truth));
        }

        foreach (var series in Enumerable.Range(1, 12).Select(SeededBase.Make))
        {
            var simulator = Sim.Create(BaseBreakoutV1.Create(series.BaseLen, (double)series.BasePct, (double)series.VolX, (double)series.RewardRisk), Timeframe.D1, Cand.NoSlippage);
            subjects.Add(new Subject("BASE_BREAKOUT", series.Seed, series.Bars(), simulator, SeededBase.Tick, series.Calendar));
        }

        return subjects;
    }

    private static SimulationResult Run(Subject s, Bar[] bars, decimal tick) => Sim.Run(s.Simulator, bars, tick: Leak.Tick(tick), calendar: s.Calendar);

    private static string Scaled(Subject s, int factor, decimal tickFactor) => Leak.Digest(Run(s, Leak.Scale(s.Bars, factor), s.Tick * tickFactor), factor, factor);

    [Fact]
    public void The_built_in_strategies_have_no_rupee_threshold_only_periods_lags_and_multiples()
    {
        var texts = new[] { OrbV1.Create(), VwapPullback.Create(), BaseBreakoutV1.Create() }
            .SelectMany(d => new[] { d.Buy, d.Sell }.OfType<XDrishti.Strategies.Model.SideRules>())
            .SelectMany(r => (r.Trigger?.Items ?? []).Concat(r.Setup?.Items ?? []).Concat(r.Invalidate?.Items ?? []))
            .Distinct()
            .ToList();

        // A numeric literal outside a call's parentheses or a lag's brackets would be a threshold on a price or a volume. Every other literal here is an indicator period, a lag, or a named parameter. The one allowed literal outside them is the
        // `* 100` of BASE_BREAKOUT's range-as-a-percentage (a unit conversion of a ratio, compared with the parameter base_pct): it is named, not a rupee level.
        var offenders = texts
            .Select(t => System.Text.RegularExpressions.Regex.Replace(System.Text.RegularExpressions.Regex.Replace(t, @"\([^()]*\)|\[[^\]]*\]", string.Empty), @"\* 100 <=", "<="))
            .Where(t => System.Text.RegularExpressions.Regex.IsMatch(t, @"(?<![\w.])\d+(\.\d+)?(?![\w.])"))
            .ToList();

        output.WriteLine($"{texts.Count} distinct condition texts of ORB v1, VWAP_PULLBACK and BASE_BREAKOUT; absolute-threshold exclusion list: {offenders.Count}");
        texts.Count.ShouldBeGreaterThanOrEqualTo(8);
        offenders.ShouldBeEmpty();
    }

    [Fact]
    public void Scaling_prices_volumes_and_the_tick_by_two_or_four_changes_no_decision_and_no_ratio_and_the_prices_scale_exactly()
    {
        var mismatches = new List<string>();
        long trades = 0, candidates = 0;
        var perSubject = new SortedDictionary<string, (int Runs, int Trades)>();
        foreach (var s in All.Value)
        {
            var baseRun = Run(s, s.Bars, s.Tick);
            var expected = Leak.Digest(baseRun);
            candidates += baseRun.Outcomes.Count;
            trades += baseRun.Outcomes.Count(o => o.Walk.Trade is not null);
            var seen = perSubject.GetValueOrDefault(s.Name);
            perSubject[s.Name] = (seen.Runs + 1, seen.Trades + baseRun.Outcomes.Count(o => o.Walk.Trade is not null));
            foreach (var factor in Factors)
            {
                if (Scaled(s, factor, factor) != expected)
                {
                    mismatches.Add($"{s.Name} seed {s.Seed} x{factor}");
                }
            }
        }

        output.WriteLine($"{All.Value.Count} base runs x {Factors.Length} factors = {All.Value.Count * Factors.Length} scaled runs; {candidates} candidates, {trades} trades compared; mismatches {mismatches.Count}");
        foreach (var (name, (runs, subjectTrades)) in perSubject)
        {
            output.WriteLine($"  {name}: {runs} series, {subjectTrades} trades");
        }

        mismatches.ShouldBeEmpty();
        perSubject["ORB exact"].Runs.ShouldBeGreaterThanOrEqualTo(200);
        perSubject["ORB exact"].Trades.ShouldBeGreaterThanOrEqualTo(150);
        perSubject["ORB modelled"].Trades.ShouldBeGreaterThanOrEqualTo(150);
        perSubject["SWING_1H"].Trades.ShouldBeGreaterThanOrEqualTo(10);
        perSubject["SWING_DAILY"].Trades.ShouldBeGreaterThanOrEqualTo(40);
        perSubject["VWAP_PULLBACK"].Trades.ShouldBeGreaterThanOrEqualTo(1);
        perSubject["BASE_BREAKOUT"].Runs.ShouldBeGreaterThanOrEqualTo(12);
    }

    [Fact]
    public void Control_a_fill_rounded_to_a_fixed_005_grid_after_scaling_is_reported_on_at_least_ten_seeds_while_the_honest_scaling_reports_none()
    {
        var orb = All.Value.Where(s => s.Name == "ORB modelled").ToList();
        var daily = All.Value.Where(s => s.Name == "SWING_DAILY").ToList();

        int Reported(IEnumerable<Subject> subjects, int factor) => subjects.Count(s => Scaled(s, factor, 1) != Leak.Digest(Run(s, s.Bars, s.Tick)));
        int Honest(IEnumerable<Subject> subjects, int factor) => subjects.Count(s => Scaled(s, factor, factor) != Leak.Digest(Run(s, s.Bars, s.Tick)));

        var orb2 = Reported(orb, 2);
        var orb4 = Reported(orb, 4);
        var daily4 = Reported(daily, 4);
        output.WriteLine($"fixed-grid control: ORB x2 {orb2} of {orb.Count} seeds, ORB x4 {orb4} of {orb.Count}, SWING_DAILY x4 {daily4} of {daily.Count}; honest scaling: ORB {Honest(orb, 2) + Honest(orb, 4)}, SWING_DAILY {Honest(daily, 4)}");
        Honest(orb, 2).ShouldBe(0);
        Honest(orb, 4).ShouldBe(0);
        Honest(daily, 4).ShouldBe(0);
        orb2.ShouldBeGreaterThanOrEqualTo(10);
        orb4.ShouldBeGreaterThanOrEqualTo(10);
    }

    [Fact]
    public void Control_a_threshold_in_rupees_is_reported_on_at_least_ten_seeds_so_the_comparison_can_see_a_scale_dependence()
    {
        // A strategy with an absolute rupee threshold in its trigger: close above 100.30 on top of the opening-range break. It is the only place a rupee number enters the run, and scaling must change what it does.
        var threshold = OrbV1.Create().WithBuy(b => b with { Trigger = TestStrategies.All(OrbV1.BuyTrigger, "close > 100.30") });
        var simulator = Sim.Create(threshold, Timeframe.M15, Cand.NoSlippage);
        var seeds = All.Value.Where(s => s.Name == "ORB exact").Select(s => s with { Simulator = simulator }).ToList();

        var reported = seeds.Count(s => Scaled(s, 2, 2) != Leak.Digest(Run(s, s.Bars, s.Tick)));

        output.WriteLine($"rupee-threshold control: reported on {reported} of {seeds.Count} seeds");
        reported.ShouldBeGreaterThanOrEqualTo(10);
    }
}
