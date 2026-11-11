using System.Globalization;

namespace XDrishti.Domain.Selection;

/// <summary>
/// The owner's basket settings (spec BK-08, ADR 0260): where the market gate reads its breadth from, the cap on open risk per correlation cluster, and the correlation at which clusters are cut. The defaults are the old behaviour: the tracked set, no cap, 0.70.
/// A value is stored as text; the newest row per key wins and no row means the default.
/// </summary>
/// <param name="BreadthSource"><c>tracked</c> or <c>basket:&lt;id&gt;</c>.</param>
/// <param name="MaxClusterRiskPct">Open risk per cluster, percent of capital; null is OFF.</param>
/// <param name="ClusterRho">The cut of the clusters.</param>
public sealed record BasketSettings(string BreadthSource, double? MaxClusterRiskPct, double ClusterRho)
{
    /// <summary>The key of the gate's breadth source.</summary>
    public const string BreadthSourceKey = "gate.breadth_source";

    /// <summary>The key of the cluster cap.</summary>
    public const string MaxClusterRiskKey = "selection.max_cluster_risk_pct";

    /// <summary>The key of the cluster cut.</summary>
    public const string ClusterRhoKey = "selection.cluster_rho";

    /// <summary>The breadth of the owner's tracked set (P3-01e), the behaviour before BK-08.</summary>
    public const string Tracked = "tracked";

    /// <summary>The stored text of a cap that is OFF.</summary>
    public const string Off = "none";

    private const string BasketPrefix = "basket:";

    /// <summary>The defaults: everything OFF.</summary>
    public static BasketSettings Defaults { get; } = new(Tracked, null, 0.70);

    /// <summary>The basket whose breadth the gate reads; null while the source is the tracked set.</summary>
    public Guid? GateBasketId => BasketOf(BreadthSource);

    /// <summary>The source text of a basket.</summary>
    public static string SourceOf(Guid basketId) => BasketPrefix + basketId.ToString("D", CultureInfo.InvariantCulture);

    /// <summary>True for <c>tracked</c> or <c>basket:&lt;non-empty id&gt;</c>.</summary>
    public static bool SourceIsValid(string? source) => source == Tracked || BasketOf(source) is not null;

    /// <summary>The stored text of the cap.</summary>
    public static string CapText(double? cap) => cap is { } c ? c.ToString("0.####", CultureInfo.InvariantCulture) : Off;

    /// <summary>A number as stored.</summary>
    public static string Text(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    /// <summary>The stored text of a key's value.</summary>
    public string? ValueOf(string key) => key switch
    {
        BreadthSourceKey => BreadthSource,
        MaxClusterRiskKey => CapText(MaxClusterRiskPct),
        ClusterRhoKey => Text(ClusterRho),
        _ => null,
    };

    /// <summary>These settings with one stored value applied; an unknown key or an unreadable value leaves them as they are.</summary>
    public BasketSettings With(string key, string value) => key switch
    {
        BreadthSourceKey when SourceIsValid(value) => this with { BreadthSource = value },
        MaxClusterRiskKey when value == Off => this with { MaxClusterRiskPct = null },
        MaxClusterRiskKey when double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => this with { MaxClusterRiskPct = d },
        ClusterRhoKey when double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => this with { ClusterRho = d },
        _ => this,
    };

    /// <summary>The basket id of a <c>basket:&lt;id&gt;</c> source; null for <c>tracked</c> or an unreadable text.</summary>
    public static Guid? BasketOf(string? source) =>
        source is not null && source.StartsWith(BasketPrefix, StringComparison.Ordinal) && Guid.TryParseExact(source[BasketPrefix.Length..], "D", out var id) && id != Guid.Empty ? id : null;
}
