using System.Diagnostics;
using System.Globalization;
using XDrishti.Domain.Common;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Simulation.Walking;

namespace XDrishti.Simulation.Tests;

/// <summary>
/// Criterion 12 of P4-03: a trail walk allocates the trade and nothing per visited minute. After one warm-up walk, a position that is stopped out on its 21st minute and one that lives 330 minutes and reads 22 bars of the tape allocate the same
/// number of bytes, for each of the three rules. The probe is the one of <see cref="WalkAllocationTests"/>. A throughput benchmark of the trails is explicit and reports only.
/// </summary>
[Collection(ExclusiveRun.Name)]
public sealed class TrailAllocationTests
{
    private const int Walks = 200;

    private static TrailBar[] LongTape(double ema) =>
        [.. Enumerable.Range(0, 22).Select(i => new TrailBar(Day.Clock(600 + (15 * i)), 100.60, ema))];

    private static TrailRow Row(bool shortLived) => new(
        shortLived ? "short" : "long",
        Side.Buy,
        shortLived ? ["09:50 100.00/100.60/100.00/100.60", "10:05 100.60/100.60/98.50/98.60"] : ["09:50 100.00/100.60/100.00/100.60"],
        LongTape(ema: 100.00),
        new TrailWant(ExitReason.Stop, "10:05", 99.00, 0, 0, 0));

    public static TheoryData<string> Rules { get; } = ["atr", "structure", "ema"];

    private static IExitRule RuleOf(string name) => name switch
    {
        "atr" => new AtrTrailRule(0, 2m, 0m),
        "structure" => new StructureTrailRule(0),
        _ => new EmaTrailRule(0),
    };

    [Theory]
    [MemberData(nameof(Rules))]
    public void A_position_stopped_on_its_21st_minute_allocates_as_much_as_one_that_lives_330_minutes(string name)
    {
        var rule = RuleOf(name);
        _ = Walk(Row(true), rule);
        _ = Walk(Row(false), rule);

        var (shortBytes, shortDisturbed, _) = Measure(Row(true), rule);
        var (longBytes, longDisturbed, _) = Measure(Row(false), rule);

        shortBytes.ShouldBeGreaterThan(0, "the result objects are allocated");
        longBytes.ShouldBe(shortBytes, shortDisturbed || longDisturbed ? "a garbage collection disturbed a measurement" : "nothing is allocated per visited minute or per bar read");
    }

    private static Result<WalkOutcome> Walk(TrailRow row, IExitRule rule) => TrailHand.Walk(row, rule);

    private static (long Allocated, bool Disturbed, int Attempts) Measure(TrailRow row, IExitRule rule)
    {
        var bars = TrailHand.Bars(row);
        var tape = TrailHand.TapeOf(row.Tape);
        var candidate = TrailHand.Candidate(row.Side);
        var rules = new WalkRules(null, 915, Cand.NoSlippage, Cand.Tick05);
        var context = new ExitContext(rule, tape);
        return AllocationProbe.Measure(window =>
        {
            Result<WalkOutcome>? last = null;
            window.Begin();
            for (var i = 0; i < Walks; i++)
            {
                last = TradeWalker.Walk(candidate, bars, rules, context);
            }

            window.End();
            last!.IsSuccess.ShouldBeTrue();
        });
    }

    /// <summary>
    /// An explicit benchmark, not part of <c>scripts/check.sh</c>: the walks of the FIXED_RR path and of the three trails over the same 250 seeded sessions; reports walks and minutes per second, asserts nothing.
    /// <c>dotnet test --project tests/XDrishti.Simulation.Tests -c Release -- --explicit only --filter-method "*Trail_walk_throughput*"</c>.
    /// </summary>
    [Fact(Explicit = true)]
    public void Trail_walk_throughput_is_reported()
    {
        var bars = new List<Domain.Market.Bars.Bar>();
        var random = new Random(2026);
        var price = 100.0;
        for (var s = 0; s < 250; s++)
        {
            var date = DayBuilder.Monday.AddDays((s / 5 * 7) + (s % 5));
            for (var minute = Domain.Market.Bars.SessionClock.OpenMinute; minute < Domain.Market.Bars.SessionClock.CloseMinute; minute++)
            {
                var open = price;
                price *= 1 + ((random.NextDouble() - 0.5) * 0.001);
                bars.Add(new Domain.Market.Bars.Bar(DayBuilder.Epoch(date, minute), open, Math.Max(open, price) * 1.0002, Math.Min(open, price) * 0.9998, price, 1_000));
            }
        }

        var candidates = new List<EntryCandidate>();
        for (var s = 0; s < 250; s++)
        {
            var index = (s * 375) + 30;
            candidates.Add(new EntryCandidate("ORB", Cand.Test, Side.Buy, index, bars[index].EpochSeconds + 60, Domain.Market.Bars.Timeframe.M15, bars[index], 15, true, bars[index].Close * 0.995, DecisionTrace.Empty));
        }

        foreach (var kind in new[] { OracleTrailKind.Atr, OracleTrailKind.Structure, OracleTrailKind.Ema })
        {
            var built = TrailTape.Of(bars, kind, Side.Buy, 14, 2.0, 0);
            var rules = Cand.Standard with { RewardRisk = null };
            long minutes = 0;
            var seconds = Median(() =>
            {
                minutes = 0;
                foreach (var candidate in candidates)
                {
                    minutes += TradeWalker.Walk(candidate, bars, rules, built.Context).Value.BarsExamined;
                }
            });
            TestContext.Current.TestOutputHelper?.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{kind}: walks {candidates.Count} minutes visited {minutes}, median {seconds * 1000:F2} ms: {candidates.Count / seconds:F0} walks/s, {minutes / seconds:F0} minutes/s"));
        }

        var fixedSeconds = Median(() =>
        {
            foreach (var candidate in candidates)
            {
                _ = TradeWalker.Walk(candidate, bars, Cand.Standard).Value.BarsExamined;
            }
        });
        TestContext.Current.TestOutputHelper?.WriteLine(string.Create(CultureInfo.InvariantCulture, $"FIXED_RR: median {fixedSeconds * 1000:F2} ms: {candidates.Count / fixedSeconds:F0} walks/s"));
    }

    private static double Median(Action run)
    {
        for (var warm = Stopwatch.StartNew(); warm.Elapsed < TimeSpan.FromSeconds(1);)
        {
            run();
        }

        var times = new List<double>();
        for (var i = 0; i < 9; i++)
        {
            var watch = Stopwatch.StartNew();
            run();
            times.Add(watch.Elapsed.TotalSeconds);
        }

        times.Sort();
        return times[4];
    }
}
