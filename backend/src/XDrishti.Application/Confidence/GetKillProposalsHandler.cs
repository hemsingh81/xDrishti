using XDrishti.Application.Abstractions.Confidence;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Paper;
using XDrishti.Domain.Common;
using XDrishti.Domain.Confidence;

namespace XDrishti.Application.Confidence;

/// <summary>The kill proposals with the automatic setting beside them (spec CF-12, part 12b, ADR 0219). Read only: it writes nothing and applies nothing.</summary>
internal sealed class GetKillProposalsHandler(IKillProposalReader reader, IKillSettings settings, KillOptions kill) : IQueryHandler<GetKillProposalsQuery, Result<KillProposalsView>>
{
    private static readonly string[] States = ["proposed", "applied", "dismissed"];

    public async ValueTask<Result<KillProposalsView>> HandleAsync(GetKillProposalsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.State is { } state && !States.Contains(state))
        {
            return Result.Failure<KillProposalsView>(PaperErrors.Invalid("state", "The state is proposed, applied or dismissed."));
        }

        if (query.Limit is < 1 or > 200)
        {
            return Result.Failure<KillProposalsView>(PaperErrors.Invalid("limit", "The limit is 1 to 200."));
        }

        var strategy = string.IsNullOrWhiteSpace(query.Strategy) ? null : query.Strategy.Trim();
        var rows = await reader.ListAsync(query.State, strategy, query.Limit, cancellationToken);
        var current = await settings.CurrentAsync(cancellationToken);
        return Result.Success(new KillProposalsView(
            [.. rows.Select(r => new KillProposalView(r.Id, r.Proposal.Strategy, r.Proposal.Rule, r.Proposal.ProposalOnly, r.Proposal.Value, r.Proposal.Limit, r.Proposal.Trades, r.Proposal.Text, r.Proposal.Cells, r.Proposal.SettingsHash, r.ProposedOn, r.ProposedAt, r.State, r.Actor, r.DecidedAt, r.AutoSettingId))],
            new KillSettingsView(current.Auto, current.Id, kill.StressMultiplier, kill.DrawdownFactor, kill.SnapshotHash()),
            [
                "A proposal changes nothing by itself. Applying it pauses the cells it names (Paper or Active to Paused); the owner applies it, or the automatic setting does when it is ON (default OFF), except for the CF-09 failure boundary, which only the owner applies.",
                "Re-enabling a paused cell is always the owner's act with a fresh password and needs the failing condition to have cleared. No order is placed by any of this.",
            ]));
    }
}
