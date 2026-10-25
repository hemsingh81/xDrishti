using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Paper;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Domain.Common;
using XDrishti.Domain.Paper;

namespace XDrishti.Application.Paper;

/// <summary>The words every governance response carries.</summary>
internal static class GovernanceWording
{
    public const string Basis = "configuration governance: detection and labelling only; nothing here changes a setting or applies a hypothesis";

    public const string QueueNote = "accepted means judged better offline; nothing was applied; applying it starts a new epoch";

    public static string Short(string? hash) => hash is null ? string.Empty : hash[..Math.Min(12, hash.Length)];

    public static IReadOnlyList<EpochView> Views(IReadOnlyList<EpochSummary> newestFirst) =>
        [.. newestFirst.Select((s, i) => new EpochView(
            s.Epoch.Id, s.Epoch.Kind, s.Epoch.StartedOn, i == 0 ? null : newestFirst[i - 1].Epoch.StartedOn, s.Hash is null ? null : Short(s.Hash), s.Counted, s.Straddle, s.RiskDownChanges, s.UnexplainedChanges, s.Epoch.Reason))];
}

/// <summary>The epochs with their counts (spec P10-05). Read only.</summary>
internal sealed class GetEpochsHandler(IGovernanceStore store) : IQueryHandler<GetEpochsQuery, Result<EpochList>>
{
    public async ValueTask<Result<EpochList>> HandleAsync(GetEpochsQuery query, CancellationToken cancellationToken) =>
        Result.Success(new EpochList(GovernanceWording.Basis, GovernanceWording.Views(await store.ListEpochsAsync(cancellationToken))));
}

/// <summary>One epoch with its changes and notes. Read only.</summary>
internal sealed class GetEpochHandler(IGovernanceStore store) : IQueryHandler<GetEpochQuery, Result<EpochDetail>>
{
    public async ValueTask<Result<EpochDetail>> HandleAsync(GetEpochQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var epoch = GovernanceWording.Views(await store.ListEpochsAsync(cancellationToken)).FirstOrDefault(e => e.Id == query.Id);
        if (epoch is null)
        {
            return Result.Failure<EpochDetail>(Error.NotFound(PaperErrors.NotFoundCode, "No epoch " + query.Id + "."));
        }

        var notes = await store.ListNotesAsync(query.Id, cancellationToken);
        var changes = await store.ListChangesAsync(query.Id, 200, cancellationToken);
        return Result.Success(new EpochDetail(GovernanceWording.Basis, epoch, ChangeViews.Of(changes, notes), [.. notes.Select(n => new EpochNoteView(n.Id, n.ChangeId, n.HypothesisId, n.Text, n.Actor, n.CreatedAt))]));
    }
}

/// <summary>Maps stored changes to views; a change is unexplained when it has neither an explanation nor a note (free changes are never).</summary>
internal static class ChangeViews
{
    public static IReadOnlyList<ConfigChangeView> Of(IReadOnlyList<StoredChange> changes, IReadOnlyList<EpochNote> notes) =>
        [.. changes.Select(c => new ConfigChangeView(c.Id, c.EpochId, c.Key, c.Class, c.Before, c.After, c.Effect, c.ExplainedBy, c.Class != "free" && c.ExplainedBy is null && !notes.Any(n => n.ChangeId == c.Id)))];
}

/// <summary>The recorded differences. Read only.</summary>
internal sealed class GetConfigChangesHandler(IGovernanceStore store) : IQueryHandler<GetConfigChangesQuery, Result<ConfigChangeList>>
{
    public async ValueTask<Result<ConfigChangeList>> HandleAsync(GetConfigChangesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Limit is < 1 or > 200)
        {
            return Result.Failure<ConfigChangeList>(PaperErrors.Invalid("limit", "The limit is 1 to 200."));
        }

        var changes = await store.ListChangesAsync(query.EpochId, query.Limit, cancellationToken);
        return Result.Success(new ConfigChangeList(GovernanceWording.Basis, ChangeViews.Of(changes, await store.ListNotesAsync(query.EpochId, cancellationToken))));
    }
}

/// <summary>The configuration now, its rules, the last snapshot and the differences not yet captured. A preview: it stores nothing.</summary>
internal sealed class GetConfigHandler(IConfigSnapshotSource source, IGovernanceStore store, IServiceScheduleReader services) : IQueryHandler<GetConfigQuery, Result<ConfigView>>
{
    public async ValueTask<Result<ConfigView>> HandleAsync(GetConfigQuery query, CancellationToken cancellationToken)
    {
        var reading = await source.ReadAsync(cancellationToken);
        var now = ConfigFingerprint.Create(reading.Components);
        var last = await store.LatestSnapshotAsync(true, cancellationToken);
        var before = last is null ? null : ConfigFingerprint.FromJson(last.ComponentsJson);
        var pending = before is null || reading.Unreadable.Count > 0
            ? []
            : ConfigDiff.Compare(before.Values, now.Values).Select(c => new PendingChangeView(c.Key, c.Class.Code(), c.Before, c.After, c.Class == ConfigClass.Frozen ? "would restart the count" : "noted only")).ToList();
        var blockers = new List<BlockerView>();
        foreach (var id in PaperConfigKeys.LearningWriterServices)
        {
            if (await services.GetAsync(id, cancellationToken) is { Enabled: true })
            {
                blockers.Add(new BlockerView(id, PaperConfigKeys.BlockerText));
            }
        }

        return Result.Success(new ConfigView(
            GovernanceWording.Basis,
            GovernanceWording.Short(now.Hash),
            last is null ? null : new LastSnapshotView(GovernanceWording.Short(last.Hash), last.TakenAt, last.SessionDate),
            [.. now.Values.Select(p => new ConfigComponentView(p.Key, PaperConfigKeys.Describe(p.Key), p.Value))],
            pending,
            blockers,
            reading.Unreadable));
    }
}

/// <summary>The accepted hypotheses that no change or note names: they wait, nothing is applied, proposed or reordered.</summary>
internal sealed class GetQueueHandler(IHypothesisLog hypotheses, IGovernanceStore store, TimeProvider time) : IQueryHandler<GetQueueQuery, Result<QueueView>>
{
    public async ValueTask<Result<QueueView>> HandleAsync(GetQueueQuery query, CancellationToken cancellationToken)
    {
        var used = await store.UsedHypothesisIdsAsync(cancellationToken);
        var now = time.GetUtcNow();
        var items = (await hypotheses.ListAsync("accepted", 200, cancellationToken))
            .Where(h => !used.Contains(h.HypothesisId))
            .OrderBy(h => h.HypothesisId)
            .Select(h => new QueueItem(h.HypothesisId, Math.Max(0, (int)(now - h.Row.CreatedAt).TotalDays), h.Row.CellKey, h.Row.KnobName, h.Row.BaselineValue, h.Row.CandidateValue, h.Row.ExperimentId))
            .ToList();
        return Result.Success(new QueueView(GovernanceWording.QueueNote, items));
    }
}
