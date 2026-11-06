using System.Globalization;
using XDrishti.Application.Abstractions.Confidence;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Planning;

namespace XDrishti.Application.Confidence;

/// <summary>
/// The bell notices of the kill proposals (spec CF-12 D3, part 12b), derived on read like the other notices (P8-15). One per proposal still <c>proposed</c>, present while it is open and gone once it is applied or dismissed; and, so that an automatic pause is never silent,
/// one <b>resolved</b> notice for every proposal the automatic setting applied in the last <see cref="AutoDays"/> days. Counts and words only (a strategy, a rule and a number of cells), no symbol, size or price; the route opens the Learning screen. Nothing is stored and
/// nothing is changed by reading it.
/// </summary>
internal sealed class KillProposalNotices(IKillProposalReader proposals) : IBellNoticeSource
{
    public const string Kind = "kill_proposed";

    public const string AutoKind = "kill_applied_auto";

    /// <summary>How many days back an automatic pause stays on the bell.</summary>
    public const int AutoDays = 3;

    private const int Limit = 20;

    public async ValueTask<IReadOnlyList<PlanNotice>> ListAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var open = await proposals.ListAsync("proposed", null, Limit, cancellationToken);
        var automatic = (await proposals.ListAsync("applied", null, Limit * 2, cancellationToken))
            .Where(p => p.Actor == KillActors.Auto && p.DecidedAt is { } at && at >= now.AddDays(-AutoDays))
            .Take(Limit);
        return
        [
            .. open.Select(p => new PlanNotice(
                "kill:" + p.Id.ToString(CultureInfo.InvariantCulture),
                Kind,
                "warning",
                string.Create(CultureInfo.InvariantCulture, $"Kill rule {p.Proposal.Rule} fires for {p.Proposal.Strategy}: {p.Proposal.Cells.Count} cell(s) would be paused. Nothing is paused until it is applied{(p.Proposal.ProposalOnly ? " by you (this one is never applied automatically)" : string.Empty)}."),
                "/learning",
                null,
                p.ProposedAt,
                p.ProposedAt)),
            .. automatic.Select(p => new PlanNotice(
                "kill-auto:" + p.Id.ToString(CultureInfo.InvariantCulture),
                AutoKind,
                "info",
                string.Create(CultureInfo.InvariantCulture, $"The automatic setting applied kill rule {p.Proposal.Rule} to {p.Proposal.Strategy}: its Paper and Active cells ({p.Proposal.Cells.Count} named) were paused. Only you re-enable them."),
                "/learning",
                null,
                p.DecidedAt,
                p.ProposedAt,
                true)),
        ];
    }
}
