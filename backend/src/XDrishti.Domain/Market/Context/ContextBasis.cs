namespace XDrishti.Domain.Market.Context;

/// <summary>What the regime of a figure was built from (D2): <see cref="IndexAndVix"/> when the VIX percentile was ready, <see cref="IndexOnly"/> when the index percentile stood alone. A regime cell must not mix the two without saying so.</summary>
public static class ContextBasis
{
    public const string IndexAndVix = "index_and_vix";

    public const string IndexOnly = "index_only";
}
