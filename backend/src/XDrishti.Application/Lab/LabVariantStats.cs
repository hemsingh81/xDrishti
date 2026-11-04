using XDrishti.Application.Abstractions.Lab;
using XDrishti.Domain.Confidence;

namespace XDrishti.Application.Lab;

/// <summary>The row of <c>lab.variant_stats</c> for a variant's per-trade results (CF-03): a figure that is not finite is stored as null.</summary>
internal static class LabVariantStats
{
    public static VariantStatsRow Of(string strategyId, Guid runId, string variantHash, TradeMoments m, string method, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(m);
        return new VariantStatsRow(strategyId, runId, variantHash, m.N, Finite(m.Mean), Finite(m.Sd), Finite(m.Skew), Finite(m.Kurt), Finite(m.Sr), method, now);
    }

    private static double? Finite(double value) => double.IsFinite(value) ? value : null;
}
