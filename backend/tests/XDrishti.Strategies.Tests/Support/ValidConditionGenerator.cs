using System.Globalization;
using System.Text;
using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators;

namespace XDrishti.Strategies.Tests.Support;

/// <summary>
/// Random valid conditions for a strategy timeframe, with the facts that follow from how each was built (see <see cref="GeneratedCondition"/>): the features read, the history depth and the needs of the frame. The
/// same seed gives the same sequence. It writes text, not trees, and knows the canonical spec of each building block from a table written by hand, so it shares nothing with the binder. Used by the plan tests and
/// meant for the tests of the compiled conditions (the oracle, the look-ahead property): <see cref="Parameters"/> must be given to the compiler with the text.
/// <para>
/// What it makes: comparisons of arithmetic over features (any of the nine timeframes, qualified with <c>tf(…)</c> or not, with subscripts), <c>crosses_above/below</c>, <c>rising/falling</c> and <c>between</c> over
/// features of one timeframe, the time against a text (intraday strategies only), the calendar and the market context (when allowed), strategy parameters, <c>and</c>, <c>or</c> and <c>not</c>. It never makes a lagged
/// call over two timeframes, a history of more than <c>maxLag + MaxLag + 1</c> bars, or a feature the catalog refuses on the timeframe.
/// </para>
/// </summary>
internal sealed class ValidConditionGenerator(int seed, Timeframe evaluationTimeframe, bool allowContext = true, bool allowExpiryDays = true, int maxLag = 12)
{
    /// <summary>The strategy parameters the conditions use (<c>fast_len</c>, <c>slow_len</c>, <c>level</c>).</summary>
    public static readonly IReadOnlyDictionary<string, double> Parameters = new Dictionary<string, double> { ["fast_len"] = 9, ["slow_len"] = 21, ["level"] = 55 };

    /// <summary>The largest run of <c>rising/falling</c> the generator writes; with a subscript of up to <c>maxLag</c> the history stays far below 256.</summary>
    public const int MaxLag = 12;

    private static readonly Timeframe[] AllFrames = [Timeframe.M1, Timeframe.M5, Timeframe.M10, Timeframe.M15, Timeframe.M30, Timeframe.H1, Timeframe.D1, Timeframe.W1, Timeframe.Mo1];

    private static readonly string[] Comparisons = ["<", "<=", ">", ">=", "==", "!="];

    private static readonly string[] Numbers = ["0", "1", "2", "55", "0.5", "100", "1e-3", "2.5", "level", "TUE", "MON"];

    private readonly Random _random = new(seed);

    private enum Reach
    {
        Everywhere,
        Sessions,
        IntradayOnly,
    }

    /// <summary>One block a feature is written with: the text as a call or reference, the flat name (for <c>tf(…)</c>), the canonical spec as a function of the timeframe, and where the catalog runs it.</summary>
    private sealed record Leaf(string Text, string QualifiedText, bool IsReference, Reach Reach, Func<Timeframe, FeatureSpec> Spec);

    private static FeatureSpec S(string name, Timeframe frame, double p1 = 0, double p2 = 0, double p3 = 0, PriceSource source = PriceSource.Close) => new(name, frame, source, p1, p2, p3);

    private static readonly Leaf[] Leaves =
    [
        new("close", "close", true, Reach.Everywhere, f => S("close", f)),
        new("open", "open", true, Reach.Everywhere, f => S("open", f)),
        new("high", "high", true, Reach.Everywhere, f => S("high", f)),
        new("low", "low", true, Reach.Everywhere, f => S("low", f)),
        new("volume", "volume", true, Reach.Everywhere, f => S("volume", f)),
        new("sma(10)", "sma(10)", false, Reach.Everywhere, f => S("sma", f, 10)),
        new("sma(5, source=low)", "sma(5, source=low)", false, Reach.Everywhere, f => S("sma", f, 5, source: PriceSource.Low)),
        new("ema(20)", "ema(20)", false, Reach.Everywhere, f => S("ema", f, 20)),
        new("ema()", "ema()", false, Reach.Everywhere, f => S("ema", f, 20)),
        new("ema(fast_len)", "ema(fast_len)", false, Reach.Everywhere, f => S("ema", f, 9)),
        new("ema(slow_len)", "ema(slow_len)", false, Reach.Everywhere, f => S("ema", f, 21)),
        new("rsi(14)", "rsi(14)", false, Reach.Everywhere, f => S("rsi", f, 14)),
        new("rsi()", "rsi()", false, Reach.Everywhere, f => S("rsi", f, 14)),
        new("atr(14)", "atr(14)", false, Reach.Everywhere, f => S("atr", f, 14)),
        new("highest(5)", "highest(5)", false, Reach.Everywhere, f => S("highest", f, 5, source: PriceSource.High)),
        new("lowest(5, source=low)", "lowest(5, source=low)", false, Reach.Everywhere, f => S("lowest", f, 5, source: PriceSource.Low)),
        new("adx(14)", "adx(14)", false, Reach.Everywhere, f => S("adx", f, 14)),
        new("roc(5)", "roc(5)", false, Reach.Everywhere, f => S("roc", f, 5)),
        new("bb_upper(20, 2)", "bb_upper(20, 2)", false, Reach.Everywhere, f => S("bb_upper", f, 20, 2)),
        new("bb_lower(20)", "bb_lower(20)", false, Reach.Everywhere, f => S("bb_lower", f, 20, 2)),
        new("macd_line()", "macd_line()", false, Reach.Everywhere, f => S("macd_line", f, 12, 26, 9)),
        new("macd_hist(8, 21, 5)", "macd_hist(8, 21, 5)", false, Reach.Everywhere, f => S("macd_hist", f, 8, 21, 5)),
        new("supertrend(10, 3)", "supertrend(10, 3)", false, Reach.Everywhere, f => S("supertrend", f, 10, 3)),
        new("rel_volume(10)", "rel_volume(10)", false, Reach.Everywhere, f => S("rel_volume", f, 10)),
        new("nr(4)", "nr(4)", false, Reach.Everywhere, f => S("nr", f, 4)),
        new("pivot_high(3)", "pivot_high(3)", false, Reach.Everywhere, f => S("pivot_high", f, 3)),
        new("plus_di()", "plus_di()", false, Reach.Everywhere, f => S("plus_di", f, 14)),
        new("minus_di(10)", "minus_di(10)", false, Reach.Everywhere, f => S("minus_di", f, 10)),
        new("prev_day.high", "prev_day_high", true, Reach.Sessions, f => S("prev_day_high", f)),
        new("prev_day.close", "prev_day_close", true, Reach.Sessions, f => S("prev_day_close", f)),
        new("today.open", "today_open", true, Reach.Sessions, f => S("today_open", f)),
        new("gap_pct", "gap_pct", true, Reach.Sessions, f => S("gap_pct", f)),
        new("vwap", "vwap", true, Reach.IntradayOnly, f => S("vwap", f)),
        new("or_high(60)", "or_high(60)", false, Reach.IntradayOnly, f => S("or_high", f, 60)),
    ];

    private readonly record struct Read(FeatureSpec Spec, int Lag);

    private sealed record Part(string Text, List<Read> Reads, bool Context = false, bool Expiry = false, bool Time = false)
    {
        public Part Shifted(int by) => this with { Reads = [.. Reads.Select(r => r with { Lag = r.Lag + by })] };
    }

    /// <summary>The next condition.</summary>
    public GeneratedCondition Next()
    {
        while (true)
        {
            var part = Bool(_random.Next(1, 4));
            var features = part.Reads.Select(r => r.Spec).Distinct().ToList();
            if (features.Count == 0)
            {
                continue;
            }

            var deepest = part.Reads.Max(r => r.Lag);
            return new GeneratedCondition(part.Text, features, deepest + 1, part.Context, part.Expiry, part.Time);
        }
    }

    private Part Bool(int depth)
    {
        if (depth <= 0 || _random.Next(100) < 25)
        {
            return BoolLeaf();
        }

        switch (_random.Next(6))
        {
            case 0:
                return Wrap("not ", Bool(depth - 1));
            case 1 or 2:
                return Join(Bool(depth - 1), " or ", Bool(depth - 1), true);
            default:
                return Join(Bool(depth - 1), " and ", Bool(depth - 1), true);
        }
    }

    private Part BoolLeaf()
    {
        switch (_random.Next(12))
        {
            case 0 or 1 or 2 or 3:
                {
                    var left = Number(2, null);
                    var right = Number(2, null);
                    return Join(left, " " + Pick(Comparisons) + " ", right, false);
                }

            case 4 or 5:
                {
                    var frame = LaggedFrame();
                    var name = _random.Next(2) == 0 ? "crosses_above" : "crosses_below";
                    var a = Number(1, frame);
                    var b = _random.Next(4) == 0 ? new Part(Pick(Numbers), []) : Number(1, frame);
                    var call = new Part(name + "(" + a.Text + ", " + b.Text + ")", [.. a.Shifted(1).Reads, .. b.Shifted(1).Reads], a.Context || b.Context, a.Expiry || b.Expiry);
                    return call;
                }

            case 6 or 7:
                {
                    var frame = LaggedFrame();
                    var steps = _random.Next(1, MaxLag);
                    var x = Number(1, frame);
                    var name = _random.Next(2) == 0 ? "rising" : "falling";
                    return x.Shifted(steps) with { Text = name + "(" + x.Text + ", " + steps.ToString(CultureInfo.InvariantCulture) + ")" };
                }

            case 8:
                {
                    var frame = LaggedFrame();
                    var x = Number(1, frame);
                    var low = Number(0, frame);
                    var high = Number(0, frame);
                    return new Part("between(" + x.Text + ", " + low.Text + ", " + high.Text + ")", [.. x.Reads, .. low.Reads, .. high.Reads]);
                }

            case 9:
                return TimeOrCalendar();
            default:
                return DateFeature();
        }
    }

    /// <summary>The time against a text (an intraday strategy), or <c>day_of_week</c> against a day.</summary>
    private Part TimeOrCalendar()
    {
        if (evaluationTimeframe.Minutes() > 0 && _random.Next(2) == 0)
        {
            string[] times = ["09:15", "09:45", "10:30", "12:00", "14:30", "15:30"];
            var text = "\"" + Pick(times) + "\"";
            var op = Pick(Comparisons);
            return _random.Next(2) == 0
                ? new Part("time " + op + " " + text, [], Time: true)
                : new Part(text + " " + Mirror(op) + " time", [], Time: true);
        }

        var day = new Part("day_of_week", [new Read(S("day_of_week", Timeframe.M1), 0)]);
        return new Part(day.Text + " " + Pick(["==", "!=", "<", ">="]) + " " + Pick(["MON", "TUE", "WED", "THU", "FRI"]), day.Reads);
    }

    /// <summary>A feature of the day (the calendar, the context) against a number, or the regime against a name.</summary>
    private Part DateFeature()
    {
        var choices = new List<(string Text, string Name, bool Context, bool Expiry)> { ("day_of_week", "day_of_week", false, false) };
        if (allowExpiryDays)
        {
            choices.Add(("is_expiry_day", "is_expiry_day", false, true));
        }

        if (allowContext)
        {
            choices.AddRange([("vix", "vix", true, false), ("vix_pct", "vix_pct", true, false), ("regime_trend", "regime_trend", true, false), ("index.adx", "index_adx", true, false), ("breadth", "breadth", true, false), ("index.trend", "index_trend", true, false)]);
        }

        if (allowContext && _random.Next(4) == 0)
        {
            string[] names = ["UP_LOW_VOL", "UP_NORMAL_VOL", "RANGE_HIGH_VOL", "DOWN_HIGH_VOL"];
            var name = _random.Next(2) == 0 ? "\"" + Pick(names) + "\"" : _random.Next(1, 10).ToString(CultureInfo.InvariantCulture);
            return new Part("regime() " + Pick(["==", "!="]) + " " + name, [new Read(S("regime", Timeframe.M1), 0)], Context: true);
        }

        var (text, featureName, context, expiry) = choices[_random.Next(choices.Count)];
        var lag = _random.Next(3) == 0 ? _random.Next(1, maxLag + 1) : 0;
        var written = lag == 0 ? text : text + "[" + lag.ToString(CultureInfo.InvariantCulture) + "]";
        var right = _random.Next(3) == 0 ? "level" : Pick(Numbers);
        return new Part(written + " " + Pick(Comparisons) + " " + right, [new Read(S(featureName, Timeframe.M1), lag)], context, expiry);
    }

    /// <summary>A number: a literal, a parameter, a feature (of <paramref name="pin"/> when given, of any timeframe otherwise), or arithmetic on those.</summary>
    private Part Number(int depth, Timeframe? pin)
    {
        if (depth <= 0 || _random.Next(100) < 45)
        {
            return _random.Next(100) < 15 ? new Part(Pick(Numbers), []) : FeatureLeaf(pin ?? RandomFrame());
        }

        switch (_random.Next(8))
        {
            case 0:
                return Wrap("-", Number(depth - 1, pin));
            case 1:
                return Call("abs", [Number(depth - 1, pin)]);
            case 2:
                return Call(_random.Next(2) == 0 ? "min" : "max", [Number(depth - 1, pin), Number(depth - 1, pin)]);
            default:
                return Join(Number(depth - 1, pin), " " + Pick(["+", "-", "*", "/"]) + " ", Number(depth - 1, pin), true);
        }
    }

    private Part FeatureLeaf(Timeframe frame)
    {
        var candidates = Leaves.Where(l => l.Reach == Reach.Everywhere
            || (l.Reach == Reach.Sessions && frame is not (Timeframe.W1 or Timeframe.Mo1))
            || (l.Reach == Reach.IntradayOnly && frame.Minutes() > 0)).ToArray();
        var leaf = Pick(candidates);
        var qualified = frame != evaluationTimeframe || _random.Next(100) < 20;
        var text = qualified ? "tf(\"" + frame.ToCode() + "\")." + leaf.QualifiedText : leaf.Text;
        var lag = 0;
        if (leaf.IsReference && _random.Next(3) == 0)
        {
            lag = _random.Next(1, maxLag + 1);
            text += "[" + lag.ToString(CultureInfo.InvariantCulture) + "]";
        }

        return new Part(text, [new Read(leaf.Spec(frame), lag)]);
    }

    private Timeframe RandomFrame() => _random.Next(100) < 45 ? evaluationTimeframe : Pick(AllFrames);

    /// <summary>The one timeframe the features of a lagged call share.</summary>
    private Timeframe LaggedFrame() => _random.Next(100) < 60 ? evaluationTimeframe : Pick(AllFrames);

    private static Part Wrap(string prefix, Part inner) => inner with { Text = prefix + (prefix == "not " || prefix == "-" ? Parenthesised(inner.Text) : inner.Text) };

    private static Part Call(string name, Part[] arguments) =>
        new(name + "(" + string.Join(", ", arguments.Select(a => a.Text)) + ")", [.. arguments.SelectMany(a => a.Reads)], arguments.Any(a => a.Context), arguments.Any(a => a.Expiry));

    private static Part Join(Part left, string op, Part right, bool parenthesise)
    {
        var text = parenthesise ? Parenthesised(left.Text) + op + Parenthesised(right.Text) : left.Text + op + right.Text;
        return new Part(
            parenthesise ? "(" + text + ")" : text,
            [.. left.Reads, .. right.Reads],
            left.Context || right.Context,
            left.Expiry || right.Expiry,
            left.Time || right.Time);
    }

    private static string Parenthesised(string text) => text.StartsWith('(') && Balanced(text) ? text : "(" + text + ")";

    /// <summary>Whether the opening bracket at the start of the text is closed by the last character (so the text is one parenthesised group).</summary>
    private static bool Balanced(string text)
    {
        var depth = 0;
        for (var i = 0; i < text.Length; i++)
        {
            depth += text[i] == '(' ? 1 : text[i] == ')' ? -1 : 0;
            if (depth == 0)
            {
                return i == text.Length - 1;
            }
        }

        return false;
    }

    private static string Mirror(string op) => op switch { "<" => ">", "<=" => ">=", ">" => "<", ">=" => "<=", _ => op };

    private T Pick<T>(T[] items) => items[_random.Next(items.Length)];

    /// <summary>The text with every condition on its own line, for a failure message.</summary>
    public static string Show(IEnumerable<GeneratedCondition> conditions)
    {
        var text = new StringBuilder();
        foreach (var c in conditions)
        {
            text.AppendLine(c.Source);
        }

        return text.ToString();
    }
}
