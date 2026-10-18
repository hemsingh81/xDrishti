using System.Collections.Frozen;
using XDrishti.Strategies.Model;

namespace XDrishti.Strategies.Compilation;

/// <summary>Whether a parameter of an exit takes one value or a list of candidate values.</summary>
internal enum ParameterShape
{
    /// <summary>Exactly one value.</summary>
    Scalar,

    /// <summary>One to ten candidate values.</summary>
    List,
}

/// <summary>
/// One parameter of an exit policy, as data: the name, whether it is a scalar or a list, whether it is required and the sanity bounds of each value. <paramref name="IsAtrPeriod"/> marks the period of the ATR the policy
/// reads: it has no bounds here, the indicator catalog judges it (a whole number in the catalog's range) and it defaults to the catalog's default.
/// </summary>
internal sealed record ParameterSpec(string Name, ParameterShape Shape, bool Required, double Min, bool MinInclusive, double Max, bool MaxInclusive, bool IsAtrPeriod = false)
{
    /// <summary>The ATR period parameter every ATR-reading policy shares (optional, whole, default from the catalog).</summary>
    public static ParameterSpec AtrPeriod() => new("atr_period", ParameterShape.Scalar, false, double.NaN, false, double.NaN, false, true);

    /// <summary>The bounds as an interval, <c>(0, 10]</c>; empty for the ATR period.</summary>
    public string Interval => IsAtrPeriod ? "catalog" : (MinInclusive ? "[" : "(") + Min.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "," + Max.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + (MaxInclusive ? "]" : ")");
}

/// <summary>
/// The exit policies this slice checks, as a table (name to parameter specs): a new policy is a row (spec P3-02b, <i>The exit candidates</i>). Every policy not in the table gets the shape check only until P3-02b2 writes its
/// table (together with P4, so that the names are not guessed). Every parameter of a policy P3 does not run is provisional (ADR 0036).
/// </summary>
internal static class ExitTables
{
    /// <summary>The checked policies and their parameters, in the order the parameters are reported when missing.</summary>
    public static readonly IReadOnlyDictionary<ExitPolicy, IReadOnlyList<ParameterSpec>> Checked = new Dictionary<ExitPolicy, IReadOnlyList<ParameterSpec>>
    {
        [ExitPolicy.FixedRr] = [new("rr", ParameterShape.List, true, 0, false, 10, true)],
        [ExitPolicy.PartialTrail] =
        [
            new("book_pct", ParameterShape.Scalar, true, 0, false, 100, false),
            new("t1_rr", ParameterShape.Scalar, true, 0, false, 10, true),
            new("trail_atr_mult", ParameterShape.Scalar, true, 0, false, 20, true),
            ParameterSpec.AtrPeriod(),
        ],
    }.ToFrozenDictionary();

    /// <summary>The most values in a list parameter.</summary>
    public const int MaxListValues = 10;

    /// <summary>The name of a policy as written in the strategy file and in doc 08 (<c>FIXED_RR</c>).</summary>
    public static string PolicyName(ExitPolicy policy) => policy switch
    {
        ExitPolicy.FixedRr => "FIXED_RR",
        ExitPolicy.PartialBe => "PARTIAL_BE",
        ExitPolicy.PartialTrail => "PARTIAL_TRAIL",
        ExitPolicy.AtrTrail => "ATR_TRAIL",
        ExitPolicy.StructureTrail => "STRUCTURE_TRAIL",
        ExitPolicy.EmaTrail => "EMA_TRAIL",
        ExitPolicy.Pyramid => "PYRAMID",
        _ => throw new InvalidOperationException("Not a member of the enum."),
    };

    /// <summary>The name of an overlay as written in the strategy file (<c>time_stop</c>).</summary>
    public static string OverlayName(OverlayKind kind) => kind == OverlayKind.TimeStop ? "time_stop" : "event_exit";
}
