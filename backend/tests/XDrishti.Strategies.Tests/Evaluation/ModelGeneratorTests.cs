using System.Text.RegularExpressions;
using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators;
using XDrishti.Strategies.Tests.Support;
using XDrishti.Strategies.Tests.Support.Oracle;

namespace XDrishti.Strategies.Tests.Evaluation;

/// <summary>
/// The oracle and the look-ahead property are only as good as the conditions they are given, so the generator is checked on its own: every condition it writes is accepted by the compiler on every evaluation timeframe,
/// every construct of the language occurs in what it writes (every builtin, the lagged forms, <c>tf(…)</c>, the parameters, the dotted spellings, <c>time</c> in both orders, <c>regime()</c> by name and by code,
/// <c>day_of_week</c> with the weekday names, the defaults left out, <c>source=</c>, a division by zero, equality of truth values), all 56 catalog names are read, and a generator told to leave the context out
/// never reads it.
/// </summary>
public sealed class ModelGeneratorTests
{
    private const int PerTimeframe = 120;

    public static TheoryData<Timeframe> Evaluations { get; } = [.. Timeframes.All];

    [Theory]
    [MemberData(nameof(Evaluations))]
    public void Every_generated_condition_is_accepted_by_the_compiler(Timeframe evaluation)
    {
        var refused = new List<string>();
        foreach (var (withContext, seed) in new[] { (true, 11), (false, 12) })
        {
            var generator = new ModelGenerator(seed * 1_000 + (int)evaluation, evaluation, withContext, withContext);
            for (var i = 0; i < PerTimeframe; i++)
            {
                var model = generator.Next();
                var source = Printer.Print(model, i % 2 == 0 ? Parens.Always : Parens.Minimal, (Spacing)(i / 2 % 3));
                var result = Compiling.Of(source, evaluation, ModelGenerator.Parameters);
                if (!result.IsSuccess)
                {
                    refused.Add(source.Replace("\n", "\\n").Replace("\t", "\\t") + " -> " + Compiling.Describe(result));
                }
            }
        }

        refused.ShouldBeEmpty(refused.Count + " of the generated conditions were refused:\n" + string.Join("\n", refused.Take(8)));
    }

    [Theory]
    [MemberData(nameof(Evaluations))]
    public void A_generator_without_a_context_never_reads_the_context_and_without_expiry_dates_never_reads_them(Timeframe evaluation)
    {
        var generator = new ModelGenerator(77 + (int)evaluation, evaluation, allowContext: false, allowExpiryDays: false);

        var facts = Enumerable.Range(0, 300).Select(_ => ModelFacts.Of(generator.Next())).ToList();

        facts.Any(f => f.NeedsContext).ShouldBeFalse();
        facts.Any(f => f.NeedsExpiryDays).ShouldBeFalse();
        facts.SelectMany(f => f.Features).Any(s => IndicatorCatalog.TryGet(s.Name, out var info) && (info.RequiresContext || info.RequiresExpiryDays)).ShouldBeFalse();
    }

    /// <summary>
    /// The calendar and context features change once a date: their previous value is the previous <i>date</i>, never the previous minute, so a lagged call may read them only among themselves (and constants) and a bar
    /// feature only among bar features of one timeframe — on 1m too (ADR 0034, Amendment 4). The generator must keep that, or the oracle would bless a mix whose "previous value" is two different moments; and it must
    /// write lagged calls over the date alone, so that the lag of the date lane is compared with the interpreter.
    /// </summary>
    [Theory]
    [MemberData(nameof(Evaluations))]
    public void A_lagged_call_never_mixes_the_date_with_a_bar_feature_or_two_bar_timeframes_and_lagged_calls_over_the_date_alone_do_occur(Timeframe evaluation)
    {
        var generator = new ModelGenerator(31 + (int)evaluation, evaluation, allowContext: true, allowExpiryDays: true);
        var mixed = new List<string>();
        var dateOnly = 0;
        var barOnly = 0;
        for (var i = 0; i < 2_000; i++)
        {
            var model = generator.Next();
            foreach (var call in ModelTree.Descendants(model).Where(n => n is Crosses or Streak))
            {
                var operands = ModelTree.Children(call).SelectMany(ModelTree.Leaves).ToList();
                var date = operands.Count(IsOfTheDate);
                var clocks = operands.Where(l => !IsOfTheDate(l)).Select(l => l.Frame).Distinct().Count();
                if ((date > 0 && date < operands.Count) || clocks > 1)
                {
                    mixed.Add(Printer.Print(call, Parens.Always, Spacing.Normal));
                }

                dateOnly += date > 0 && date == operands.Count ? 1 : 0;
                barOnly += date == 0 && operands.Count > 0 ? 1 : 0;
            }
        }

        mixed.ShouldBeEmpty("lagged calls that mix clocks: " + string.Join("; ", mixed.Take(5)));
        dateOnly.ShouldBeGreaterThan(30, evaluation + " hardly ever writes a lagged call over the date alone");
        barOnly.ShouldBeGreaterThan(200, evaluation + " hardly ever writes a lagged call over bars");
    }

    private static bool IsOfTheDate(Leaf leaf) => IndicatorCatalog.TryGet(leaf.Feature.Spec.Name, out var info) && info.DateDriven;

    [Fact]
    public void Time_is_only_written_for_an_intraday_evaluation_timeframe()
    {
        foreach (var evaluation in new[] { Timeframe.D1, Timeframe.W1, Timeframe.Mo1 })
        {
            var generator = new ModelGenerator(5, evaluation, true, true);
            Enumerable.Range(0, 400).Any(_ => ModelFacts.Of(generator.Next()).UsesTime).ShouldBeFalse(evaluation + " used time");
        }

        foreach (var evaluation in new[] { Timeframe.M1, Timeframe.M5, Timeframe.M10, Timeframe.M15, Timeframe.M30, Timeframe.H1 })
        {
            var generator = new ModelGenerator(5, evaluation, true, true);
            Enumerable.Range(0, 400).Any(_ => ModelFacts.Of(generator.Next()).UsesTime).ShouldBeTrue(evaluation + " never used time");
        }
    }

    private static readonly Lazy<List<string>> Sources = new(Corpus);

    /// <summary>The text every construct is looked for in: bracketed, normal spacing.</summary>
    private static List<string> Corpus()
    {
        var sources = new List<string>();
        foreach (var evaluation in Timeframes.All)
        {
            var generator = new ModelGenerator(4_000 + (int)evaluation, evaluation, true, true);
            for (var i = 0; i < 700; i++)
            {
                sources.Add(Printer.Print(generator.Next(), Parens.Always, Spacing.Normal));
            }
        }

        return sources;
    }

    public static TheoryData<string, string> Constructs { get; } = new()
    {
        { "crosses_above", @"crosses_above\(" },
        { "crosses_below", @"crosses_below\(" },
        { "rising", @"rising\(" },
        { "falling", @"falling\(" },
        { "between", @"between\(" },
        { "abs", @"\babs\(" },
        { "min", @"\bmin\(" },
        { "max", @"\bmax\(" },
        { "not", @"\bnot " },
        { "and", @" and " },
        { "or", @" or " },
        { "true", @"\btrue\b" },
        { "false", @"\bfalse\b" },
        { "tf qualification", @"tf\(""[0-9]+[mhDWM]""\)\." },
        { "subscript on a reference", @"\b[a-z_]+\[[0-9]+\]" },
        { "subscript on a qualified reference", @"tf\(""[^""]+""\)\.[a-z_]+\[[0-9]+\]" },
        { "deep subscript", @"\[(1[3-9]|[23][0-9]|40)\]" },
        { "parameter fast_len", @"\bfast_len\b" },
        { "parameter slow_len", @"\bslow_len\b" },
        { "parameter level", @"\blevel\b" },
        { "parameter as an argument", @"[a-z_]+\((fast_len|slow_len)[,)]" },
        { "prev_day dotted", @"prev_day\.[a-z]+" },
        { "prev_day flat", @"prev_day_[a-z]+" },
        { "today dotted", @"today\.[a-z]+" },
        { "today flat", @"today_[a-z]+" },
        { "index.trend", @"index\.trend" },
        { "index.adx", @"index\.adx" },
        { "index_trend", @"index_trend" },
        { "index_adx", @"index_adx" },
        { "time before the text", @"time [<>=!]=? ""[0-9:]{5}""" },
        { "time after the text", @"""[0-9:]{5}"" [<>=!]=? time" },
        { "regime by name", @"regime\(\) [=!]= ""[A-Z_]+""" },
        { "regime name first", @"""[A-Z_]+"" [=!]= regime\(\)" },
        { "regime by code", @"regime\(\) [=!]= [1-9]\b" },
        { "weekday constant", @"\b(MON|TUE|WED|THU|FRI)\b" },
        { "weekend constant", @"\b(SAT|SUN)\b" },
        { "day_of_week", @"\bday_of_week\b" },
        { "is_expiry_day", @"\bis_expiry_day\b" },
        { "defaults left out", @"\b(rsi|ema|sma|atr|adx|macd_line|bb_upper|supertrend)\(\)" },
        { "trailing defaults left out", @"\b(bb_upper|bb_lower|supertrend|macd_line)\([0-9a-z_.]+\)" },
        { "source", @"source=(close|open|high|low)\)" },
        { "source only", @"\b[a-z_]+\(source=(close|open|high|low)\)" },
        { "division by a literal zero", @"/ 0\b" },
        { "division by a difference", @"/ \(([^()]+) - \1\)" },
        { "equality of truth values", @"\) [=!]= \(" },
        { "less", @" < " },
        { "less or equal", @" <= " },
        { "greater", @" > " },
        { "greater or equal", @" >= " },
        { "equal", @" == " },
        { "not equal", @" != " },
        { "exponent literal", @"\b1e-3\b" },
        { "huge literal", @"\b1e308\b" },
        { "negation", @"-[a-z(]" },
        { "call of a parameterless feature", @"\b(inside_bar|engulfing|vwap)\(\)" },
        { "bare parameterless feature", @"\b(inside_bar|engulfing|vwap)\b[^(]" },
    };

    [Theory]
    [MemberData(nameof(Constructs))]
    public void Every_construct_of_the_language_occurs_in_the_generated_conditions(string construct, string pattern)
    {
        var occurrences = Sources.Value.Count(source => Regex.IsMatch(source, pattern));

        occurrences.ShouldBeGreaterThan(5, "the generator hardly ever writes " + construct);
    }

    [Fact]
    public void All_56_catalog_names_are_read_by_the_generated_conditions()
    {
        var names = new HashSet<string>();
        foreach (var evaluation in Timeframes.All)
        {
            var generator = new ModelGenerator(9_000 + (int)evaluation, evaluation, true, true);
            for (var i = 0; i < 1_500; i++)
            {
                foreach (var spec in ModelFacts.Of(generator.Next()).Features)
                {
                    names.Add(spec.Name);
                }
            }
        }

        var missing = IndicatorCatalog.All.Select(i => i.Name).Where(n => !names.Contains(n)).ToList();

        IndicatorCatalog.All.Count.ShouldBe(56);
        missing.ShouldBeEmpty("never read: " + string.Join(", ", missing));
    }

    [Fact]
    public void Every_evaluation_timeframe_reads_features_of_other_timeframes()
    {
        foreach (var evaluation in Timeframes.All)
        {
            var generator = new ModelGenerator(1_234 + (int)evaluation, evaluation, true, true);
            var others = 0;
            for (var i = 0; i < 300; i++)
            {
                others += ModelFacts.Of(generator.Next()).Features.Count(s => s.Timeframe != evaluation);
            }

            others.ShouldBeGreaterThan(100, evaluation + " hardly reads another timeframe");
        }
    }
}
