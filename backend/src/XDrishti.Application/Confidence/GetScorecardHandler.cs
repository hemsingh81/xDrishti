using XDrishti.Application.Abstractions.Confidence;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Confidence;

/// <summary>
/// The scorecard of one strategy version (spec CF-11, ADR 0221): derived on every request from the stored results, with the owner's enforce switch and the newest daily snapshots (the trend) beside it. Read only: it stores nothing, moves no cell and places no order, and
/// nothing that chooses what to approve may use it (an architecture test).
/// </summary>
internal sealed class GetScorecardHandler(ScorecardReader reader, IReadinessSettings settings, IReadinessSnapshotReader snapshots) : IQueryHandler<GetScorecardQuery, Result<ScorecardView>>
{
    /// <summary>How many daily snapshots the trend shows.</summary>
    public const int HistoryDays = 30;

    public async ValueTask<Result<ScorecardView>> HandleAsync(GetScorecardQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var strategy = query.Strategy?.Trim();
        if (string.IsNullOrEmpty(strategy) || strategy.Contains('|', StringComparison.Ordinal))
        {
            return Result.Failure<ScorecardView>(ReadinessErrors.Invalid("strategy", "The scorecard needs a strategy id without `|`."));
        }

        if (query.Version < 1)
        {
            return Result.Failure<ScorecardView>(ReadinessErrors.Invalid("version", "The version is 1 or more."));
        }

        var exists = await reader.VersionExistsAsync(strategy, query.Version, cancellationToken);
        if (exists.IsFailure)
        {
            return Result.Failure<ScorecardView>(exists.Error);
        }

        var card = await reader.BuildAsync(strategy, query.Version, cancellationToken);
        var current = await settings.CurrentAsync(cancellationToken);
        var recent = await snapshots.RecentAsync("strategy", strategy, "daily", HistoryDays * 2, cancellationToken);
        var history = recent.Where(r => r.Version == query.Version).Take(HistoryDays).Select(r => new ScorecardPointView(r.Id, r.TakenOn, r.Score, r.Label, r.SettingsHash)).ToList();
        return Result.Success(card with { Enforce = current.Enforce, History = history });
    }
}
