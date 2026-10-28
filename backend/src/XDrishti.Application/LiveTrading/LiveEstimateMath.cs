using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using XDrishti.Application.Abstractions.LiveTrading;
using XDrishti.Domain.LiveTrading;
using XDrishti.Domain.Reporting;

namespace XDrishti.Application.LiveTrading;

/// <summary>
/// The pure part of the weekly estimates (spec P11-02): groups the stored executions by bucket, side, style and reference kind (the two kinds are never pooled; an observation with no reference is left out and counted by the caller) and turns each group into a
/// slippage row, and the stored order classes into the capacity rows. The same inputs give the same rows and the same input hash. Nothing is applied anywhere.
/// </summary>
internal static class LiveEstimateMath
{
    public const string AccountScope = "account";
    public const string BucketScope = "bucket";
    public const string AccountKey = "all";

    public static IReadOnlyList<NewSlippageEstimate> Slippage(DateOnly week, IReadOnlyList<NewLiveExecution> executions, int minN, int proposalMinN)
    {
        ArgumentNullException.ThrowIfNull(executions);
        var groups = executions
            .Where(e => e.ReferenceKind != ReferenceKind.Unavailable && e.SlipTotalBps is not null)
            .GroupBy(e => (e.Bucket, e.Side, e.Style, e.ReferenceKind))
            .OrderBy(g => g.Key.Bucket, StringComparer.Ordinal).ThenBy(g => g.Key.Side, StringComparer.Ordinal).ThenBy(g => g.Key.Style, StringComparer.Ordinal).ThenBy(g => g.Key.ReferenceKind, StringComparer.Ordinal);
        var rows = new List<NewSlippageEstimate>();
        foreach (var g in groups)
        {
            var ordered = g.OrderBy(e => e.OutcomeId).ToList();
            var bps = ordered.Select(e => e.SlipTotalBps!.Value).ToList();
            var hash = Hash(string.Join("|", g.Key.Bucket, g.Key.Side, g.Key.Style, g.Key.ReferenceKind) + "#" + string.Join(",", ordered.Select(e => e.OutcomeId.ToString(CultureInfo.InvariantCulture) + ":" + e.SlipTotalBps!.Value.ToString("R", CultureInfo.InvariantCulture))));
            if (bps.Count < minN || SlippageStats.Summarise(bps) is not { } s)
            {
                rows.Add(new NewSlippageEstimate(week, g.Key.Bucket, g.Key.Side, g.Key.Style, g.Key.ReferenceKind, bps.Count, StateWord(MetricState.Insufficient), $"n = {bps.Count}, at least {minN} needed", null, null, null, null, null, null, null, null, null, hash));
                continue;
            }

            var ticks = g.Where(e => e.SlipTotalTicks is not null).Select(e => e.SlipTotalTicks!.Value).ToList();
            var rs = g.Where(e => e.SlipTotalR is not null).Select(e => e.SlipTotalR!.Value).ToList();
            rows.Add(new NewSlippageEstimate(
                week, g.Key.Bucket, g.Key.Side, g.Key.Style, g.Key.ReferenceKind, bps.Count, StateWord(MetricState.Value), null,
                s.Mean, s.MeanLo, s.MeanHi, s.Median, s.MedianLo, s.MedianHi,
                ticks.Count > 0 ? ticks.Average() : null, rs.Count > 0 ? rs.Average() : null,
                SlippageProposal.For(g.Key.Bucket, g.Key.Side, g.Key.Style, g.Key.ReferenceKind, bps.Count, s.Mean, s.MeanLo, proposalMinN), hash));
        }

        return rows;
    }

    public static IReadOnlyList<NewCapacityScore> Capacity(DateOnly week, IReadOnlyList<LiveOrderClassRow> classes, BucketEdges edges, int priorStrength, int minN)
    {
        ArgumentNullException.ThrowIfNull(classes);
        ArgumentNullException.ThrowIfNull(edges);
        var rows = new List<NewCapacityScore>();
        var all = CapacityCounts.From(classes.Select(c => c.Class));
        var account = CapacityScore.ForAccount(all, minN);
        rows.Add(Row(week, AccountScope, AccountKey, all, account, priorStrength: 2, Hash(AccountKey + "|" + string.Join(",", classes.Select(c => c.AccountId.ToString("N") + c.BrokerOrderId + c.Class).Order(StringComparer.Ordinal)))));
        foreach (var bucket in edges.Buckets)
        {
            var mine = classes.Where(c => c.Bucket == bucket).ToList();
            if (mine.Count == 0)
            {
                continue;
            }

            var counts = CapacityCounts.From(mine.Select(c => c.Class));
            var result = CapacityScore.ForChild(counts, account.Score.Number, priorStrength, minN);
            rows.Add(Row(week, BucketScope, bucket, counts, result, priorStrength, Hash(bucket + "|" + string.Join(",", mine.Select(c => c.AccountId.ToString("N") + c.BrokerOrderId + c.Class).Order(StringComparer.Ordinal)))));
        }

        return rows;
    }

    private static NewCapacityScore Row(DateOnly week, string scope, string key, CapacityCounts counts, CapacityResult result, int priorStrength, string hash) =>
        new(week, scope, key, counts.Counted, counts.Full, counts.Partial, counts.Rejected, counts.QueueMiss, counts.PriceMiss, counts.Unclassified, result.Score.Number, result.Lo, result.Hi, StateWord(result.Score.State), result.Score.Reason, priorStrength, hash);

    /// <summary>The stored word of a metric state.</summary>
    public static string StateWord(MetricState state) => state switch
    {
        MetricState.Value => "value",
        MetricState.Provisional => "provisional",
        MetricState.Insufficient => "insufficient",
        MetricState.Unavailable => "unavailable",
        MetricState.NotEvaluable => "not_evaluable",
        _ => "empty",
    };

    private static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
