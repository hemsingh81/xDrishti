using XDrishti.Strategies.Syntax;
using static XDrishti.Strategies.Tests.Support.Ast;

namespace XDrishti.Strategies.Tests.Syntax;

/// <summary>
/// The valid sources and the trees written by hand for them (precedence, associativity, every literal form, the dotted spellings, tf(), named arguments, nested calls and subscripts). The expected trees come from the
/// grammar of the spec, not from the parser.
/// </summary>
public static class ValidSources
{
    public static readonly IReadOnlyDictionary<string, Expression> Cases = new Dictionary<string, Expression>
    {
        // literals
        ["55"] = N(55),
        ["0"] = N(0),
        ["0.5"] = N(0.5),
        ["1e-3"] = N(0.001),
        ["1e3"] = N(1000),
        ["2.5e+2"] = N(250),
        ["007"] = N(7),
        ["-0"] = Neg(N(0)),
        ["-1e-3"] = Neg(N(0.001)),
        ["1e308"] = N(1e308),
        ["\"09:45\""] = S("09:45"),
        ["\"\""] = S(string.Empty),
        ["\"UP_LOW_VOL\""] = S("UP_LOW_VOL"),
        ["\"भारत\""] = S("भारत"),
        ["true"] = B(true),
        ["false"] = B(false),

        // references, including the dotted spellings and the timeframe qualifier
        ["close"] = R("close"),
        ["_x1"] = R("_x1"),
        ["MON"] = R("MON"),
        ["time"] = R("time"),
        ["prev_day.high"] = R("prev_day", "high"),
        ["today.open"] = R("today", "open"),
        ["index.trend"] = R("index", "trend"),
        ["tf(\"1h\").close"] = Tf("1h", "close"),
        ["tf(\"1D\").prev_day_high"] = Tf("1D", "prev_day_high"),
        ["tf ( \"1W\" ) . close"] = Tf("1W", "close"),

        // calls
        ["rsi(14)"] = C("rsi", N(14)),
        ["rsi()"] = C("rsi"),
        ["regime()"] = C("regime"),
        ["bb_upper(20, 2)"] = C("bb_upper", N(20), N(2)),
        ["bb_upper(20,2)"] = C("bb_upper", N(20), N(2)),
        ["ema(20, source=high)"] = C("ema", N(20), Named("source", "high")),
        ["ema(source=low)"] = C("ema", Named("source", "low")),
        ["ema(source = low)"] = C("ema", Named("source", "low")),
        ["tf(\"1h\").ema(20)"] = TfC("1h", "ema", N(20)),
        ["tf(\"1W\").rsi()"] = TfC("1W", "rsi"),
        ["crosses_above(close, vwap)"] = C("crosses_above", R("close"), R("vwap")),
        ["between(rsi(14), 30, 70)"] = C("between", C("rsi", N(14)), N(30), N(70)),
        ["max(abs(close - open), 1)"] = C("max", C("abs", Sub(R("close"), R("open"))), N(1)),
        ["f(g(h(1)))"] = C("f", C("g", C("h", N(1)))),
        ["f(a + 1, -b, not c)"] = C("f", Add(R("a"), N(1)), Neg(R("b")), Not(R("c"))),
        ["f(a < b, c)"] = C("f", Lt(R("a"), R("b")), R("c")),
        ["rising(close, 3)"] = C("rising", R("close"), N(3)),
        ["between(close, prev_day.low, prev_day.high)"] = C("between", R("close"), R("prev_day", "low"), R("prev_day", "high")),
        ["crosses_above(close[1], tf(\"1h\").close[1])"] = C("crosses_above", Idx(R("close"), 1), Idx(Tf("1h", "close"), 1)),

        // subscripts
        ["close[1]"] = Idx(R("close"), 1),
        ["close [ 1 ]"] = Idx(R("close"), 1),
        ["close[255]"] = Idx(R("close"), 255),
        ["close[0]"] = Idx(R("close"), 0),
        ["close[-1]"] = Idx(R("close"), -1),
        ["tf(\"1h\").close[2]"] = Idx(Tf("1h", "close"), 2),
        ["prev_day.high[1]"] = Idx(R("prev_day", "high"), 1),
        ["-x[1]"] = Neg(Idx(R("x"), 1)),

        // precedence and associativity
        ["a or b and c"] = Or(R("a"), And(R("b"), R("c"))),
        ["a and b or c"] = Or(And(R("a"), R("b")), R("c")),
        ["a and b and c"] = And(And(R("a"), R("b")), R("c")),
        ["a or b or c"] = Or(Or(R("a"), R("b")), R("c")),
        ["not a and b"] = And(Not(R("a")), R("b")),
        ["not a or b"] = Or(Not(R("a")), R("b")),
        ["a and not b"] = And(R("a"), Not(R("b"))),
        ["not not a"] = Not(Not(R("a"))),
        ["not (a and b)"] = Not(And(R("a"), R("b"))),
        ["not a < b"] = Not(Lt(R("a"), R("b"))),
        ["-x * y"] = Mul(Neg(R("x")), R("y")),
        ["- - a"] = Neg(Neg(R("a"))),
        ["-(-a)"] = Neg(Neg(R("a"))),
        ["a - b - c"] = Sub(Sub(R("a"), R("b")), R("c")),
        ["a / b * c"] = Mul(Div(R("a"), R("b")), R("c")),
        ["a + b * c"] = Add(R("a"), Mul(R("b"), R("c"))),
        ["a * b + c * d"] = Add(Mul(R("a"), R("b")), Mul(R("c"), R("d"))),
        ["(a + b) * c"] = Mul(Add(R("a"), R("b")), R("c")),
        ["a - (b - c)"] = Sub(R("a"), Sub(R("b"), R("c"))),
        ["a - -b"] = Sub(R("a"), Neg(R("b"))),
        ["1 - -1"] = Sub(N(1), Neg(N(1))),
        ["a-b"] = Sub(R("a"), R("b")),
        ["a -b"] = Sub(R("a"), R("b")),
        ["a + b < c - d"] = Lt(Add(R("a"), R("b")), Sub(R("c"), R("d"))),
        ["a < b and c > d or e == f"] = Or(And(Lt(R("a"), R("b")), Gt(R("c"), R("d"))), Bin(BinaryOperator.Equal, R("e"), R("f"))),
        ["a and (b or c)"] = And(R("a"), Or(R("b"), R("c"))),
        ["((a))"] = R("a"),
        ["(a)"] = R("a"),
        ["a < b"] = Lt(R("a"), R("b")),
        ["a<b"] = Lt(R("a"), R("b")),
        ["a <= b"] = Bin(BinaryOperator.LessOrEqual, R("a"), R("b")),
        ["a > b"] = Gt(R("a"), R("b")),
        ["a >= b"] = Bin(BinaryOperator.GreaterOrEqual, R("a"), R("b")),
        ["a == b"] = Bin(BinaryOperator.Equal, R("a"), R("b")),
        ["a != b"] = Bin(BinaryOperator.NotEqual, R("a"), R("b")),
        ["(a < b) == (c < d)"] = Bin(BinaryOperator.Equal, Lt(R("a"), R("b")), Lt(R("c"), R("d"))),
        ["1e-3*2"] = Mul(N(0.001), N(2)),

        // the conditions of the design documents
        ["rsi(14) > 55"] = Gt(C("rsi", N(14)), N(55)),
        ["  rsi(14)\n>\t55  "] = Gt(C("rsi", N(14)), N(55)),
        ["rsi(14) > 55 and close > vwap"] = And(Gt(C("rsi", N(14)), N(55)), Gt(R("close"), R("vwap"))),
        ["time >= \"09:45\""] = Bin(BinaryOperator.GreaterOrEqual, R("time"), S("09:45")),
        ["regime() == \"UP_LOW_VOL\""] = Bin(BinaryOperator.Equal, C("regime"), S("UP_LOW_VOL")),
        ["tf(\"1h\").ema(20) > tf(\"1h\").ema(50)"] = Gt(TfC("1h", "ema", N(20)), TfC("1h", "ema", N(50))),
        ["day_of_week == TUE"] = Bin(BinaryOperator.Equal, R("day_of_week"), R("TUE")),
        ["sma(20) > sma(50) and rsi(14) < 70 or not (close > vwap)"] =
            Or(And(Gt(C("sma", N(20)), C("sma", N(50))), Lt(C("rsi", N(14)), N(70))), Not(Gt(R("close"), R("vwap")))),
    };

    /// <summary>The sources, as theory data: one test case per source.</summary>
    public static TheoryData<string> Sources()
    {
        var data = new TheoryData<string>();
        foreach (var source in Cases.Keys)
        {
            data.Add(source);
        }

        return data;
    }
}
