using System.Globalization;
using XDrishti.Application.Abstractions.LearnedSettings;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Planning;

namespace XDrishti.Application.LearnedSettings;

/// <summary>
/// The bell notice of AU-10 criterion 9, derived on read: when the coming plan has been empty for <see cref="Sessions"/> sessions in a row and a setting was tightened by itself in the last <see cref="Days"/> days, one notice names each such setting.
/// It only tells; nothing is loosened. With no way to know (the probe says null) there is no notice. Words and ids only.
/// </summary>
internal sealed class LearnedTighteningNotices(ILearnedSettingStore store, IEmptyPlanProbe probe, ILearnedConfig config) : IBellNoticeSource
{
    public const string Kind = "learned_tightening_empty_plan";

    public const int Sessions = 5;

    public const int Days = 14;

    public async ValueTask<IReadOnlyList<PlanNotice>> ListAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (await probe.ConsecutiveEmptySessionsAsync(cancellationToken) is not { } empty || empty < Sessions)
        {
            return [];
        }

        var rows = await store.ListAsync(GetLearnedChangesHandler.Limit, cancellationToken);
        return
        [
            .. rows.Where(r => r.State == "applied" && r.DecidedBy == LearnedSettingProposer.AutoActor && r.Direction == "tighten" && r.DecidedAt is { } at && at >= now.AddDays(-Days))
                .Select(r => new PlanNotice(
                    "learned-empty:" + r.Id.ToString(CultureInfo.InvariantCulture) + ":" + empty.ToString(CultureInfo.InvariantCulture),
                    Kind,
                    "warning",
                    string.Create(CultureInfo.InvariantCulture, $"The coming plan has been empty for {empty} sessions in a row. The setting \"{config.Descriptor(r.Key)?.Label ?? r.Key}\" was tightened automatically from {r.OldValue} to {r.NewValue}. Nothing is loosened by itself; undo it in Settings with your password if you want it back."),
                    "/settings",
                    null,
                    r.DecidedAt,
                    r.DecidedAt)),
        ];
    }
}

/// <summary>The only <see cref="IEmptyPlanProbe"/> today: it cannot tell, so no notice is raised.</summary>
internal sealed class NoEmptyPlanProbe : IEmptyPlanProbe
{
    // shortcut: no stored count of consecutive empty coming plans exists yet; a later slice reads the plan record behind this port. Until then criterion 9 never fires (it never invents a number).
    public ValueTask<int?> ConsecutiveEmptySessionsAsync(CancellationToken cancellationToken) => ValueTask.FromResult<int?>(null);
}

/// <summary>The only <see cref="ILearnedEvidenceSource"/> today: nothing is available, so the weekly run changes nothing.</summary>
internal sealed class NoLearnedEvidence : ILearnedEvidenceSource
{
    // shortcut: no measured source (paper fills, decay, readiness) produces out-of-sample proof for the allow-listed keys yet; a later slice adds the second implementation behind this port. Unavailable is never a pass.
    public ValueTask<LearnedEvidenceBatch> ReadAsync(CancellationToken cancellationToken) => ValueTask.FromResult(LearnedEvidenceBatch.Unavailable);
}
