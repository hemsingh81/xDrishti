using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Indicators;
using XDrishti.Strategies.Binding;
using XDrishti.Strategies.Model;

namespace XDrishti.Strategies.Compilation;

/// <summary>
/// The stop of a side (spec P3-02b, <i>The stop kinds</i>): checks the kind against the side, the fields against the kind (required, not used, bounds), resolves parameter names to numbers, and builds the level and ATR
/// features the simulator reads on the stop's timeframe. The specs are built from the catalog's default spec of the indicator and judged by the catalog (<see cref="CatalogCheck"/>), the one rule for periods, whole numbers,
/// whole bars of an opening range and timeframes. The formula of the stop price is the simulator's.
/// </summary>
internal sealed class StopCompiler(ProblemSink sink, QuantityReader quantities)
{
    private const double MaxPct = 50;

    private const double MaxMult = 20;

    private const double MaxBufferAtr = 5;

    private enum Use
    {
        NotUsed,
        Optional,
        Required,
    }

    /// <summary>The plan and the features it reads (level, then ATR); null when the stop has an error.</summary>
    public (StopPlan Plan, IReadOnlyList<FeatureSpec> Features)? Compile(StopSpec stop, Side side, Timeframe evaluationTimeframe, string path)
    {
        var errorsBefore = sink.ErrorCount;
        if (!Enum.IsDefined(stop.Kind))
        {
            sink.Error(path + ".kind", ModelCodes.UndefinedValue, "The stop kind " + (int)stop.Kind + " is not one of the stop kinds.");
            return null;
        }

        var kind = stop.Kind;
        if ((side == Side.Buy && kind is StopKind.SwingHigh or StopKind.BarHigh) || (side == Side.Sell && kind is StopKind.SwingLow or StopKind.BarLow))
        {
            sink.Error(path, ModelCodes.StopWrongSide, "The stop `" + Name(kind) + "` is for " + (side == Side.Buy ? "SELL" : "BUY") + ", not " + ModelChecks.SideName(side) + "; use `" + Name(Opposite(kind)) + "`.");
        }

        var stopTimeframe = ReadTimeframe(stop, kind, path, evaluationTimeframe);
        var pct = ReadField(stop.Pct, "pct", kind, path, 0, false, MaxPct, true);
        var mult = ReadField(stop.Mult, "mult", kind, path, 0, false, MaxMult, true);
        var lookback = ReadField(stop.Lookback, "lookback", kind, path);
        var minutes = ReadField(stop.Minutes, "minutes", kind, path);
        var buffer = ReadField(stop.BufferAtr, "buffer_atr", kind, path, 0, true, MaxBufferAtr, true);
        var atrPeriod = ReadField(stop.AtrPeriod, "atr_period", kind, path);

        var level = BuildLevel(kind, side, stopTimeframe, lookback, minutes);
        var atrRead = kind == StopKind.AtrMult || buffer > 0;
        (FeatureSpec Spec, string Field)? atr = atrRead || atrPeriod is not null ? BuildAtr(stopTimeframe, atrPeriod) : null;
        var checks = new List<(FeatureSpec Spec, string Field)>();
        if (level is { } l)
        {
            checks.Add(l);
        }

        if (atr is { } a)
        {
            checks.Add(a);
        }

        ReportCatalog(checks, path, stop.Timeframe is not null);
        if (sink.ErrorCount > errorsBefore)
        {
            return null;
        }

        var atrSpec = atrRead ? atr?.Spec : null;
        var plan = new StopPlan(kind, stopTimeframe, level?.Spec, atrSpec, kind == StopKind.FixedPct ? pct : null, kind == StopKind.AtrMult ? mult : null, buffer ?? 0);
        var features = new List<FeatureSpec>(2);
        if (level is { } read)
        {
            features.Add(read.Spec);
        }

        if (atrSpec is { } readAtr)
        {
            features.Add(readAtr);
        }

        return (plan, features.AsReadOnly());
    }

    /// <summary>The name of a stop kind as written in the strategy file (doc 11).</summary>
    public static string Name(StopKind kind) => kind switch
    {
        StopKind.FixedPct => "fixed_pct",
        StopKind.AtrMult => "atr_mult",
        StopKind.SwingLow => "swing_low",
        StopKind.SwingHigh => "swing_high",
        StopKind.BarLow => "bar_low",
        StopKind.BarHigh => "bar_high",
        StopKind.OrOpposite => "or_opposite",
        _ => throw new InvalidOperationException("Not a member of the enum."),
    };

    private static StopKind Opposite(StopKind kind) => kind switch
    {
        StopKind.SwingHigh => StopKind.SwingLow,
        StopKind.SwingLow => StopKind.SwingHigh,
        StopKind.BarHigh => StopKind.BarLow,
        StopKind.BarLow => StopKind.BarHigh,
        _ => throw new InvalidOperationException("Not a member of the enum."),
    };

    private static Use UseOf(StopKind kind, string field) => field switch
    {
        "pct" => kind == StopKind.FixedPct ? Use.Required : Use.NotUsed,
        "mult" => kind == StopKind.AtrMult ? Use.Required : Use.NotUsed,
        "lookback" => kind is StopKind.SwingLow or StopKind.SwingHigh ? Use.Required : Use.NotUsed,
        "minutes" => kind == StopKind.OrOpposite ? Use.Required : Use.NotUsed,
        "buffer_atr" => kind is StopKind.SwingLow or StopKind.SwingHigh or StopKind.BarLow or StopKind.BarHigh or StopKind.OrOpposite ? Use.Optional : Use.NotUsed,
        "atr_period" => kind == StopKind.FixedPct ? Use.NotUsed : Use.Optional,
        _ => Use.NotUsed,
    };

    private Timeframe ReadTimeframe(StopSpec stop, StopKind kind, string path, Timeframe evaluationTimeframe)
    {
        if (stop.Timeframe is not { } timeframe)
        {
            return evaluationTimeframe;
        }

        if (kind == StopKind.FixedPct)
        {
            sink.Error(path + ".timeframe", ModelCodes.FieldNotUsed, "The stop `fixed_pct` reads no bars, so `timeframe` is not used. Leave it out.");
            return evaluationTimeframe;
        }

        if (!Enum.IsDefined(timeframe))
        {
            sink.Error(path + ".timeframe", ModelCodes.UndefinedValue, "The timeframe " + (int)timeframe + " is not one of the timeframes.");
            return evaluationTimeframe;
        }

        return timeframe;
    }

    /// <summary>The number of a field: null when the field is absent or has a problem (reported). Required fields that are absent are <c>field_missing</c>, fields the kind does not use are <c>field_not_used</c>.</summary>
    private double? ReadField(Quantity? quantity, string field, StopKind kind, string path, double min = double.NaN, bool minInclusive = false, double max = double.NaN, bool maxInclusive = false)
    {
        var use = UseOf(kind, field);
        var fieldPath = path + "." + field;
        if (quantity is not { } given)
        {
            if (use == Use.Required)
            {
                sink.Error(fieldPath, ModelCodes.FieldMissing, "The stop `" + Name(kind) + "` needs `" + field + "`.");
            }

            return null;
        }

        if (use == Use.NotUsed)
        {
            sink.Error(fieldPath, ModelCodes.FieldNotUsed, "The stop `" + Name(kind) + "` does not use `" + field + "`. Leave it out.");
            return null;
        }

        if (!quantities.TryRead(given, fieldPath, out var value))
        {
            return null;
        }

        if (!double.IsNaN(max) && !(AtLeast(value, min, minInclusive) && (maxInclusive ? value <= max : value < max)))
        {
            sink.Error(fieldPath, ModelCodes.ValueOutOfRange, "`" + field + "` is " + Format(value) + ", but it must be in " + (minInclusive ? "[" : "(") + Format(min) + ", " + Format(max) + (maxInclusive ? "]" : ")") + ".");
            return null;
        }

        return value;
    }

    private static bool AtLeast(double value, double min, bool inclusive) => inclusive ? value >= min : value > min;

    /// <summary>The feature that gives the level of the stop, and the field its first parameter came from (empty when it has none).</summary>
    private static (FeatureSpec Spec, string Field)? BuildLevel(StopKind kind, Side side, Timeframe timeframe, double? lookback, double? minutes) => kind switch
    {
        StopKind.SwingLow when lookback is { } n => (Default("lowest", timeframe) with { P1 = n }, "lookback"),
        StopKind.SwingHigh when lookback is { } n => (Default("highest", timeframe) with { P1 = n }, "lookback"),
        StopKind.BarLow => (Default("low", timeframe), string.Empty),
        StopKind.BarHigh => (Default("high", timeframe), string.Empty),
        StopKind.OrOpposite when minutes is { } m => (Default(side == Side.Buy ? "or_low" : "or_high", timeframe) with { P1 = m }, "minutes"),
        _ => null,
    };

    /// <summary>The ATR on the stop's timeframe, with the given period or the catalog's default.</summary>
    private static (FeatureSpec Spec, string Field) BuildAtr(Timeframe timeframe, double? atrPeriod)
    {
        var spec = Default("atr", timeframe);
        return (atrPeriod is { } period ? spec with { P1 = period } : spec, "atr_period");
    }

    private static FeatureSpec Default(string name, Timeframe timeframe) =>
        IndicatorCatalog.TryGet(name, out var info) ? info.DefaultSpec(timeframe) : throw new InvalidOperationException("The indicator catalog has no `" + name + "`.");

    /// <summary>Asks the catalog about each spec and reports what it refuses as <c>feature_invalid</c> at the field the spec's parameter came from.</summary>
    private void ReportCatalog(List<(FeatureSpec Spec, string Field)> specs, string path, bool timeframeGiven)
    {
        var reported = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (spec, firstField) in specs)
        {
            foreach (var error in CatalogCheck.Answer(spec))
            {
                var field = CatalogCheck.FieldOf(error);
                var at = field == "timeframe" ? (timeframeGiven ? path + ".timeframe" : path) : field == "p1" && firstField.Length > 0 ? path + "." + firstField : path;
                if (reported.Add(at + "|" + error.Message))
                {
                    sink.Error(at, ModelCodes.FeatureInvalid, error.Message);
                }
            }
        }
    }

    private static string Format(double value) => value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
}
