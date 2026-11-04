namespace XDrishti.Domain.Confidence;

/// <summary>An effective-sample-size estimate or the closed reason there is none.</summary>
/// <param name="NEff">Effective trades, between 1 and the trade count; null when unavailable.</param>
/// <param name="DesignEffect">Trades over effective trades (1 or more); null when unavailable.</param>
/// <param name="Method"><c>cluster</c>, <c>serial</c> or <c>none</c>.</param>
/// <param name="Note">Empty for a usable estimate; otherwise <c>unavailable: &lt;reason&gt;</c> (or a plain remark on the estimate).</param>
public sealed record EffectiveEstimate(double? NEff, double? DesignEffect, string Method, string Note)
{
    /// <summary>Method names.</summary>
    public const string ClusterMethod = "cluster";

    /// <summary>Method names.</summary>
    public const string SerialMethod = "serial";

    /// <summary>Method names.</summary>
    public const string NoneMethod = "none";

    /// <summary>No estimate, for <paramref name="reason"/>.</summary>
    public static EffectiveEstimate Unavailable(string reason) => new(null, null, NoneMethod, "unavailable: " + reason);

    /// <summary>True when there is a number.</summary>
    public bool IsAvailable => NEff.HasValue;
}
