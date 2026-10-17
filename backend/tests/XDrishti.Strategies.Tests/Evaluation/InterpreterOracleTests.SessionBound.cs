using XDrishti.Domain.Market.Bars;
using XDrishti.Strategies.Tests.Support.Oracle;

namespace XDrishti.Strategies.Tests.Evaluation;

/// <summary>
/// The session-bound corpus of the oracle (ADR 0035): conditions whose feature leaves are all among the twelve per-session features, on every intraday evaluation timeframe, over dense, gappy (duplicate and out-of-order bars
/// the frame skips) and sparse (a few bars a session: double closes, late closes, pending lanes everywhere) series. The interpreters read those leaves from the <see cref="SessionReference"/> — the definition of the rule,
/// which has no clearing and no pending flag — and the compiled conditions must agree at every 1-minute step. Counters prove the rule is exercised (reads that the rule turned unknown, by the history of the session and by
/// a pending lane; a condition that was unknown at the first evaluation of a date and true later in it), and the run-on control — the reference with the mask taken off — must disagree.
/// </summary>
public sealed partial class InterpreterOracleTests
{
    private const int SessionRounds = 2;

    public static TheoryData<Timeframe> IntradayEvaluations { get; } = [Timeframe.M1, Timeframe.M5, Timeframe.M10, Timeframe.M15, Timeframe.M30, Timeframe.H1];

    /// <summary>Seeds with <c>seed % 3</c> the shape of the series (<see cref="OracleSeries.For"/>): 0 dense, 1 gappy, 2 sparse.</summary>
    private static int SessionSeedOf(Timeframe evaluation, int round, SeriesShape shape) => 3 * (300_000 + (int)evaluation * 100 + round * 10) + (int)shape;

    /// <summary>What a pass over the session-bound blocks of one evaluation timeframe saw.</summary>
    private sealed record SessionPass(int Conditions, int Comparisons, Tally Tally, long MaskedByHistory, long MaskedByPending, int TrueAfterUnknownFirst, List<string> Rejected, List<string> PlanDifferences);

    private static SessionPass RunSessionBlocks(Timeframe evaluation, bool runOn)
    {
        var rejected = new List<string>();
        var planDifferences = new List<string>();
        var tally = new Tally();
        var conditions = 0;
        long history = 0;
        long pending = 0;
        var trueAfterUnknown = 0;
        var block = 0;
        foreach (var round in Enumerable.Range(0, SessionRounds))
        {
            foreach (var shape in Enum.GetValues<SeriesShape>())
            {
                var run = Prepare(SessionSeedOf(evaluation, round, shape), evaluation, withContext: false, block++, rejected, sessionBound: true);
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
                var sessions = SessionReference.For(frame, runOn: runOn);
                var interpreter = new Interpreter(frame, sessions);
                var boundTreeInterpreter = new BoundTreeInterpreter(frame, sessions);
                var firstOfDate = new Tri?[run.Models.Count];
                DateOnly? lastDate = null;
                tally.Compare(run, frame, interpreter, boundTreeInterpreter, bound);
                foreach (var bar in run.Bars)
                {
                    frame.Step(bar);
                    sessions.Feed(bar);
                    tally.Compare(run, frame, interpreter, boundTreeInterpreter, bound);
                    if (!frame.BarClosed(evaluation))
                    {
                        continue;
                    }

                    var date = SessionClock.DateOf(frame.LastBarTime);
                    if (date != lastDate)
                    {
                        Array.Fill(firstOfDate, null);
                        lastDate = date;
                    }

                    for (var k = 0; k < run.Models.Count; k++)
                    {
                        var now = interpreter.Evaluate(run.Models[k]);
                        if (firstOfDate[k] is null)
                        {
                            firstOfDate[k] = now;
                        }
                        else if (firstOfDate[k] == Tri.Unknown && now == Tri.True)
                        {
                            trueAfterUnknown++;
                            firstOfDate[k] = Tri.False;
                        }
                    }
                }

                history += sessions.Masked.History;
                pending += sessions.Masked.Pending;
            }
        }

        return new SessionPass(conditions, tally.Comparisons, tally, history, pending, trueAfterUnknown, rejected, planDifferences);
    }

    [Theory]
    [MemberData(nameof(IntradayEvaluations))]
    public void The_session_bound_corpus_equals_the_interpreter_at_every_step_and_the_rule_is_exercised(Timeframe evaluation)
    {
        var pass = RunSessionBlocks(evaluation, runOn: false);

        pass.Rejected.ShouldBeEmpty("the compiler refused conditions of the language:\n" + string.Join("\n", pass.Rejected));
        pass.PlanDifferences.ShouldBeEmpty("the plan differs from what the model reads:\n" + string.Join("\n", pass.PlanDifferences));
        pass.Tally.Mismatches.ShouldBeEmpty(Describe(pass.Tally.Mismatches));
        pass.Conditions.ShouldBe(SessionRounds * 3 * ConditionsPerBlock);
        pass.Comparisons.ShouldBeGreaterThan(pass.Conditions * 20);
        pass.Tally.All[Tri.True].ShouldBeGreaterThan(0, evaluation + ": no condition was ever true");
        pass.Tally.All[Tri.False].ShouldBeGreaterThan(0, evaluation + ": no condition was ever false");
        pass.Tally.All[Tri.Unknown].ShouldBeGreaterThan(0, evaluation + ": no condition was ever unknown");

        // the rule was exercised: reads the history of the session turned unknown, reads a pending lane turned unknown, and conditions that were unknown at the first evaluation of a date were true later in it
        pass.MaskedByHistory.ShouldBeGreaterThan(MinimumMasked, evaluation + ": reads turned unknown because they reached before the first bar of the session");
        pass.MaskedByPending.ShouldBeGreaterThan(MinimumMasked, evaluation + ": reads turned unknown because the lane had no bar of the date yet");
        pass.TrueAfterUnknownFirst.ShouldBeGreaterThan(MinimumTrueAfterUnknown, evaluation + ": too few conditions were unknown at the first evaluation of a date and true later in it");
    }

    [Theory]
    [MemberData(nameof(IntradayEvaluations))]
    public void The_run_on_control_the_reference_without_its_mask_disagrees_with_the_compiled_conditions(Timeframe evaluation)
    {
        var pass = RunSessionBlocks(evaluation, runOn: true);

        pass.Tally.Mismatches.Count.ShouldBeGreaterThan(MinimumRunOnDisagreements, evaluation + ": the positional reading of the old frame is no longer told from the new rule by the corpus");
    }

    // Bounds set from a measured run of the 48 conditions per timeframe over 6 series (about 32,000 comparisons each). Honest run, smallest figure over the six timeframes: reads turned unknown by the history of the
    // session 10,877 (1m), by a pending lane 5,382 (1m), conditions true after an unknown first evaluation 36 (1h); run-on control, disagreements with the compiled conditions: 822 (1m), 1,229 (5m), 1,519 (30m),
    // 1,560 (15m), 1,634 (10m), 2,751 (1h). The bounds are about half of the smallest.
    private const int MinimumMasked = 2_500;

    private const int MinimumTrueAfterUnknown = 15;

    private const int MinimumRunOnDisagreements = 400;
}
