using XDrishti.Indicators;

namespace XDrishti.Strategies.Binding;

/// <summary>
/// The closed vocabulary of the rules language besides the catalog: the builtin functions with their parameter names, the constants <c>MON … SUN</c>, the keyword <c>time</c>, the dotted spellings
/// (<c>prev_day.high</c>, <c>today.open</c>, <c>index.trend</c>) and the four price sources of <c>source=</c>. Everything an identifier can mean is in here, in the catalog or in the strategy parameters; nothing else
/// is ever looked up, so no text can reach anything by name.
/// </summary>
internal static class Vocabulary
{
    /// <summary>The keyword for the India clock time at which the evaluated bar closed.</summary>
    public const string TimeKeyword = "time";

    /// <summary>The word that qualifies a feature with a timeframe: <c>tf("1h").close</c>.</summary>
    public const string TimeframeQualifier = "tf";

    /// <summary>The one named argument: <c>ema(20, source=high)</c>.</summary>
    public const string SourceArgument = "source";

    private static readonly string[] BuiltinNameList = ["crosses_above", "crosses_below", "rising", "falling", "between", "abs", "min", "max", "regime"];

    private static readonly string[] ConstantNameList = ["MON", "TUE", "WED", "THU", "FRI", "SAT", "SUN"];

    private static readonly string[] ScopeNameList = ["prev_day", "today", "index"];

    private static readonly string[] PrevDayMembers = ["open", "high", "low", "close", "volume"];

    private static readonly string[] TodayMembers = ["open", "high", "low"];

    private static readonly string[] IndexMembers = ["trend", "adx"];

    /// <summary>The four prices <c>source=</c> may name (volume is the feature <c>volume</c>, not a price source).</summary>
    public static readonly IReadOnlyList<string> PriceSourceNames = ["close", "open", "high", "low"];

    /// <summary>The names of the builtin functions.</summary>
    public static IEnumerable<string> BuiltinNames => BuiltinNameList;

    /// <summary>The names of the constants.</summary>
    public static IEnumerable<string> ConstantNames => ConstantNameList;

    /// <summary>The scopes of the dotted spellings.</summary>
    public static IEnumerable<string> ScopeNames => ScopeNameList;

    public static bool TryBuiltin(string name, out Builtin builtin)
    {
        var index = Array.IndexOf(BuiltinNameList, name);
        builtin = (Builtin)Math.Max(index, 0);
        return index >= 0;
    }

    /// <summary>The value of <c>MON … SUN</c>: 1 to 7.</summary>
    public static bool TryConstant(string name, out int value)
    {
        var index = Array.IndexOf(ConstantNameList, name);
        value = index + 1;
        return index >= 0;
    }

    /// <summary>The names that may follow the dot after the scope.</summary>
    public static bool TryScope(string scope, out IReadOnlyList<string> members)
    {
        members = scope switch
        {
            "prev_day" => PrevDayMembers,
            "today" => TodayMembers,
            "index" => IndexMembers,
            _ => [],
        };
        return members.Count > 0;
    }

    /// <summary>Whether the name is a price source for <c>source=</c>.</summary>
    public static bool TryPriceSource(string name, out PriceSource source)
    {
        source = name switch
        {
            "close" => PriceSource.Close,
            "open" => PriceSource.Open,
            "high" => PriceSource.High,
            "low" => PriceSource.Low,
            _ => PriceSource.Default,
        };
        return source != PriceSource.Default;
    }

    /// <summary>Whether a strategy parameter of this name would hide something: a builtin, a constant, <c>time</c> or a catalog feature.</summary>
    public static bool IsLanguageName(string name) =>
        name == TimeKeyword || Array.IndexOf(BuiltinNameList, name) >= 0 || Array.IndexOf(ConstantNameList, name) >= 0 || IndicatorCatalog.TryGet(name, out _);

    /// <summary>The parameter names of a builtin, in order (the names the messages use).</summary>
    public static IReadOnlyList<string> ParameterNames(Builtin builtin) => builtin switch
    {
        Builtin.CrossesAbove or Builtin.CrossesBelow => ["a", "b"],
        Builtin.Rising or Builtin.Falling => ["x", "n"],
        Builtin.Between => ["x", "low", "high"],
        Builtin.Abs => ["x"],
        Builtin.Min or Builtin.Max => ["a", "b"],
        _ => [],
    };

    /// <summary>The name of a builtin as written in a condition.</summary>
    public static string NameOf(Builtin builtin) => builtin switch
    {
        Builtin.CrossesAbove => "crosses_above",
        Builtin.CrossesBelow => "crosses_below",
        Builtin.Rising => "rising",
        Builtin.Falling => "falling",
        Builtin.Between => "between",
        Builtin.Abs => "abs",
        Builtin.Min => "min",
        Builtin.Max => "max",
        _ => "regime",
    };
}
