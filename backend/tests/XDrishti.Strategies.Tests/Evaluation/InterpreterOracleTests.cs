using System.Text;
using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.Tests.Support;
using XDrishti.Strategies.Tests.Support.Oracle;

namespace XDrishti.Strategies.Tests.Evaluation;

/// <summary>
/// Criterion 7: the compiled condition equals an <b>independent tree-walking interpreter</b> on seeded random conditions over seeded random frames. The interpreter (<see cref="Interpreter"/>) is written from the
/// <i>Definitions</i> of the spec in the test project, evaluates a model of the condition that exists before any text does (<see cref="Node"/>), reads the frame only through its public members and shares no code or type with
/// the parser, the binder, the lint or the compiler; a second interpreter of the same kind walks the <i>bound</i> tree the compiler is built from (<see cref="BoundTreeInterpreter"/>) and tells a binder fault from a compiler
/// fault. The model is printed to text (<see cref="Printer"/>, bracketed or relying on the precedence rules, with three kinds of spacing), the text is compiled, and the two
/// answers are compared <b>after every 1-minute step</b> — so at every closed bar of the evaluation timeframe and between them —, as the full three-valued result and as the decision.
/// <para>
/// For each of the nine evaluation timeframes there are two flavours (a frame with a market context and without) of six blocks of eight conditions, each block over a series of its own: whole, gappy (with duplicate and
/// out-of-order bars that the frame skips) or sparse sessions over days, weeks or months, with a holiday now and then, prices on a tick so that ties are frequent, zero volumes, flat bars, gaps and spikes. The frame keeps
/// more history than the conditions need in every third block. The answers are also compared before the first bar, and again after <c>Reset</c> over the start of the series. Besides the answers, the compiler's plan is
/// checked against what the model reads (the features, the history depth, the needs of the frame), so the binder is covered as well.
/// </para>
/// </summary>
public sealed class InterpreterOracleTests
{
    private const int Flavours = 2;

    private const int Blocks = 6;

    private const int ConditionsPerBlock = 8;

    public static TheoryData<Timeframe> Evaluations { get; } = [.. Timeframes.All];

    private sealed record Mismatch(string Source, long Step, string Expected, string Actual);

    /// <summary>The counters of a run and the differences found.</summary>
    private sealed class Tally
    {
        public List<Mismatch> Mismatches { get; } = [];

        public Dictionary<Tri, int> All { get; } = new() { [Tri.True] = 0, [Tri.False] = 0, [Tri.Unknown] = 0 };

        public Dictionary<Tri, int> Closed { get; } = new() { [Tri.True] = 0, [Tri.False] = 0, [Tri.Unknown] = 0 };

        public int Comparisons { get; private set; }

        public int ClosedBars { get; private set; }

        /// <summary>Compares every condition with the interpreter on the frame as it is now.</summary>
        public void Compare(Block run, FeatureFrame frame, Interpreter interpreter, BoundTreeInterpreter boundTreeInterpreter, IReadOnlyList<BoundCondition> bound)
        {
            var closed = frame.Steps > 0 && frame.BarClosed(run.Evaluation);
            ClosedBars += closed ? 1 : 0;
            for (var k = 0; k < run.Models.Count; k++)
            {
                var expected = interpreter.Evaluate(run.Models[k]);
                var truth = bound[k].EvaluateTruth(frame);
                var decision = bound[k].Evaluate(frame);
                var fromBoundTree = boundTreeInterpreter.Evaluate(run.Compiled[k].Tree);
                Comparisons++;
                All[expected]++;
                if (closed)
                {
                    Closed[expected]++;
                }

                if (Of(truth) != expected || decision != (expected == Tri.True))
                {
                    Mismatches.Add(new Mismatch(run.Sources[k], frame.Steps, expected.ToString(), truth + " / decision " + decision + (fromBoundTree == expected ? " (the bound tree agrees with the model: the compiler is at fault)" : " (the bound tree says " + fromBoundTree + ")")));
                }
                else if (fromBoundTree != expected)
                {
                    Mismatches.Add(new Mismatch(run.Sources[k], frame.Steps, expected.ToString(), "the bound tree says " + fromBoundTree + " (the binder is at fault)"));
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Evaluations))]
    public void The_compiled_condition_equals_the_interpreter_at_every_step_on_random_frames(Timeframe evaluation)
    {
        var rejected = new List<string>();
        var planDifferences = new List<string>();
        var tally = new Tally();
        var conditions = 0;

        for (var flavour = 0; flavour < Flavours; flavour++)
        {
            for (var block = 0; block < Blocks; block++)
            {
                var seed = 700_000 + (int)evaluation * 1_000 + flavour * 100 + block;
                var run = Prepare(seed, evaluation, withContext: flavour == 0, block, rejected);
                conditions += run.Models.Count;
                if (run.Plan is null)
                {
                    continue;
                }

                CheckPlans(run, planDifferences);
                if (planDifferences.Count > 0)
                {
                    continue;
                }

                var (frame, bound) = run.NewFrame();
                var interpreter = new Interpreter(frame);
                var boundTreeInterpreter = new BoundTreeInterpreter(frame);
                tally.Compare(run, frame, interpreter, boundTreeInterpreter, bound);
                foreach (var bar in run.Bars)
                {
                    frame.Step(bar);
                    tally.Compare(run, frame, interpreter, boundTreeInterpreter, bound);
                }

                // the same bound conditions on the same frame after a reset: nothing of the first run may linger
                frame.Reset();
                tally.Compare(run, frame, interpreter, boundTreeInterpreter, bound);
                foreach (var bar in run.Bars.Take(150))
                {
                    frame.Step(bar);
                    tally.Compare(run, frame, interpreter, boundTreeInterpreter, bound);
                }
            }
        }

        rejected.ShouldBeEmpty("the compiler refused conditions of the language:\n" + string.Join("\n", rejected));
        planDifferences.ShouldBeEmpty("the plan differs from what the model reads:\n" + string.Join("\n", planDifferences));
        tally.Mismatches.ShouldBeEmpty(Describe(tally.Mismatches));
        conditions.ShouldBe(Flavours * Blocks * ConditionsPerBlock);
        tally.ClosedBars.ShouldBeGreaterThan(Flavours * Blocks, "the frames closed hardly any bar of " + evaluation);
        tally.Comparisons.ShouldBeGreaterThan(Flavours * Blocks * ConditionsPerBlock * 20);
        tally.All[Tri.True].ShouldBeGreaterThan(0, evaluation + ": no condition was ever true");
        tally.All[Tri.False].ShouldBeGreaterThan(0, evaluation + ": no condition was ever false");
        tally.All[Tri.Unknown].ShouldBeGreaterThan(0, evaluation + ": no condition was ever unknown");
        tally.Closed[Tri.True].ShouldBeGreaterThan(0, evaluation + ": no condition was true at a closed bar");
        tally.Closed[Tri.False].ShouldBeGreaterThan(0, evaluation + ": no condition was false at a closed bar");
    }

    [Fact]
    public void The_corpus_has_at_least_200_conditions_per_the_spec()
    {
        (Timeframes.All.Count * Flavours * Blocks * ConditionsPerBlock).ShouldBeGreaterThanOrEqualTo(200);
    }

    [Fact]
    public void The_same_seed_gives_the_same_conditions_and_the_same_series()
    {
        var first = new ModelGenerator(5, Timeframe.M15, true, true);
        var second = new ModelGenerator(5, Timeframe.M15, true, true);

        var a = Enumerable.Range(0, 20).Select(_ => Printer.Print(first.Next())).ToList();
        var b = Enumerable.Range(0, 20).Select(_ => Printer.Print(second.Next())).ToList();

        b.ShouldBe(a);
        OracleSeries.For(11, Timeframe.M15).ShouldBe(OracleSeries.For(11, Timeframe.M15));
    }

    [Fact]
    public void The_gappy_series_contain_bars_the_frame_skips_and_the_oracle_agrees_over_them()
    {
        var seed = 1; // seed % 3 == 1 is the gappy shape
        var bars = OracleSeries.For(seed, Timeframe.M5);
        var scenario = Scenario.Create(Timeframe.M5, ["close > close[1]", "time >= \"10:00\"", "rsi(3) > 50 or day_of_week == WED"]);
        var (frame, bound) = scenario.NewFrame();
        var interpreter = new Interpreter(frame);

        foreach (var bar in bars)
        {
            frame.Step(bar);
            for (var k = 0; k < bound.Count; k++)
            {
                Of(bound[k].EvaluateTruth(frame)).ShouldBe(interpreter.Evaluate(Models[k]), scenario.Sources[k] + " at step " + frame.Steps);
            }
        }

        frame.IgnoredBars.ShouldBeGreaterThan(0, "the series had no bar for the ordering guard to skip");
        frame.Steps.ShouldBe(bars.Length - frame.IgnoredBars);
    }

    /// <summary>The three conditions of the gappy test as models.</summary>
    private static readonly Node[] Models =
    [
        new Cmp(">", Close(0), Close(1)),
        new TimeCmp(">=", 600, "10:00", false),
        new Logic(false, new Cmp(">", new Leaf(new Feature("rsi(3)", "rsi(3)", false, new FeatureSpec("rsi", Timeframe.M5, PriceSource.Default, 3)), Timeframe.M5, false, 0), new Lit("50")), new Cmp("==", new Leaf(new Feature("day_of_week", "day_of_week", true, new FeatureSpec("day_of_week")), Timeframe.M1, false, 0), new Const("WED", 3))),
    ];

    private static Leaf Close(int lag) => new(new Feature("close", "close", true, new FeatureSpec("close")), Timeframe.M5, false, lag);

    [Theory]
    [InlineData(Timeframe.M1)]
    [InlineData(Timeframe.M15)]
    [InlineData(Timeframe.D1)]
    public void Subscripts_and_runs_up_to_the_frame_limit_agree_with_the_interpreter_with_and_without_history(Timeframe evaluation)
    {
        var close = new Feature("close", "close", true, new FeatureSpec("close"));
        var open = new Feature("open", "open", true, new FeatureSpec("open"));
        Node[] models =
        [
            new Cmp(">", new Leaf(close, evaluation, false, 255), new Lit("0")),
            new Cmp("<", new Leaf(close, evaluation, false, 254), new Leaf(open, evaluation, false, 255)),
            new Streak(true, new Leaf(close, evaluation, false, 0), 255),
            new Streak(false, new Leaf(close, evaluation, false, 0), 255),
            new Streak(true, new Leaf(open, evaluation, false, 0), 254),
            new Crosses(true, new Leaf(close, evaluation, false, 254), new Leaf(open, evaluation, false, 254)),
            new Between(new Leaf(close, evaluation, false, 255), new Lit("0"), new Lit("1e308")),
        ];
        var sources = models.Select(m => Printer.Print(m)).ToList();
        var features = models.SelectMany(m => ModelFacts.Of(m).Features).Append(Specs.Close(evaluation)).Distinct().ToList();
        var scenario = Scenario.Create(evaluation, sources, features: features, historyDepth: 256);
        scenario.Plan.HistoryDepth.ShouldBe(256);
        var (frame, bound) = scenario.NewFrame();
        var interpreter = new Interpreter(frame);
        var bars = evaluation switch
        {
            Timeframe.M1 => Series.Sessions(21, 2),
            Timeframe.M15 => Series.Sessions(22, 12),
            _ => Series.Sparse(23, 330),
        };
        var counts = new Dictionary<Tri, int> { [Tri.True] = 0, [Tri.False] = 0, [Tri.Unknown] = 0 };

        foreach (var bar in bars)
        {
            frame.Step(bar);
            for (var k = 0; k < models.Length; k++)
            {
                var expected = interpreter.Evaluate(models[k]);
                Of(bound[k].EvaluateTruth(frame)).ShouldBe(expected, $"{sources[k]} at step {frame.Steps}");
                bound[k].Evaluate(frame).ShouldBe(expected == Tri.True);
                counts[expected]++;
            }
        }

        counts[Tri.Unknown].ShouldBeGreaterThan(0, "the history was always long enough");
        (counts[Tri.True] + counts[Tri.False]).ShouldBeGreaterThan(0, "the history was never long enough");
    }

    // ---- one block ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    private sealed class Block(Timeframe evaluation, IReadOnlyList<Node> models, IReadOnlyList<string> sources, Bar[] bars, CompiledPlan? plan, IReadOnlyList<CompiledCondition> compiled, IReadOnlyList<Facts> facts, FrameOptions options, IReadOnlyList<FeatureSpec> specs)
    {
        public Timeframe Evaluation => evaluation;

        public IReadOnlyList<Node> Models => models;

        public IReadOnlyList<string> Sources => sources;

        public Bar[] Bars => bars;

        public CompiledPlan? Plan => plan;

        public IReadOnlyList<CompiledCondition> Compiled => compiled;

        public IReadOnlyList<Facts> Facts => facts;

        public (FeatureFrame Frame, IReadOnlyList<BoundCondition> Bound) NewFrame()
        {
            var created = FeatureFrame.Create(specs, options);
            created.IsSuccess.ShouldBeTrue(created.IsFailure ? created.Error.Message + " " + string.Join("; ", created.Error.Fields?.Select(f => f.Field + ": " + f.Message) ?? []) : string.Empty);
            return (created.Value, plan!.BindAll(created.Value));
        }
    }

    /// <summary>Generates the models, prints and compiles them (what the compiler refuses is added to <paramref name="rejected"/>), and makes the frame from what the <i>models</i> read, not from the plan.</summary>
    private static Block Prepare(int seed, Timeframe evaluation, bool withContext, int block, List<string> rejected)
    {
        var generator = new ModelGenerator(seed, evaluation, allowContext: withContext, allowExpiryDays: block % 2 == 0);
        var models = Enumerable.Range(0, ConditionsPerBlock).Select(_ => generator.Next()).ToList();
        var sources = models.Select((m, i) => Printer.Print(m, (i + block) % 2 == 0 ? Parens.Always : Parens.Minimal, (Spacing)(((i + block) / 2) % 3))).ToList();
        var facts = models.Select(ModelFacts.Of).ToList();
        var bars = OracleSeries.For(seed, evaluation);

        var compiled = new List<CompiledCondition>();
        for (var i = 0; i < sources.Count; i++)
        {
            var result = Compiling.Of(sources[i], evaluation, ModelGenerator.Parameters);
            if (result.IsSuccess)
            {
                compiled.Add(result.Condition!);
            }
            else
            {
                rejected.Add($"[{evaluation}] {sources[i].Replace("\n", "\\n").Replace("\t", "\\t")} -> {Compiling.Describe(result)}");
            }
        }

        if (compiled.Count != sources.Count)
        {
            return new Block(evaluation, models, sources, bars, null, compiled, facts, new FrameOptions(), []);
        }

        var specs = facts.SelectMany(f => f.Features).Append(Specs.Close(evaluation)).Distinct().ToList();
        var depth = Math.Min(FrameOptions.MaxHistoryDepth, facts.Max(f => f.HistoryDepth) + (block % 3 == 0 ? 7 : 0));
        var options = new FrameOptions(null, depth, withContext ? OracleContext.For(seed, bars) : null, block % 2 == 0 ? OracleContext.ExpiryDays(seed) : null);
        return new Block(evaluation, models, sources, bars, ConditionSet.Merge(compiled), compiled, facts, options, specs);
    }

    /// <summary>The plan of each condition against the facts of its model: the features (as the frame names them), the depth and the needs.</summary>
    private static void CheckPlans(Block run, List<string> differences)
    {
        var (frame, _) = run.NewFrame();
        for (var k = 0; k < run.Models.Count; k++)
        {
            var facts = run.Facts[k];
            var condition = run.Compiled[k];
            var expected = facts.Features.Select(f => frame.SpecOf(frame.HandleOf(f))).ToHashSet();
            var actual = condition.Features.ToHashSet();
            if (!expected.SetEquals(actual) || condition.Features.Count != actual.Count)
            {
                differences.Add($"`{run.Sources[k]}`: features {string.Join(", ", condition.Features)} instead of {string.Join(", ", expected)}");
            }

            if (condition.HistoryDepth != facts.HistoryDepth)
            {
                differences.Add($"`{run.Sources[k]}`: history depth {condition.HistoryDepth} instead of {facts.HistoryDepth}");
            }

            if (condition.NeedsContext != facts.NeedsContext || condition.NeedsExpiryDays != facts.NeedsExpiryDays)
            {
                differences.Add($"`{run.Sources[k]}`: needs context {condition.NeedsContext}, expiry days {condition.NeedsExpiryDays} instead of {facts.NeedsContext}, {facts.NeedsExpiryDays}");
            }
        }
    }

    private static Tri Of(Truth truth) => truth switch
    {
        Truth.True => Tri.True,
        Truth.False => Tri.False,
        Truth.Unknown => Tri.Unknown,
        _ => throw new InvalidOperationException("Unexpected truth " + (int)truth),
    };

    private static string Describe(List<Mismatch> mismatches)
    {
        var text = new StringBuilder();
        text.AppendLine($"{mismatches.Count} differences between the compiled condition and the interpreter; the first ones:");
        foreach (var m in mismatches.Take(10))
        {
            text.AppendLine($"  step {m.Step}: `{m.Source.Replace("\n", "\\n").Replace("\t", "\\t")}` interpreter {m.Expected}, compiled {m.Actual}");
        }

        return text.ToString();
    }
}
