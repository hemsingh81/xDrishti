using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Learning;

/// <summary>The rollback proposals with the three ML switches beside them (spec CF-10 D8, part 10d-1, ADR 0220). Read only: it writes nothing and applies nothing.</summary>
internal sealed class GetRollbackProposalsHandler(IRollbackProposalReader reader, IMlSwitchSettings switches) : IQueryHandler<GetRollbackProposalsQuery, Result<RollbackProposalsView>>
{
    private static readonly string[] States = ["proposed", "applied", "dismissed"];

    public async ValueTask<Result<RollbackProposalsView>> HandleAsync(GetRollbackProposalsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.State is { } state && !States.Contains(state))
        {
            return Result.Failure<RollbackProposalsView>(LearningErrors.Invalid([new FieldError("state", "The state is proposed, applied or dismissed.")]));
        }

        if (query.Limit is < 1 or > 200)
        {
            return Result.Failure<RollbackProposalsView>(LearningErrors.Invalid([new FieldError("limit", "The limit is 1 to 200.")]));
        }

        var rows = await reader.ListAsync(query.State, query.ModelId, query.Limit, cancellationToken);
        var s = await switches.CurrentAsync(cancellationToken);
        return Result.Success(new RollbackProposalsView(
            [.. rows.Select(r => new RollbackProposalView(r.Id, r.Proposal.ModelId, r.Proposal.Trigger, r.Proposal.Value, r.Proposal.Limit, r.Proposal.Text, r.Proposal.Cells, r.Proposal.PreviousChampionId, r.Proposal.PreviousQualifies, r.Proposal.SettingsHash, r.ProposedOn, r.ProposedAt, r.State, r.Actor, r.DecidedAt, r.AutoSettingId))],
            new MlSettingsView(s.Influence.On, s.Influence.Id, s.AutoPromote.On, s.AutoPromote.Id, s.AutoRollback.On, s.AutoRollback.Id),
            [
                "A proposal changes nothing by itself. Applying it moves the champion to challenger (shadow); the owner applies it, or the automatic setting ml.auto_rollback does when it is ON (default OFF).",
                "Re-promotion is always the owner's act with a fresh password. Applying a rollback never re-promotes the previous champion, even when it qualifies: there is no champion until the owner names one. No order is placed by any of this.",
            ]));
    }
}
