using System.Globalization;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Quality;

namespace XDrishti.Application.Market.Quality;

/// <summary>Builds the rows of the quality table from the stored results, and says in words what is wrong.</summary>
public static class QualityRows
{
    public static QualityInstrumentRow From(
        Guid instrumentId,
        string symbol,
        string name,
        string type,
        string? source,
        string? status,
        int? score,
        int? barsChecked,
        int? missingMinutes,
        QualityChecks? checks,
        DateTimeOffset? checkedAt,
        Guid? runId)
    {
        if (checks is null || status is null)
        {
            return new QualityInstrumentRow(instrumentId, symbol, name, type, QualityInstrumentSearch.NotChecked, null, null, null, null, null, null, [], source, null, null);
        }

        return new QualityInstrumentRow(
            instrumentId,
            symbol,
            name,
            type,
            status,
            score,
            Math.Round(100 - checks.Completeness.MissingPercent, 2),
            barsChecked,
            missingMinutes,
            new QualityCheckStatuses(checks.Completeness.Status, checks.Sanity.Status, checks.Spikes.Status, checks.Aggregates.Status, checks.Freshness.Status, checks.OfficialDaily.Status),
            InOrder(checks.Aggregates).ByTimeframe.ToDictionary(t => t.Key, t => t.Value.Status, StringComparer.Ordinal),
            Issues(checks),
            source,
            checkedAt is { } at ? IndiaTime.ToIst(at) : null,
            runId);
    }

    /// <summary>The aggregates check with its timeframes in the order of the timeframes (5m … 1M); the database hands the keys of a JSON object back in its own order.</summary>
    public static AggregatesCheck InOrder(AggregatesCheck check)
    {
        ArgumentNullException.ThrowIfNull(check);
        var ordered = new Dictionary<string, TimeframeCheck>(StringComparer.Ordinal);
        foreach (var timeframe in Timeframes.Aggregated)
        {
            if (check.ByTimeframe.TryGetValue(timeframe.ToCode(), out var own))
            {
                ordered[timeframe.ToCode()] = own;
            }
        }

        return check with { ByTimeframe = ordered };
    }

    /// <summary>What is wrong, one short sentence per failing or warning check.</summary>
    public static IReadOnlyList<string> Issues(QualityChecks checks)
    {
        ArgumentNullException.ThrowIfNull(checks);
        var issues = new List<string>();
        var completeness = checks.Completeness;
        if (completeness.Status != CheckStatus.Pass)
        {
            var unfillable = completeness.UnfillableMinutes > 0 ? string.Create(CultureInfo.InvariantCulture, $", {completeness.UnfillableMinutes} of them could not be fetched") : string.Empty;
            issues.Add(string.Create(CultureInfo.InvariantCulture, $"{completeness.MissingMinutes} missing minutes ({completeness.MissingPercent} %){unfillable}"));
        }

        if (checks.Sanity.BadBars > 0)
        {
            issues.Add(string.Create(CultureInfo.InvariantCulture, $"{checks.Sanity.BadBars} bad bars"));
        }

        if (checks.Spikes.Spikes > 0)
        {
            issues.Add(string.Create(CultureInfo.InvariantCulture, $"{checks.Spikes.Spikes} price spikes"));
        }

        if (checks.Aggregates.Differences > 0)
        {
            issues.Add(string.Create(CultureInfo.InvariantCulture, $"{checks.Aggregates.Differences} aggregate bars differ from the 1-minute bars"));
        }

        if (checks.Freshness.Status == CheckStatus.Fail)
        {
            issues.Add(checks.Freshness.LastBar is null ? "no bars" : string.Create(CultureInfo.InvariantCulture, $"{checks.Freshness.DaysBehind} trading days behind"));
        }

        return issues;
    }
}
