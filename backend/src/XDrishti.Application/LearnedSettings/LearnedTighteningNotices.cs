using System.Globalization;
using XDrishti.Application.Abstractions.LearnedSettings;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Planning;

namespace XDrishti.Application.LearnedSettings;

/// <summary>
/// The bell notice of AU-10 criterion 9, derived on read: while the coming plan has been empty for <see cref="Sessions"/> sessions in a row (the notice says the setting may be a cause: the empty run is not measured from the change), one notice names each setting whose CURRENT value (the newest applied row of the key) is an automatic tightening. It stays while that is so, however
/// old the change is, and goes when the plan is not empty any more or the change is undone or replaced by an owner. It only tells; nothing is loosened. With no way to know (the probe says null) there is no notice. Words and ids only.
/// </summary>
internal sealed class LearnedTighteningNotices(ILearnedSettingStore store, IEmptyPlanProbe probe, ILearnedConfig config) : IBellNoticeSource
{
    public const string Kind = "learned_tightening_empty_plan";

    public const int Sessions = 5;

    public async ValueTask<IReadOnlyList<PlanNotice>> ListAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (await probe.ConsecutiveEmptySessionsAsync(cancellationToken) is not { } empty || empty < Sessions)
        {
            return [];
        }

        var current = await store.CurrentAppliedAsync(cancellationToken); // the newest applied row of every key, not the capped log
        return
        [
            .. current.Where(r => r.DecidedBy == LearnedSettingProposer.AutoActor && r.Direction == "tighten")
                .OrderBy(r => r.Key, StringComparer.Ordinal)
                .Select(r => new PlanNotice(
                    "learned-empty:" + r.Id.ToString(CultureInfo.InvariantCulture) + ":" + empty.ToString(CultureInfo.InvariantCulture),
                    Kind,
                    "warning",
                    string.Create(CultureInfo.InvariantCulture, $"The coming plan has been empty for {empty} sessions in a row, and this may be a cause: the setting \"{config.Descriptor(r.Key)?.Label ?? r.Key}\" was tightened automatically from {r.OldValue} to {r.NewValue}. Nothing is loosened by itself; undo it in Settings with your password if you want it back."),
                    "/settings",
                    null,
                    r.DecidedAt,
                    r.DecidedAt)),
        ];
    }
}

/// <summary>
/// The empty-plan probe over the stored plans: the newest version of each planned session, newest session first, counted while the primary list is empty. A session with no stored plan (a run that failed closed) is not counted and does not break the run of empty ones;
/// no stored plan at all is unknown (null), never zero.
/// </summary>
internal sealed class StoredEmptyPlanProbe(IPlanStore plans) : IEmptyPlanProbe
{
    public const int Look = 40;

    public async ValueTask<int?> ConsecutiveEmptySessionsAsync(CancellationToken cancellationToken)
    {
        var rows = await plans.ListAsync(null, null, Look, cancellationToken);
        if (rows.Count == 0)
        {
            return null;
        }

        var empty = 0;
        foreach (var session in rows.GroupBy(r => r.Plan.ForDate).OrderByDescending(g => g.Key))
        {
            if (session.OrderByDescending(r => r.Plan.Version).First().Primary > 0)
            {
                break;
            }

            empty++;
        }

        return empty;
    }
}

/// <summary>The only <see cref="ILearnedEvidenceSource"/> today: nothing is available, so the weekly run changes nothing.</summary>
internal sealed class NoLearnedEvidence : ILearnedEvidenceSource
{
    // shortcut: no measured source (paper fills, decay, readiness) produces out-of-sample proof for the allow-listed keys yet; a later slice adds the second implementation behind this port. Unavailable is never a pass.
    public ValueTask<LearnedEvidenceBatch> ReadAsync(CancellationToken cancellationToken) => ValueTask.FromResult(LearnedEvidenceBatch.Unavailable);
}
