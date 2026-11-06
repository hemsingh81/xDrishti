using System.Globalization;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Planning;

namespace XDrishti.Application.Learning;

/// <summary>
/// The bell notices of the rollback proposals (spec CF-10 D8, part 10d-1), derived on read like the other notices (P8-15). One per proposal still <c>proposed</c>, present while it is open and gone once it is applied or dismissed; and, so that an automatic demotion is never silent, one
/// <b>resolved</b> notice for every proposal the automatic setting applied in the last <see cref="AutoDays"/> days. Words and ids only. Nothing is stored and nothing is changed by reading it.
/// </summary>
internal sealed class RollbackProposalNotices(IRollbackProposalReader proposals) : IBellNoticeSource
{
    public const string Kind = "rollback_proposed";

    public const string AutoKind = "rollback_applied_auto";

    /// <summary>How many days back an automatic demotion stays on the bell.</summary>
    public const int AutoDays = 3;

    private const int Limit = 20;

    public async ValueTask<IReadOnlyList<PlanNotice>> ListAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var open = await proposals.ListAsync("proposed", null, Limit, cancellationToken);
        var automatic = (await proposals.ListAsync("applied", null, Limit * 2, cancellationToken))
            .Where(p => p.Actor == MlActors.Auto && p.DecidedAt is { } at && at >= now.AddDays(-AutoDays))
            .Take(Limit);
        return
        [
            .. open.Select(p => new PlanNotice(
                "rollback:" + p.Id.ToString(CultureInfo.InvariantCulture),
                Kind,
                "warning",
                string.Create(CultureInfo.InvariantCulture, $"Model {p.Proposal.ModelId}: the {p.Proposal.Trigger} rollback trigger fires. Nothing changes until you apply it; it would move the champion to challenger (shadow) and leave no champion until you name one."),
                "/learning",
                null,
                p.ProposedAt,
                p.ProposedAt)),
            .. automatic.Select(p => new PlanNotice(
                "rollback-auto:" + p.Id.ToString(CultureInfo.InvariantCulture),
                AutoKind,
                "info",
                string.Create(CultureInfo.InvariantCulture, $"The automatic setting applied the {p.Proposal.Trigger} rollback to model {p.Proposal.ModelId}: it is a challenger (shadow) now and there is no champion. Only you name a champion again."),
                "/learning",
                null,
                p.DecidedAt,
                p.ProposedAt,
                true)),
        ];
    }
}
