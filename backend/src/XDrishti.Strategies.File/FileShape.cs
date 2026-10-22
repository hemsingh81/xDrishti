using System.Text.RegularExpressions;
using XDrishti.Domain.Market.Bars;
using XDrishti.Strategies.Canonical;
using XDrishti.Strategies.Compilation;

namespace XDrishti.Strategies.File;

/// <summary>
/// The shape of the strategy file as one closed table (P3-02c D6): the key names of each object and the lexical rules of the scalars. The mapper, the JSON Schema generator and the writer all read it, so a field cannot exist in one
/// and not in another. The names of enum members come from <see cref="StrategyNames"/> and the parameters of the exit objects from <see cref="ExitParameterInfo"/>.
/// </summary>
internal static partial class FileShape
{
    /// <summary>The root keys.</summary>
    public static readonly string[] Root = ["strategy", "test", "universe"];

    /// <summary>The keys of <c>strategy</c> that are not rules of a side, in the order the writer writes them (<c>sell</c> comes after the rules of the BUY side).</summary>
    public static readonly string[] Header = ["schema", "id", "version", "style", "timeframes", "sides", "params"];

    /// <summary>The keys of a side, in writing order.</summary>
    public static readonly string[] Side = ["setup", "trigger", "invalidate", "entry", "stop", "exits", "overlays"];

    /// <summary>The keys after the sides.</summary>
    public static readonly string[] Trailer = ["time_rules", "filters"];

    /// <summary>The sections that are read and reported but not validated here.</summary>
    public static readonly string[] Opaque = ["test", "universe"];

    /// <summary>The fields of a stop besides <c>type</c>: <c>timeframe</c> and the quantities, in writing order.</summary>
    public static readonly string[] StopFields = ["timeframe", "pct", "mult", "lookback", "minutes", "buffer_atr", "atr_period"];

    /// <summary>The keys of <c>time_rules</c>.</summary>
    public static readonly string[] TimeFields = ["first_entry", "last_entry", "square_off", "max_hold_sessions"];

    /// <summary>The key of the SELL side.</summary>
    public const string SellKey = "sell";

    /// <summary>The names the timeframes are spelt with.</summary>
    public static IReadOnlyList<string> TimeframeCodes => [.. Enum.GetValues<Timeframe>().Select(t => t.ToCode())];

    /// <summary>The words that are never read as the name of a parameter (they would be read as something else by another YAML tool).</summary>
    public static readonly HashSet<string> Reserved = ["yes", "no", "on", "off", "true", "false", "null", "inf", "nan"];

    /// <summary>A plain decimal number.</summary>
    public const string NumberPattern = "-?(0|[1-9][0-9]*)(\\.[0-9]+)?([eE][+-]?[0-9]+)?";

    /// <summary>The name of a strategy parameter.</summary>
    public const string ParamNamePattern = "[a-z][a-z0-9_]{0,31}";

    /// <summary>A time <c>HH:mm</c>.</summary>
    public const string TimePattern = "([01][0-9]|2[0-3]):[0-5][0-9]";

    /// <summary>Whether the text is a plain decimal number.</summary>
    public static bool IsNumber(string text) => NumberRegex().IsMatch(text);

    /// <summary>Whether the text can be the name of a parameter.</summary>
    public static bool IsParamName(string text) => ParamRegex().IsMatch(text) && !Reserved.Contains(text);

    /// <summary>Whether the text is a time <c>HH:mm</c>.</summary>
    public static bool IsTime(string text) => TimeRegex().IsMatch(text);

    [GeneratedRegex("^" + NumberPattern + "\\z", RegexOptions.CultureInvariant)]
    private static partial Regex NumberRegex();

    [GeneratedRegex("^" + ParamNamePattern + "\\z", RegexOptions.CultureInvariant)]
    private static partial Regex ParamRegex();

    [GeneratedRegex("^" + TimePattern + "\\z", RegexOptions.CultureInvariant)]
    private static partial Regex TimeRegex();
}
