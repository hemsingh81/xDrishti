using XDrishti.Domain.Market.Bars;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.Tests.Support.Oracle;

namespace XDrishti.Strategies.Tests.Evaluation;

/// <summary>
/// Criterion 10, the point of the slice: <b>a later bar can never change an earlier answer</b>. For 240 seeds (40 on each of the evaluation timeframes 1m, 5m, 15m, 1h, 1D and 1W) and 1,200 random conditions (five to
/// a seed, all in one frame, a context on half of the seeds), the three-valued result and the decision of every condition at every closed bar of the evaluation timeframe is the same (a) when every later 1-minute bar is
/// removed and (b) when every later bar is replaced by arbitrary values — extreme prices, zero prices, zero volumes, huge volumes —, for cut points at the closed bars (all of them when there are few, a sample with the first and
/// the last otherwise) and a few minutes in the middle of a bar (see <see cref="LookAheadProperty"/>). On the seeds that have a market context the same runs also remove, or change, the context days dated at or after the
/// cut date, which a step on that date must not read (the context is read as of the previous day).
/// <para>
/// The <b>negative controls</b> prove that the property can fail: run through a feeder that gives the frame the first bar after the step before reading (<see cref="NextBarFeeder"/>), one that runs the frame on to the
/// close of the higher-timeframe bar before reading (<see cref="FormingBarFeeder"/>) or one that reads the context day of the date itself (<see cref="SameDayContextFeeder"/>), the same property reports violations — for
/// hand-written conditions that must be caught, and for the random conditions —, while the honest feeder reports none on the very same inputs.
/// </para>
/// <para>
/// <b>Limits.</b> The property guards the feed of the frame, the frame itself and the determinism of the chain; it cannot fail for a wrong compiler, because a compiled condition is a pure function of the frame as it
/// is at the step. The compiler is guarded by the oracle (<see cref="InterpreterOracleTests"/>, which holds the lag and the clock exactly, and whose mutant control shows that it notices a wrong one), the hand examples,
/// <c>CompiledTreeTests</c> (a tree can only call the kernel, reach the frame through three doors and read no negative lag) and the architecture test on the members of the frame the library may use.
/// </para>
/// </summary>
public sealed class LookAheadPropertyTests
{
    private const int SeedsPerTimeframe = 40;

    private const int ConditionsPerSeed = 5;

    private static readonly Timeframe[] Evaluated = [Timeframe.M1, Timeframe.M5, Timeframe.M15, Timeframe.H1, Timeframe.D1, Timeframe.W1];

    public static TheoryData<Timeframe> Evaluations { get; } = [.. Evaluated];

    /// <summary>The conditions of a seed, printed in the three spacings and both bracket styles, compiled once, with the series, context and expiry dates that fit.</summary>
    private static (Scenario Scenario, Bar[] Bars) SeedFor(int seed, Timeframe evaluation)
    {
        var withContext = seed % 2 == 0;
        var generator = new ModelGenerator(seed, evaluation, withContext, allowExpiryDays: true);
        var sources = Enumerable.Range(0, ConditionsPerSeed).Select(i => Printer.Print(generator.Next(), (i + seed) % 2 == 0 ? Parens.Always : Parens.Minimal, (Spacing)((i + seed) % 3))).ToList();
        var bars = OracleSeries.For(seed, evaluation, light: true);
        var scenario = Scenario.Create(evaluation, sources, expiryDays: OracleContext.ExpiryDays(seed), contextDays: withContext ? OracleContext.Days(seed, bars) : null);
        return (scenario, bars);
    }

    private static string Show(IEnumerable<string> violations) => string.Join("\n", violations.Take(8));

    // ---- the property -----------------------------------------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_corpus_has_at_least_200_seeds_and_1000_conditions()
    {
        (SeedsPerTimeframe * Evaluated.Length).ShouldBeGreaterThanOrEqualTo(200);
        (SeedsPerTimeframe * Evaluated.Length * ConditionsPerSeed).ShouldBeGreaterThanOrEqualTo(1_000);
    }

    [Theory]
    [MemberData(nameof(Evaluations))]
    public void Removing_or_replacing_every_later_bar_changes_no_answer_at_a_closed_bar(Timeframe evaluation)
    {
        var violations = new List<string>();
        var comparisons = 0;
        var cuts = 0;
        var contextRuns = 0;
        var values = new Dictionary<Truth, int> { [Truth.True] = 0, [Truth.False] = 0, [Truth.Unknown] = 0 };

        for (var s = 0; s < SeedsPerTimeframe; s++)
        {
            var seed = 900_000 + (int)evaluation * 1_000 + s;
            var (scenario, bars) = SeedFor(seed, evaluation);

            var report = LookAheadProperty.Check(new HonestFeeder(), scenario, bars, seed);

            violations.AddRange(report.Violations.Select(v => $"seed {seed}: {v}"));
            comparisons += report.Comparisons;
            cuts += report.Cuts;
            contextRuns += report.ContextRuns;
            foreach (var (truth, count) in report.Values)
            {
                values[truth] += count;
            }
        }

        violations.ShouldBeEmpty(violations.Count + " answers changed when later bars changed:\n" + Show(violations));
        cuts.ShouldBeGreaterThan(SeedsPerTimeframe * 4, "too few cut points for " + evaluation);
        comparisons.ShouldBeGreaterThan(SeedsPerTimeframe * ConditionsPerSeed * 20, "too few values compared for " + evaluation);
        contextRuns.ShouldBeGreaterThan(SeedsPerTimeframe * 4, "too few runs with the context days from the cut date on removed or changed for " + evaluation);
        values[Truth.True].ShouldBeGreaterThan(0, evaluation + ": the conditions were never true at a closed bar");
        values[Truth.False].ShouldBeGreaterThan(0, evaluation + ": the conditions were never false at a closed bar");
        values[Truth.Unknown].ShouldBeGreaterThan(0, evaluation + ": the conditions were never unknown at a closed bar");
    }

    // ---- the negative controls --------------------------------------------------------------------------------------------------------------------------------------------------------------

    /// <summary>Conditions that read the last 1-minute bar and the date: a feeder that lets the next bar in must change them.</summary>
    private static readonly string[] LastBarSources =
    [
        "tf(\"1m\").close > tf(\"1m\").open",
        "tf(\"1m\").close > tf(\"1m\").close[1]",
        "tf(\"1m\").volume > tf(\"1m\").volume[1]",
        "day_of_week >= WED",
    ];

    private static Timeframe HigherThan(Timeframe evaluation) => evaluation switch
    {
        Timeframe.D1 => Timeframe.W1,
        Timeframe.W1 => Timeframe.Mo1,
        Timeframe.H1 => Timeframe.D1,
        _ => Timeframe.H1,
    };

    /// <summary>Conditions that compare a bar of the next timeframe up with the evaluation timeframe's: a feeder that reads that bar at its close before it has closed must change them.</summary>
    private static string[] HigherBarSources(Timeframe evaluation)
    {
        var higher = HigherThan(evaluation).ToCode();
        return
        [
            $"tf(\"{higher}\").close > close",
            $"tf(\"{higher}\").close > tf(\"{higher}\").open",
            $"tf(\"{higher}\").low < low",
            $"tf(\"{higher}\").high > tf(\"{higher}\").high[1]",
        ];
    }

    private static (int Honest, int Leaky, int Compared) Controls(Timeframe evaluation, string[] sources, IFeeder leaky)
    {
        var honest = 0;
        var leak = 0;
        var compared = 0;
        for (var s = 0; s < 6; s++)
        {
            var seed = 910_000 + (int)evaluation * 1_000 + s;
            var bars = OracleSeries.For(seed, evaluation, light: true);
            var scenario = Scenario.Create(evaluation, sources);

            honest += LookAheadProperty.Check(new HonestFeeder(), scenario, bars, seed).ViolationCount;
            var wrong = LookAheadProperty.Check(leaky, scenario, bars, seed);
            leak += wrong.ViolationCount;
            compared += wrong.Comparisons;
        }

        return (honest, leak, compared);
    }

    [Theory]
    [MemberData(nameof(Evaluations))]
    public void A_feeder_that_gives_the_frame_the_first_later_bar_is_reported_as_a_violation(Timeframe evaluation)
    {
        var (honest, leaky, compared) = Controls(evaluation, LastBarSources, new NextBarFeeder());

        honest.ShouldBe(0, "the honest feeder must report nothing on the same inputs");
        compared.ShouldBeGreaterThan(0);
        leaky.ShouldBeGreaterThan(0, "the property did not notice a feeder that reads the first bar after the step on " + evaluation);
    }

    [Theory]
    [MemberData(nameof(Evaluations))]
    public void A_feeder_that_reads_the_higher_timeframe_bar_before_it_closes_is_reported_as_a_violation(Timeframe evaluation)
    {
        var (honest, leaky, compared) = Controls(evaluation, HigherBarSources(evaluation), new FormingBarFeeder());

        honest.ShouldBe(0, "the honest feeder must report nothing on the same inputs");
        compared.ShouldBeGreaterThan(0);
        leaky.ShouldBeGreaterThan(0, "the property did not notice a feeder that reads the forming higher-timeframe bar on " + evaluation);
    }

    /// <summary>Conditions on the context: a feeder that lets a step on the date <i>D</i> read the record dated <i>D</i> must change them.</summary>
    private static readonly string[] ContextSources =
    [
        "vix > 18",
        "vix_chg > 0",
        "vix_pct > 50",
        "breadth > 50",
        "index_trend >= 0",
        "regime() != 4",
        "regime_vol == 1",
        "index_vol_pct > 40",
    ];

    [Theory]
    [MemberData(nameof(Evaluations))]
    public void A_feeder_that_reads_the_context_day_of_the_date_itself_is_reported_as_a_violation(Timeframe evaluation)
    {
        var honest = 0;
        var leaky = 0;
        var runs = 0;
        for (var s = 0; s < 6; s++)
        {
            var seed = 920_000 + (int)evaluation * 1_000 + s;
            var bars = OracleSeries.For(seed, evaluation, light: true);
            var scenario = Scenario.Create(evaluation, ContextSources, contextDays: OracleContext.Days(seed, bars));

            honest += LookAheadProperty.Check(new HonestFeeder(), scenario, bars, seed).ViolationCount;
            var wrong = LookAheadProperty.Check(new SameDayContextFeeder(), scenario, bars, seed);
            leaky += wrong.ViolationCount;
            runs += wrong.ContextRuns;
        }

        honest.ShouldBe(0, "the honest feeder must report nothing on the same inputs");
        runs.ShouldBeGreaterThan(0);
        leaky.ShouldBeGreaterThan(0, "the property did not notice a feeder that reads the context day of the date itself on " + evaluation);
    }

    [Theory]
    [MemberData(nameof(Evaluations))]
    public void Both_wrong_feeders_are_also_reported_on_the_random_conditions_of_the_property(Timeframe evaluation)
    {
        var honest = 0;
        var nextBar = 0;
        var forming = 0;
        for (var s = 0; s < 8; s++)
        {
            var seed = 900_000 + (int)evaluation * 1_000 + s;
            var (scenario, bars) = SeedFor(seed, evaluation);

            honest += LookAheadProperty.Check(new HonestFeeder(), scenario, bars, seed).ViolationCount;
            nextBar += LookAheadProperty.Check(new NextBarFeeder(), scenario, bars, seed).ViolationCount;
            forming += LookAheadProperty.Check(new FormingBarFeeder(), scenario, bars, seed).ViolationCount;
        }

        honest.ShouldBe(0);
        nextBar.ShouldBeGreaterThan(0, "no violation found for the feeder that reads the first bar after the step");
        forming.ShouldBeGreaterThan(0, "no violation found for the feeder that reads the forming higher-timeframe bar");
    }

    // ---- the harness itself -------------------------------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_perturbation_keeps_the_bars_up_to_the_cut_and_replaces_every_later_one_at_the_same_time()
    {
        var bars = OracleSeries.Build(3, SeriesShape.Dense, 1);

        var perturbed = LookAheadProperty.Perturb(bars, 100, new Random(1));

        perturbed.Length.ShouldBe(bars.Length);
        perturbed[..101].ShouldBe(bars[..101]);
        perturbed.Select(b => b.EpochSeconds).ShouldBe(bars.Select(b => b.EpochSeconds));
        Enumerable.Range(101, bars.Length - 101).Count(i => perturbed[i] != bars[i]).ShouldBe(bars.Length - 101);
        perturbed[101..].Any(b => b.Volume == 0).ShouldBeTrue("no zero volume among the replaced bars");
        perturbed[101..].Any(b => b.Close >= 1e11).ShouldBeTrue("no extreme price among the replaced bars");
        perturbed[101..].Any(b => b.Close == 0).ShouldBeTrue("no zero price among the replaced bars");
    }

    [Fact]
    public void The_context_variants_of_a_cut_are_the_full_context_up_to_the_day_before_the_cut_date_and_differ_from_the_cut_date_on()
    {
        var bars = OracleSeries.Build(5, SeriesShape.Dense, 1);
        var days = OracleContext.Days(5, bars);
        var cutDate = SessionClock.DateOf(bars[bars.Length / 2].EpochSeconds);
        var full = OracleContext.Build(days);
        var without = OracleContext.Build(days.Where(d => d.Date < cutDate));
        var changed = OracleContext.Build(days.Select(d => d.Date < cutDate ? d : OracleContext.Altered(d)));
        var dayAfter = cutDate.AddDays(1);

        // what a step on the cut date (or before it) reads is the same in the three
        full.TryAsOf(cutDate, out var fullAtCut).ShouldBeTrue();
        without.TryAsOf(cutDate, out var withoutAtCut).ShouldBeTrue();
        changed.TryAsOf(cutDate, out var changedAtCut).ShouldBeTrue();
        withoutAtCut.ShouldBe(fullAtCut);
        changedAtCut.ShouldBe(fullAtCut);

        // from the next date the record of the cut date is visible, and it is not the same in the three: the variants are real changes
        full.TryAsOf(dayAfter, out var fullAfter).ShouldBeTrue();
        without.TryAsOf(dayAfter, out var withoutAfter).ShouldBeTrue();
        changed.TryAsOf(dayAfter, out var changedAfter).ShouldBeTrue();
        withoutAfter.Date.ShouldBeLessThan(cutDate);
        fullAfter.Date.ShouldBe(cutDate);
        changedAfter.Date.ShouldBe(cutDate);
        changedAfter.Vix.ShouldBe(99.9);
        changedAfter.ShouldNotBe(fullAfter);
    }

    [Fact]
    public void The_scenario_with_records_keeps_them_and_a_scenario_with_a_ready_context_or_none_has_none()
    {
        var bars = OracleSeries.Build(5, SeriesShape.Dense, 1);
        var days = OracleContext.Days(5, bars);

        var withRecords = Scenario.Create(Timeframe.M5, ["vix > 18"], contextDays: days);
        var withReady = Scenario.Create(Timeframe.M5, ["vix > 18"], OracleContext.Build(days));
        var report = LookAheadProperty.Check(new HonestFeeder(), withReady, bars, 1);

        withRecords.ContextDays.ShouldBe(days);
        withReady.ContextDays.ShouldBeNull();
        report.ContextRuns.ShouldBe(0, "a scenario that was given a ready context cannot have its records cut");
        report.ViolationCount.ShouldBe(0);
    }

    [Fact]
    public void The_honest_run_of_a_cut_series_is_the_prefix_of_the_run_of_the_full_series_for_a_condition_with_no_lookahead()
    {
        var scenario = Scenario.Create(Timeframe.M5, ["close > close[1]", "rsi(3) > 50"]);
        var bars = OracleSeries.Build(8, SeriesShape.Dense, 1);

        var full = new HonestFeeder().Run(scenario, bars);
        var cut = new HonestFeeder().Run(scenario, [.. bars.Take(200)]);

        cut.Closed.ShouldBe(full.Closed[..200]);
        cut.Truths.SelectMany(t => t).ShouldBe(full.Truths.Take(200).SelectMany(t => t));
        cut.Decisions.SelectMany(d => d).ShouldBe(full.Decisions.Take(200).SelectMany(d => d));
    }
}
