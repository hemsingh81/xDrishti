using System.Collections.Immutable;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Strategies.Binding;
using XDrishti.Strategies.Lint;
using XDrishti.Strategies.Model;

namespace XDrishti.Strategies.Compilation;

/// <summary>The checks of the definition level, whichever side is compiled: the identity, the style, the timeframes, the sides, the parameters and the filters (spec P3-02b, criteria 3, 4, 5 and 7).</summary>
internal sealed class ModelChecks(ProblemSink sink)
{
    private const int MaxIdLength = 64;

    private const int MaxParamNameLength = 32;

    /// <summary>The entry timeframes (doc 07 §4).</summary>
    public static readonly ImmutableArray<Timeframe> EntryTimeframes = [Timeframe.M5, Timeframe.M10, Timeframe.M15, Timeframe.M30, Timeframe.H1, Timeframe.D1];

    /// <summary>The path of a field of the enum kind: <c>style</c>, <c>sides[1]</c>.</summary>
    public static string Index(string path, int index) => path + "[" + index + "]";

    /// <summary>Schema, id, style, timeframes and sides, in that order. Returns the sides the strategy declares (the defined ones).</summary>
    public IReadOnlySet<Side> CheckIdentity(StrategyDefinition definition, Timeframe evaluationTimeframe, Side side)
    {
        if (definition.Schema != StrategyDefinition.CurrentSchema)
        {
            sink.Error("schema", ModelCodes.SchemaUnsupported, "Schema " + definition.Schema + " is not supported; this library reads schema " + StrategyDefinition.CurrentSchema + ".");
        }

        if (!IsValidId(definition.Id))
        {
            sink.Error("id", ModelCodes.BadId, "The id must be 1 to " + MaxIdLength + " characters, a letter and then letters, digits or underscores (for example ORB or VWAP_RSI_TREND).");
        }

        var styleKnown = Enum.IsDefined(definition.Style);
        if (!styleKnown)
        {
            sink.Error("style", ModelCodes.UndefinedValue, "The style " + (int)definition.Style + " is not intraday or swing.");
        }

        CheckTimeframes(definition, styleKnown, evaluationTimeframe);
        return CheckSides(definition, styleKnown, side);
    }

    /// <summary>The parameters: at most four, each a name and a finite number. Returns what the conditions and fields are offered.</summary>
    public DeclaredParams CheckParams(StrategyDefinition definition)
    {
        var list = definition.Params ?? [];
        var values = new Dictionary<string, double>(StringComparer.Ordinal);
        if (list.Count > StrategyDefinition.MaxParams)
        {
            sink.Error("params", ModelCodes.TooManyParams, "A strategy has at most " + StrategyDefinition.MaxParams + " parameters, but " + list.Count + " are declared.");
        }

        for (var i = 0; i < list.Count; i++)
        {
            var path = Index("params", i);
            var parameter = list[i];
            if (parameter is null)
            {
                sink.Error(path, ModelCodes.BadParamName, "The parameter is missing.");
                continue;
            }

            var name = parameter.Name;
            var nameOk = IsValidParamName(name);
            if (!nameOk)
            {
                sink.Error(path, ModelCodes.BadParamName, "A parameter name is 1 to " + MaxParamNameLength + " characters, a lower-case letter and then lower-case letters, digits or underscores (for example rsi_len)" + (name is null ? "." : ", not " + BindingScope.Quote(name) + "."));
            }

            if (!string.IsNullOrEmpty(name) && !values.TryAdd(name, parameter.Value))
            {
                sink.Error(path, ModelCodes.DuplicateParam, "The parameter " + BindingScope.Quote(name) + " is declared twice; the first is used.");
            }

            if (!double.IsFinite(parameter.Value))
            {
                sink.Error(path, ModelCodes.BadParamValue, "The value of a parameter must be a finite number.");
            }

            if (nameOk && Vocabulary.IsLanguageName(name!))
            {
                sink.Warning(path, ModelCodes.ParamShadowsName, "The parameter `" + name + "` has the name of a feature, a function or `time`: in every condition the parameter is read, not the feature.");
            }
        }

        return new DeclaredParams(values);
    }

    /// <summary>The filter names: each one of the closed set (a warning, nothing evaluates them in P3), none twice. Returns the names that are known, once each.</summary>
    public IReadOnlyList<string> CheckFilters(StrategyDefinition definition)
    {
        var list = definition.Filters ?? [];
        var known = new List<string>();
        for (var i = 0; i < list.Count; i++)
        {
            var path = Index("filters", i);
            var name = list[i];
            if (name is null || !FilterNames.All.Contains(name))
            {
                var close = name is null ? [] : Suggestions.Closest(name, FilterNames.All);
                sink.Error(
                    path,
                    ModelCodes.UnknownFilter,
                    (name is null ? "The filter name is missing" : BindingScope.Quote(name) + " is not a filter") + ": the filters are " + string.Join(" and ", FilterNames.All.Select(n => "`" + n + "`")) + "." + (close.Count > 0 ? " Did you mean `" + close[0] + "`?" : string.Empty),
                    close.Count > 0 ? close[0] : null);
            }
            else if (known.Contains(name))
            {
                sink.Error(path, ModelCodes.DuplicateFilter, "The filter `" + name + "` is listed twice.");
            }
            else
            {
                known.Add(name);
                sink.Warning(
                    path,
                    ModelCodes.FilterNotEvaluated,
                    name == FilterNames.NotEventDay
                        ? "The filter `not_event_day` is not evaluated: there is no results calendar yet (P15), so event days are not excluded."
                        : "The filter `min_turnover` is not evaluated: there is no liquidity data of the universe layer yet (doc 10 §2).");
            }
        }

        return known.AsReadOnly();
    }

    private static bool IsValidId(string? id)
    {
        if (string.IsNullOrEmpty(id) || id.Length > MaxIdLength || !char.IsAsciiLetter(id[0]))
        {
            return false;
        }

        return id.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');
    }

    private static bool IsValidParamName(string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > MaxParamNameLength || !char.IsAsciiLetterLower(name[0]))
        {
            return false;
        }

        return name.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '_');
    }

    /// <summary>Whether a name is a parameter or field name of the shape <c>[a-z][a-z0-9_]*</c> of 1 to 32 characters (used for the names of exit parameters too).</summary>
    public static bool IsName(string? name) => IsValidParamName(name);

    private void CheckTimeframes(StrategyDefinition definition, bool styleKnown, Timeframe evaluationTimeframe)
    {
        var list = definition.Timeframes ?? [];
        if (list.Count == 0)
        {
            sink.Error("timeframes", ModelCodes.TimeframesEmpty, "The strategy lists no timeframe; the entry timeframes are 5m, 10m, 15m, 30m, 1h and 1D.");
        }

        var seen = new HashSet<Timeframe>();
        for (var i = 0; i < list.Count; i++)
        {
            var path = Index("timeframes", i);
            var timeframe = list[i];
            if (!EntryTimeframes.Contains(timeframe))
            {
                sink.Error(path, ModelCodes.BadTimeframe, (Enum.IsDefined(timeframe) ? "`" + timeframe.ToCode() + "`" : "The timeframe " + (int)timeframe) + " is not an entry timeframe: the entry timeframes are 5m, 10m, 15m, 30m, 1h and 1D (doc 07 §4).");
                continue;
            }

            if (!seen.Add(timeframe))
            {
                sink.Error(path, ModelCodes.DuplicateTimeframe, "`" + timeframe.ToCode() + "` is listed twice.");
                continue;
            }

            if (styleKnown && definition.Style == Style.Intraday && timeframe == Timeframe.D1)
            {
                sink.Error(path, ModelCodes.StyleTimeframeMismatch, "An intraday strategy does not run on 1D: it is flat by the square-off time. Use the swing style, or a timeframe of 5m to 1h.");
            }
            else if (styleKnown && definition.Style == Style.Swing && timeframe is Timeframe.M5 or Timeframe.M10 or Timeframe.M15)
            {
                sink.Error(path, ModelCodes.StyleTimeframeMismatch, "A swing strategy runs on 30m, 1h or 1D, not on `" + timeframe.ToCode() + "` (doc 07 §3 and §4). Use the intraday style, or a longer timeframe.");
            }
        }

        if (list.Count > 0 && !list.Contains(evaluationTimeframe))
        {
            sink.Error("timeframes", ModelCodes.TimeframeNotDeclared, "The strategy does not list `" + evaluationTimeframe.ToCode() + "` in its timeframes, so it cannot be compiled for it.");
        }
    }

    private HashSet<Side> CheckSides(StrategyDefinition definition, bool styleKnown, Side side)
    {
        var list = definition.Sides ?? [];
        var declared = new HashSet<Side>();
        if (list.Count == 0)
        {
            sink.Error("sides", ModelCodes.SidesEmpty, "The strategy lists no side: write BUY, SELL or both.");
        }

        for (var i = 0; i < list.Count; i++)
        {
            var path = Index("sides", i);
            if (!Enum.IsDefined(list[i]))
            {
                sink.Error(path, ModelCodes.UndefinedValue, "The side " + (int)list[i] + " is not BUY or SELL.");
            }
            else if (!declared.Add(list[i]))
            {
                sink.Error(path, ModelCodes.DuplicateSide, "The side " + SideName(list[i]) + " is listed twice.");
            }
            else if (styleKnown && definition.Style == Style.Swing && list[i] == Side.Sell)
            {
                sink.Error(path, ModelCodes.SwingSellUnsupported, "A swing strategy cannot sell: swing SELL is futures only and there are no futures yet (doc 07 §7).");
            }
        }

        if (list.Count > 0 && !declared.Contains(side))
        {
            sink.Error("sides", ModelCodes.SideNotDeclared, "The strategy does not list " + SideName(side) + " in its sides, so that side cannot be compiled.");
        }

        if (list.Count > 0)
        {
            if (definition.Buy is not null && !declared.Contains(Side.Buy))
            {
                sink.Warning("buy", ModelCodes.SideRulesUnused, "The strategy has BUY rules but does not list BUY in its sides: they are not used.");
            }

            if (definition.Sell is not null && !declared.Contains(Side.Sell))
            {
                sink.Warning("sell", ModelCodes.SideRulesUnused, "The strategy has SELL rules but does not list SELL in its sides: they are not used.");
            }
        }

        return declared;
    }

    public static string SideName(Side side) => side == Side.Buy ? "BUY" : "SELL";
}
