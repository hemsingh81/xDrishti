using XDrishti.Domain.Market.Bars;
using XDrishti.Strategies.Mirror;
using XDrishti.Strategies.Syntax;
using XDrishti.Strategies.Tests.Support;
using static XDrishti.Strategies.Tests.Mirror.MirrorTestSupport;

namespace XDrishti.Strategies.Tests.Mirror;

/// <summary>Spec P3-02e criterion 2: mirroring twice gives back the tree, over at least 500 conditions the table accepts; a refusal is identical on repeat.</summary>
public sealed class MirrorInvolutionTests
{
    private static readonly string[] Frames = ["5m", "15m", "1h"];

    private static IEnumerable<string> Corpus()
    {
        foreach (var row in ConditionMirrorExamplesTests.Examples)
        {
            yield return row.Data.Item1;
            yield return row.Data.Item2;
        }

        foreach (var row in ConditionMirrorRefusalTests.NearMisses)
        {
            yield return row.Data;
        }

        string[] levels = ["close", "open", "high[1]", "low[2]", "vwap", "ema(20)", "sma(10, source=high)", "bb_upper(20, 2)", "prev_day.low", "today.open", "highest(5)", "tf(\"1h\").close", "or_low(60)", "supertrend(10, 3)"];
        string[] operators = ["<", "<=", ">", ">=", "==", "!="];
        foreach (var a in levels)
        {
            foreach (var b in levels)
            {
                foreach (var op in operators)
                {
                    yield return a + " " + op + " " + b;
                }
            }
        }

        foreach (var op in operators)
        {
            foreach (var number in new[] { "30", "55.1", "70", "rsi_hi", "0.5", "100 - rsi_hi" })
            {
                yield return "rsi(14) " + op + " " + number;
                yield return "rsi(2)[1] " + op + " " + number + " and crosses_above(rsi(14), " + number + ")";
            }

            foreach (var number in new[] { "0", "1", "-1", "gap_min", "-gap_max", "2.5" })
            {
                yield return "gap_pct " + op + " " + number;
                yield return "macd_hist[1] " + op + " " + number + " or engulfing " + op + " " + number;
            }

            foreach (var number in new[] { "0", "20", "adx_max", "1.5" })
            {
                yield return "atr(14) " + op + " " + number + " and rel_volume(10) " + op + " " + number;
                yield return "plus_di(14) " + op + " minus_di(14) + " + number;
            }
        }

        foreach (var timeframe in new[] { Timeframe.M5, Timeframe.M15, Timeframe.H1 })
        {
            for (var seed = 1; seed <= 12; seed++)
            {
                var generator = new ValidConditionGenerator(seed * 7919 + (int)timeframe, timeframe);
                for (var i = 0; i < 25; i++)
                {
                    yield return generator.Next().Source;
                }
            }
        }
    }

    private static IReadOnlySet<string> Names => new HashSet<string>(Parameters.Concat(ValidConditionGenerator.Parameters.Keys), StringComparer.Ordinal);

    [Fact]
    public void Mirroring_twice_gives_back_the_condition_for_every_accepted_text()
    {
        var accepted = 0;
        var broken = new List<string>();
        foreach (var source in Corpus().Distinct())
        {
            var tree = ConditionParser.Parse(source).Tree;
            var first = ConditionMirror.Mirror(source, Names);
            if (tree is null || !first.IsSuccess)
            {
                continue;
            }

            accepted++;
            var second = ConditionMirror.Mirror(first.Tree!, Names);
            if (!second.IsSuccess || !second.Tree!.Equals(tree))
            {
                broken.Add(source + "  ->  " + first.Text + "  ->  " + second.Text + " " + second.Problem?.Message);
            }
        }

        broken.ShouldBeEmpty(string.Join("\n", broken));
        accepted.ShouldBeGreaterThanOrEqualTo(500);
    }

    [Fact]
    public void A_refused_text_gives_the_same_problem_on_repeat()
    {
        var refused = Corpus().Distinct().Select(s => (s, ConditionMirror.Mirror(s, Names))).Where(r => r.Item2.Problem is not null).ToList();

        refused.Count.ShouldBeGreaterThan(20);
        foreach (var (source, first) in refused)
        {
            ConditionMirror.Mirror(source, Names).Problem.ShouldBe(first.Problem, source);
        }
    }

    [Fact]
    public void The_frames_are_the_three_the_property_runs_on()
    {
        Frames.Length.ShouldBe(3);
    }
}
