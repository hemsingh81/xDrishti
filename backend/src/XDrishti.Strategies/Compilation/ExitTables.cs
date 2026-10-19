using System.Collections.Frozen;
using XDrishti.Domain.Research;
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
/// One parameter of an exit policy, as data: the name, whether it is a scalar or a list, whether it is required, the sanity bounds of each value and whether the value must be a whole number. <paramref name="IsCatalog"/> marks
/// the parameter that is the first parameter of the feature the policy reads (<c>atr_period</c>, <c>lookback_bars</c>, <c>length</c>): it has no bounds here, the indicator catalog judges it (a whole number in the catalog's range).
/// </summary>
internal sealed record ParameterSpec(string Name, ParameterShape Shape, bool Required, double Min, bool MinInclusive, double Max, bool MaxInclusive, bool IsCatalog = false, bool Whole = false)
{
    /// <summary>The ATR period parameter every ATR-reading policy shares (optional, whole, default from the catalog).</summary>
    public static ParameterSpec AtrPeriod() => new("atr_period", ParameterShape.Scalar, false, double.NaN, false, double.NaN, false, true, true);

    /// <summary>A required parameter that is the first parameter of the feature the policy reads (<c>lookback_bars</c>, <c>length</c>): the catalog judges it.</summary>
    public static ParameterSpec Catalog(string name) => new(name, ParameterShape.Scalar, true, double.NaN, false, double.NaN, false, true, true);

    /// <summary>The bounds as an interval, <c>(0, 10]</c>; <c>catalog</c> for a parameter the indicator catalog judges.</summary>
    public string Interval => IsCatalog ? "catalog" : (MinInclusive ? "[" : "(") + Min.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "," + Max.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + (MaxInclusive ? "]" : ")");
}

/// <summary>
/// The exit policies and the overlays, as tables (name to parameter specs): a new policy is a row (specs P3-02b and P3-02b2, <i>The exit candidates</i>). Every parameter of a policy P3 does not run is ACCEPTED but
/// PROVISIONAL until P4's spec (ADR 0036 decision 15 and Amendment 1); only <c>FIXED_RR</c>, the policy the simulator runs, is final.
/// </summary>
internal static class ExitTables
{
    /// <summary>The most values in a list parameter.</summary>
    public const int MaxListValues = 10;

    /// <summary>The minutes of a regular session (09:15 to 15:30), the base of the bound of <c>intraday_bars</c>.</summary>
    public const int SessionMinutes = 375;

    /// <summary>The most sessions of a swing time stop (the range of <c>max_hold_sessions</c>).</summary>
    public const int MaxSwingSessions = 5;

    /// <summary>The policies and their parameters, in the order the parameters are reported when missing.</summary>
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
        [ExitPolicy.PartialBe] =
        [
            new("book_pct", ParameterShape.Scalar, true, 0, false, 100, false),
            new("t1_rr", ParameterShape.Scalar, true, 0, false, 10, true),
            new("t2_rr", ParameterShape.List, true, 0, false, 10, true),
        ],
        [ExitPolicy.AtrTrail] =
        [
            new("mult", ParameterShape.List, true, 0, false, 20, true),
            new("activate_at_rr", ParameterShape.Scalar, true, 0, true, 10, true),
            ParameterSpec.AtrPeriod(),
        ],
        [ExitPolicy.StructureTrail] = [ParameterSpec.Catalog("lookback_bars")],
        [ExitPolicy.EmaTrail] = [ParameterSpec.Catalog("length")],
        [ExitPolicy.Pyramid] =
        [
            new("initial_pct", ParameterShape.Scalar, true, 0, false, 100, true),
            new("add_at_rr", ParameterShape.List, true, 0, false, 10, true),
            new("add_pct", ParameterShape.List, true, 0, false, 100, true),
            new("max_adds", ParameterShape.Scalar, true, 1, true, MaxListValues, true, false, true),
            new("trail_atr_mult", ParameterShape.Scalar, true, 0, false, 20, true),
            ParameterSpec.AtrPeriod(),
        ],
    }.ToFrozenDictionary();

    /// <summary>The overlays and their parameters. <c>intraday_bars</c> and <c>swing_sessions</c> are each required by one style only and refused for the other (<see cref="ExitCompiler"/>).</summary>
    public static readonly IReadOnlyDictionary<OverlayKind, IReadOnlyList<ParameterSpec>> OverlayChecked = new Dictionary<OverlayKind, IReadOnlyList<ParameterSpec>>
    {
        [OverlayKind.TimeStop] =
        [
            new("min_progress_rr", ParameterShape.Scalar, true, 0, true, 10, true),
            new("intraday_bars", ParameterShape.Scalar, false, 1, true, SessionMinutes, true, false, true),
            new("swing_sessions", ParameterShape.Scalar, false, 1, true, MaxSwingSessions, true, false, true),
        ],
        [OverlayKind.EventExit] = [new("days_before_results", ParameterShape.Scalar, true, 1, true, 10, true, false, true)],
    }.ToFrozenDictionary();

    /// <summary>
    /// The candidate policies a simulator runs: today only <c>FIXED_RR</c>, so every other policy, and every overlay, gets the warning <c>exit_not_evaluated</c>. P4 edits this set when it builds a walker, together with the
    /// refusal in <c>Simulator.RefuseWhatItCannotRun</c>; a test asserts that the two agree.
    /// </summary>
    public static readonly IReadOnlySet<ExitPolicy> RunBySimulator = new HashSet<ExitPolicy> { ExitPolicy.FixedRr }.ToFrozenSet();

    /// <summary>The feature a policy reads on the evaluation timeframe and its first (period) parameter; null for a policy that reads nothing.</summary>
    public static (string Indicator, string Parameter)? Read(ExitPolicy policy, Side side) => policy switch
    {
        ExitPolicy.PartialTrail or ExitPolicy.AtrTrail or ExitPolicy.Pyramid => ("atr", "atr_period"),
        ExitPolicy.StructureTrail => (side == Side.Buy ? "lowest" : "highest", "lookback_bars"),
        ExitPolicy.EmaTrail => ("ema", "length"),
        _ => null,
    };

    /// <summary>The message for a parameter doc 16 once listed that schema 1 does not have, or null.</summary>
    public static string? RefusedNote(string label, string name) => (label, name) switch
    {
        ("PYRAMID", "cap_total_risk_to_initial") => "PYRAMID has no `cap_total_risk_to_initial`: capping the total open risk at the initial 1R (doc 08 section 4) is a rule that is always on.",
        ("PYRAMID", "trail") => "PYRAMID has no `trail`: the trail of the combined position is the ATR, written `trail_atr_mult` (and `atr_period`).",
        ("time_stop" or "event_exit", "enabled") => "`enabled` is not a parameter: an overlay that is listed is on.",
        _ => null,
    };

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
