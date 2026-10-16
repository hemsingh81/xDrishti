using System.Collections.Concurrent;
using System.Globalization;
using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators;

namespace XDrishti.Strategies.Tests.Support.Oracle;

/// <summary>
/// Random valid conditions as <see cref="Node"/> models for an evaluation timeframe; the same seed gives the same sequence. It never writes text — <see cref="Printer"/> does — and it knows the spec of what it builds from
/// its own tables, so it shares nothing with the parser or the binder. It covers every construct of the language: all six comparisons on numbers and on true/false values, arithmetic including a division by zero,
/// <c>abs</c>, <c>min</c>, <c>max</c>, <c>crosses_above/below</c>, <c>rising/falling</c>, <c>between</c>, <c>not/and/or</c>, <c>true/false</c>, subscripts on references of any timeframe, <c>tf(…)</c> qualification,
/// strategy parameters, the dotted spellings (<c>prev_day.high</c>, <c>today.open</c>, <c>index.trend</c>, <c>index.adx</c>), defaults left out (<c>rsi()</c>), <c>source=</c>, <c>time</c> against a text (intraday
/// evaluation only), <c>day_of_week</c> against <c>MON … SUN</c>, <c>regime()</c> by name or by code, and the calendar and context features (when allowed). A lagged call (<c>crosses_*</c>, <c>rising</c>, <c>falling</c>) only
/// ever has leaves on one clock — the same bar timeframe, or only features of the date (the calendar and the context: their previous value is the previous <i>date</i>, so they are never mixed with a bar feature,
/// 1m included) —, so every condition is one the language accepts; <see cref="Parameters"/> must be given to the compiler with the text.
/// </summary>
internal sealed class ModelGenerator(int seed, Timeframe evaluation, bool allowContext, bool allowExpiryDays)
{
    /// <summary>The strategy parameters the conditions use.</summary>
    public static readonly IReadOnlyDictionary<string, double> Parameters = new Dictionary<string, double> { ["fast_len"] = 9, ["slow_len"] = 21, ["level"] = 55 };

    /// <summary>The one clock every feature of a lagged call must share: a bar timeframe, or <see cref="Date"/> (the calendar and context features, one value a date, always read on 1m).</summary>
    private readonly record struct Pin(Timeframe Frame, bool Date);

    private static readonly Timeframe[] AllFrames = [Timeframe.M1, Timeframe.M5, Timeframe.M10, Timeframe.M15, Timeframe.M30, Timeframe.H1, Timeframe.D1, Timeframe.W1, Timeframe.Mo1];

    private static readonly string[] Comparisons = ["<", "<=", ">", ">=", "==", "!="];

    private static readonly string[] Literals = ["0", "1", "2", "3", "5", "10", "14", "20", "30", "50", "70", "100", "200", "500", "1000", "0.5", "0.25", "2.5", "25.5", "1e-3", "1e3", "1e308"];

    private static readonly string[] Levels = ["0", "1", "20", "50", "70", "100", "200", "500", "1000", "1500"];

    private static readonly string[] Weekdays = ["MON", "TUE", "WED", "THU", "FRI", "SAT", "SUN"];

    private static readonly string[] RawNames = ["open", "high", "low", "close", "close", "close", "volume"];

    private static readonly string[] Sources = ["close", "open", "high", "low"];

    private static readonly string[] ContextNames = ["index_trend", "index_adx", "index_vol_pct", "vix", "vix_pct", "vix_chg", "breadth", "regime_trend", "regime_vol"];

    private static readonly string[] IndicatorNames =
    [
        "sma", "ema", "rsi", "atr", "highest", "lowest", "rel_volume", "or_high", "or_low", "adx", "plus_di", "minus_di", "macd_line", "macd_signal", "macd_hist",
        "bb_upper", "bb_middle", "bb_lower", "bb_width", "supertrend", "supertrend_dir", "roc", "nr", "pivot_high", "pivot_low", "pivot_high_age", "pivot_low_age",
    ];

    private static readonly string[] TimeTexts = ["09:15", "09:16", "09:30", "09:45", "10:00", "11:30", "12:00", "14:30", "15:00", "15:29", "15:30"];

    private static readonly ConcurrentDictionary<FeatureSpec, bool> ValidSpecs = new();

    private readonly Random _random = new(seed);

    /// <summary>The next condition: a true/false model.</summary>
    public Node Next() => Truthy(_random.Next(1, 4));

    // ---- true / false -------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    private Node Truthy(int depth)
    {
        if (depth <= 0 || Chance(25))
        {
            return TruthLeaf();
        }

        var roll = _random.Next(100);
        if (roll < 15)
        {
            return new Not(Truthy(depth - 1));
        }

        if (roll < 82)
        {
            return new Logic(Chance(50), Truthy(depth - 1), Truthy(depth - 1));
        }

        return new Cmp(Chance(50) ? "==" : "!=", Truthy(depth - 1), Truthy(depth - 1));
    }

    private Node TruthLeaf()
    {
        var roll = _random.Next(100);
        if (roll < 28)
        {
            return Compare();
        }

        if (roll < 40)
        {
            var pin = LaggedPin();
            if (!pin.Date && Chance(35))
            {
                return new Crosses(Chance(50), BarLeaf(pin.Frame), BarLeaf(pin.Frame));
            }

            var a = Number(1, pin);
            var b = Chance(30) ? Level() : Number(1, pin);
            return new Crosses(Chance(50), a, b);
        }

        if (roll < 50)
        {
            var pin = LaggedPin();
            var steps = Chance(85) ? _random.Next(1, 6) : _random.Next(6, 10);
            return new Streak(Chance(50), Number(1, pin), steps);
        }

        if (roll < 58)
        {
            return new Between(Number(1, null), Chance(30) ? Level() : Number(1, null), Chance(30) ? Level() : Number(1, null));
        }

        if (roll < 66)
        {
            return TimeCompare() ?? (Node)Compare();
        }

        if (roll < 74)
        {
            var index = Chance(85) ? _random.Next(5) : _random.Next(5, 7);
            var day = new Const(Weekdays[index], index + 1);
            var leaf = new Leaf(DateFeature("day_of_week", "day_of_week", false, false), Timeframe.M1, Chance(10), Chance(25) ? _random.Next(1, 4) : 0);
            return Chance(80) ? new Cmp(Pick(Comparisons), leaf, day) : new Cmp(Pick(Comparisons), day, leaf);
        }

        if (roll < 82)
        {
            var leaf = DateLeaf();
            return new Cmp(Pick(Comparisons), leaf, Chance(30) ? Level() : Literal());
        }

        if (roll < 88)
        {
            return RegimeCompare() ?? (Node)Compare();
        }

        if (roll < 91)
        {
            return new BoolLit(Chance(50));
        }

        return Discrete();
    }

    /// <summary>
    /// Features that take only a few values (a volume that is often 0 or the same as the bar before, the patterns, the supertrend direction, the age of a pivot) against whole numbers: crossings, runs, ranges and
    /// comparisons where equality is the usual case — at the bar before a crossing, at a step of a run, at the ends of a range —, not a coincidence.
    /// </summary>
    private Node Discrete()
    {
        var pin = LaggedFrame();
        switch (_random.Next(4))
        {
            case 0:
                return new Crosses(Chance(50), DiscreteLeaf(pin), SmallInteger());
            case 1:
                return new Streak(Chance(50), DiscreteLeaf(pin), _random.Next(1, 4));
            case 2:
                return new Between(DiscreteLeaf(pin), SmallInteger(), SmallInteger());
            default:
                return new Cmp(Pick(Comparisons), DiscreteLeaf(pin), SmallInteger());
        }
    }

    private Node SmallInteger() => Chance(15) ? new Neg(new Lit("1")) : new Lit(Pick(["0", "1", "2"]));

    /// <summary>One of open, high, low, close of the timeframe, with a subscript now and then: they are equal to one another and to the bar before often enough (flat bars, prices on a tick).</summary>
    private Leaf BarLeaf(Timeframe frame)
    {
        var name = Pick(["open", "high", "low", "close", "close"]);
        var feature = new Feature(name, name, true, new FeatureSpec(name));
        return new Leaf(feature, frame, frame != evaluation || Chance(15), Chance(30) ? _random.Next(1, 3) : 0);
    }

    private Leaf DiscreteLeaf(Timeframe frame)
    {
        var feature = _random.Next(6) switch
        {
            0 or 1 => new Feature("volume", "volume", true, new FeatureSpec("volume")),
            2 => Chance(70) ? new Feature("inside_bar", "inside_bar", true, new FeatureSpec("inside_bar")) : new Feature("inside_bar()", "inside_bar()", false, new FeatureSpec("inside_bar")),
            3 => new Feature("engulfing", "engulfing", true, new FeatureSpec("engulfing")),
            4 => new Feature("supertrend_dir(2, 1.5)", "supertrend_dir(2, 1.5)", false, new FeatureSpec("supertrend_dir", Timeframe.M1, PriceSource.Default, 2, 1.5)),
            _ => new Feature("nr(2)", "nr(2)", false, new FeatureSpec("nr", Timeframe.M1, PriceSource.Default, 2)),
        };
        return new Leaf(feature, frame, frame != evaluation || Chance(15), feature.IsReference && Chance(30) ? _random.Next(1, 3) : 0);
    }

    private Cmp Compare() => new Cmp(Pick(Comparisons), Number(2, null), Chance(25) ? Level() : Number(2, null));

    /// <summary><c>time</c> against a text in 09:15 … 15:30, or null when the evaluation timeframe is a day or longer (the language refuses <c>time</c> there).</summary>
    private TimeCmp? TimeCompare()
    {
        if (evaluation.Minutes() == 0)
        {
            return null;
        }

        var text = Chance(70) ? Pick(TimeTexts) : TimeText(_random.Next(9 * 60 + 15, 15 * 60 + 31));
        var minutes = int.Parse(text[..2], CultureInfo.InvariantCulture) * 60 + int.Parse(text[3..], CultureInfo.InvariantCulture);
        return new TimeCmp(Pick(Comparisons), minutes, text, Chance(40));
    }

    private static string TimeText(int minuteOfDay) => (minuteOfDay / 60).ToString("00", CultureInfo.InvariantCulture) + ":" + (minuteOfDay % 60).ToString("00", CultureInfo.InvariantCulture);

    private RegimeCmp? RegimeCompare()
    {
        if (!allowContext)
        {
            return null;
        }

        var code = _random.Next(1, 10);
        var byName = Chance(60);
        var regime = new Leaf(new Feature("regime()", "regime()", false, new FeatureSpec("regime", Timeframe.M1), ReadsContext: true), Timeframe.M1, false, 0);
        return new RegimeCmp(Chance(60), code, byName ? RegimeNames.All[code - 1] : null, byName && Chance(30), regime);
    }

    // ---- numbers ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    /// <summary>A number. <paramref name="pin"/> is the one clock every feature must run on (inside a lagged call), or null for any.</summary>
    private Node Number(int depth, Pin? pin)
    {
        if (depth <= 0 || Chance(40))
        {
            var roll = _random.Next(100);
            if (roll < 12)
            {
                return Literal();
            }

            return roll < 18 ? NamedConstant() : LeafOf(pin);
        }

        var kind = _random.Next(100);
        if (kind < 8)
        {
            return new Neg(Number(depth - 1, pin));
        }

        if (kind < 16)
        {
            return new Abs(Number(depth - 1, pin));
        }

        if (kind < 28)
        {
            return new MinMax(Chance(50), Number(depth - 1, pin), Number(depth - 1, pin));
        }

        var op = Pick(['+', '-', '*', '/']);
        var left = Number(depth - 1, pin);
        if (op == '/' && Chance(20))
        {
            var x = Number(depth - 1, pin);
            return new Arith('/', left, Chance(50) ? new Lit("0") : new Arith('-', x, x));
        }

        return new Arith(op, left, Number(depth - 1, pin));
    }

    private Lit Literal() => new(Pick(Literals));

    private Node Level() => Chance(35) ? new Const("level", 55) : new Lit(Pick(Levels));

    private Const NamedConstant()
    {
        if (Chance(60))
        {
            var name = Pick(Parameters.Keys.ToArray());
            return new Const(name, Parameters[name]);
        }

        var day = _random.Next(Weekdays.Length);
        return new Const(Weekdays[day], day + 1);
    }

    private Timeframe RandomFrame() => Chance(45) ? evaluation : Pick(AllFrames);

    private Timeframe LaggedFrame() => Chance(60) ? evaluation : Pick(AllFrames);

    /// <summary>The clock of a lagged call: a bar timeframe, now and then the date (a lagged call over the calendar and the context only, which the language accepts on any evaluation timeframe).</summary>
    private Pin LaggedPin() => Chance(14) ? new Pin(Timeframe.M1, true) : new Pin(LaggedFrame(), false);

    // ---- features -----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    private Leaf LeafOf(Pin? pin)
    {
        var roll = _random.Next(100);
        if (pin is { Date: true } || (pin is null && roll < 14))
        {
            return DateLeaf();
        }

        var frame = pin?.Frame ?? RandomFrame();
        var feature = roll switch
        {
            < 42 => RawFeature(),
            < 84 => Indicator(frame),
            _ => Extra(frame),
        };
        return Finish(feature ?? RawFeature(), frame);
    }

    private Leaf Finish(Feature feature, Timeframe frame)
    {
        var qualified = frame != evaluation || Chance(15);
        var lag = feature.IsReference && Chance(30) ? SubscriptLag(frame) : 0;
        return new Leaf(feature, frame, qualified, lag);
    }

    private int SubscriptLag(Timeframe frame)
    {
        switch (frame)
        {
            case Timeframe.W1 or Timeframe.Mo1:
                return _random.Next(1, 4);
            case Timeframe.D1:
                return _random.Next(1, 5);
            default:
                {
                    var roll = _random.Next(100);
                    return roll < 70 ? _random.Next(1, 5) : roll < 95 ? _random.Next(5, 13) : _random.Next(13, 41);
                }
        }
    }

    private Feature RawFeature()
    {
        var name = Pick(RawNames);
        return new Feature(name, name, true, new FeatureSpec(name));
    }

    private Leaf DateLeaf()
    {
        var choices = new List<Feature> { DateFeature("day_of_week", "day_of_week", false, false) };
        if (allowExpiryDays)
        {
            choices.Add(DateFeature("is_expiry_day", "is_expiry_day", false, true));
        }

        if (allowContext)
        {
            foreach (var name in ContextNames)
            {
                var dotted = name switch { "index_trend" => "index.trend", "index_adx" => "index.adx", _ => name };
                choices.Add(DateFeature(Chance(50) ? dotted : name, name, true, false));
            }
        }

        return new Leaf(Pick(choices.ToArray()), Timeframe.M1, Chance(15), Chance(30) ? _random.Next(1, 4) : 0);
    }

    private static Feature DateFeature(string text, string flat, bool context, bool expiry) =>
        new(text, flat, true, new FeatureSpec(flat, Timeframe.M1), context, expiry);

    /// <summary>The parameterless features that are not raw bars: the patterns, <c>vwap</c>, the previous day and today, the gap (where the catalog runs them).</summary>
    private Feature? Extra(Timeframe frame)
    {
        var candidates = new List<Feature>();
        foreach (var name in new[] { "inside_bar", "engulfing", "vwap" })
        {
            if (Supports(name, frame))
            {
                var call = Chance(30);
                var text = call ? name + "()" : name;
                candidates.Add(new Feature(text, text, !call, new FeatureSpec(name)));
            }
        }

        foreach (var (dotted, flat) in new[]
        {
            ("prev_day.open", "prev_day_open"), ("prev_day.high", "prev_day_high"), ("prev_day.low", "prev_day_low"), ("prev_day.close", "prev_day_close"), ("prev_day.volume", "prev_day_volume"),
            ("today.open", "today_open"), ("today.high", "today_high"), ("today.low", "today_low"), ("gap_pct", "gap_pct"),
        })
        {
            if (Supports(flat, frame))
            {
                candidates.Add(new Feature(Chance(60) ? dotted : flat, flat, true, new FeatureSpec(flat)));
            }
        }

        return candidates.Count == 0 ? null : Pick(candidates.ToArray());
    }

    private static bool Supports(string name, Timeframe frame) => IndicatorCatalog.TryGet(name, out var info) && info.Supports(frame);

    /// <summary>An indicator with arguments, positional (trailing defaults left out) and sometimes <c>source=</c>; null when no valid choice was found.</summary>
    private Feature? Indicator(Timeframe frame)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var name = Pick(IndicatorNames);
            if (!IndicatorCatalog.TryGet(name, out var info) || !info.Supports(frame))
            {
                continue;
            }

            var feature = BuildIndicator(info, frame);
            if (ValidSpecs.GetOrAdd(feature.Spec with { Timeframe = frame }, spec => FeatureFrame.Create([spec], new FrameOptions()).IsSuccess))
            {
                return feature;
            }
        }

        return null;
    }

    private Feature BuildIndicator(IndicatorInfo info, Timeframe frame)
    {
        var count = info.Parameters.Count;
        var values = new double[4];
        var written = new string?[4];
        var shown = count;
        if (Chance(15))
        {
            shown = 0;
            for (var i = 0; i < count; i++)
            {
                values[i] = info.Parameters[i].Default;
            }
        }
        else
        {
            PickArguments(info, frame, values, written);
            while (shown > 0 && values[shown - 1] == info.Parameters[shown - 1].Default && Chance(70))
            {
                shown--;
            }
        }

        var arguments = new List<string>();
        for (var i = 0; i < shown; i++)
        {
            arguments.Add(written[i] ?? values[i].ToString("R", CultureInfo.InvariantCulture));
        }

        var source = PriceSource.Default;
        if (info.UsesSource && Chance(25))
        {
            var chosen = Pick(Sources);
            arguments.Add("source=" + chosen);
            source = chosen switch { "open" => PriceSource.Open, "high" => PriceSource.High, "low" => PriceSource.Low, _ => PriceSource.Close };
        }

        var text = info.Name + "(" + string.Join(", ", arguments) + ")";
        return new Feature(text, text, false, new FeatureSpec(info.Name, Timeframe.M1, source, values[0], values[1], values[2], values[3]));
    }

    /// <summary>Arguments the catalog accepts on the timeframe (and sometimes a strategy parameter in place of a number). The caller checks the spec against the frame's own validation.</summary>
    private void PickArguments(IndicatorInfo info, Timeframe frame, double[] values, string?[] written)
    {
        var slow = frame is Timeframe.D1 or Timeframe.W1 or Timeframe.Mo1;
        switch (info.Name)
        {
            case "macd_line" or "macd_signal" or "macd_hist":
                if (!slow && Chance(15))
                {
                    (values[0], values[1], written[0], written[1]) = (9, 21, "fast_len", "slow_len");
                }
                else
                {
                    var fast = slow ? _random.Next(1, 3) : Pick([2, 3, 5, 8, 12]);
                    (values[0], values[1]) = (fast, fast + (slow ? _random.Next(1, 3) : Pick([1, 2, 5, 9, 14])));
                }

                values[2] = slow ? _random.Next(1, 3) : Pick([2, 3, 5, 9]);
                return;
            case "or_high" or "or_low":
                {
                    var unit = Math.Max(1, frame.Minutes());
                    values[0] = unit == 1 ? Pick([5, 15, 30, 60]) : Math.Min(375, unit * _random.Next(1, 7));
                    return;
                }
        }

        for (var i = 0; i < info.Parameters.Count; i++)
        {
            var parameter = info.Parameters[i];
            if (parameter.Name == "n")
            {
                var pool = frame is Timeframe.W1 or Timeframe.Mo1 ? new[] { 1.0, 2, 3 } : frame == Timeframe.D1 ? new[] { 2.0, 3, 5, 8 } : new[] { 2.0, 3, 4, 5, 8, 10, 14, 20 };
                var allowed = pool.Where(v => v >= parameter.Min && v <= parameter.Max).DefaultIfEmpty(parameter.Default).ToArray();
                values[i] = Pick(allowed);
                if (!slow && values[i] >= parameter.Min && Chance(12))
                {
                    var name = Chance(50) ? "fast_len" : "slow_len";
                    values[i] = Parameters[name];
                    written[i] = name;
                }
            }
            else if (parameter.Name is "k" or "mult")
            {
                values[i] = Pick([1.0, 1.5, 2, 2.5, 3]);
            }
            else
            {
                values[i] = parameter.Default;
            }
        }
    }

    // ---- helpers ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    private bool Chance(int percent) => _random.Next(100) < percent;

    private T Pick<T>(T[] items) => items[_random.Next(items.Length)];
}
