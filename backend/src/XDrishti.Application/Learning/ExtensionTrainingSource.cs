using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Learning.Extensions;

namespace XDrishti.Application.Learning;

/// <summary>
/// The rows of protocol X (P15-10): the stored runs, the extraction record of the version for each, and the labelled rows of the runs joined with the stored <c>tf-1</c> and extension values through the same loader as every training set. The style of a run is intraday when none of its closed
/// trades is held past its first session, else swing; the exit label is fixed, because the cell features are not what the comparison judges (both models see the same cells). Reads only.
/// </summary>
internal sealed class ExtensionTrainingSource(IRunStore runs, ITradeFeatureStore features, IExtensionFeatureStore extensionRows, IExtensionExtractionStore records) : IExtensionTrainingSource
{
    private const string ExitLabel = "default";

    public async ValueTask<IReadOnlyList<ExtensionRunSource>> ListRunsAsync(string version, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(version);
        var headers = new List<StoredRunHeader>();
        string? cursor = null;
        do
        {
            var page = await runs.ListAsync(new RunListFilter(), new PageRequest(PageRequest.MaxSize, cursor), cancellationToken);
            if (page.IsFailure)
            {
                break;
            }

            headers.AddRange(page.Value.Items);
            cursor = page.Value.NextCursor;
        }
        while (cursor is not null);

        var list = new List<ExtensionRunSource>(headers.Count);
        foreach (var header in headers.OrderBy(h => h.RunKey, StringComparer.Ordinal))
        {
            list.Add(new ExtensionRunSource(header.RunKey, header.StrategyId, header.TradeCount, await records.FindAsync(header.RunKey, version, cancellationToken)));
        }

        return list;
    }

    public async ValueTask<Result<ExtensionTrainingData>> LoadAsync(ExtensionGroup group, IReadOnlyList<string> runKeys, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(runKeys);
        var sources = new List<CellSource>(runKeys.Count);
        foreach (var key in runKeys.Order(StringComparer.Ordinal))
        {
            var swing = false;
            await foreach (var runTrade in runs.StreamTradesAsync(key, false, cancellationToken))
            {
                swing |= runTrade.Trade.SessionsHeld > 1;
            }

            sources.Add(new CellSource(key, swing ? "swing" : "intraday", ExitLabel));
        }

        var values = new Dictionary<string, double[]>(StringComparer.Ordinal);
        foreach (var row in await extensionRows.ReadAsync(runKeys, group.Version, cancellationToken))
        {
            values.TryAdd(row.TradeKey, [.. row.Values]);
        }

        var loaded = await TrainingDataLoader.LoadAsync(runs, features, sources, new CellStatsOptions(), null, cancellationToken, new ExtensionGroupInput(group, values));
        if (loaded.IsFailure)
        {
            return Result.Failure<ExtensionTrainingData>(loaded.Error);
        }

        var data = loaded.Value.Data;
        var rows = data.Rows
            .Select(r => new ValidationRow(r.TradeKey, r.Label, r.R, r.TradeDate, r.DecisionEpochSeconds, r.ResolvedEpochSeconds, loaded.Value.CellOf[r.TradeKey], [.. r.Values]))
            .ToArray();
        return Result.Success(new ExtensionTrainingData(rows, TradeFeatureCatalog.Names.Count, data.Id, data.RuleVersion));
    }
}
