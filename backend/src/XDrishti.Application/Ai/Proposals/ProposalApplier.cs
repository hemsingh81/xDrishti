using XDrishti.Application.Abstractions.Ai;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Learning;
using XDrishti.Application.Market.Baskets;
using XDrishti.Domain.Assistant.Proposals;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Ai.Proposals;

/// <summary>
/// What accepting a proposal does (spec P9-08 AC 9, 12): with the accept and auto handlers, the only code of the assistant that writes a hypothesis or changes a basket. A hypothesis is written <c>proposed</c> only (the owner still approves, starts and judges it). A basket change goes through
/// the owner's own basket commands; no command that tracks an instrument, starts a backfill, arms a strategy or places an order is named here. (The add command tracks an untracked instrument itself, so the engine checks the instrument is tracked first, in the same locked step.)
/// </summary>
internal sealed class ProposalApplier(
    ICommandHandler<ProposeHypothesisCommand, HypothesisMoveOutcome> hypotheses,
    ICommandHandler<AddBasketMembersCommand, AddMembersResult> add,
    ICommandHandler<RemoveBasketMembersCommand, RemoveMembersResult> remove,
    IProposalFacts facts)
{
    private const int MaxRationale = 500;

    /// <summary>Applies the proposal as <paramref name="actor"/>; the success value is the reference of what was written (<c>hypothesis:12</c>, <c>basket:&lt;guid&gt;</c>).</summary>
    public async ValueTask<Result<string>> ApplyAsync(long id, string kind, ProposalPayload payload, string reason, string actor, CancellationToken cancellationToken)
    {
        switch (payload)
        {
            case HypothesisPayload h when kind == ProposalKind.Hypothesis:
                var knob = new ExperimentKnob(h.KnobName, ExperimentKnobKind.Parameter, KnobBounds.Normal(h.BaselineValue) ?? h.BaselineValue, KnobBounds.Normal(h.CandidateValue) ?? h.CandidateValue);
                var rationale = $"Assistant proposal #{id}: {reason}";
                var written = await hypotheses.HandleAsync(new ProposeHypothesisCommand(HypothesisSource.Assistant, h.CellKey, knob, rationale.Length <= MaxRationale ? rationale : rationale[..MaxRationale], h.BaselineRunKey, new HypothesisGrid(), actor), cancellationToken);
                return written.IsSuccess ? Result.Success($"hypothesis:{written.Value.Id}") : Result.Failure<string>(written.Error);
            case BasketPayload b when ProposalKind.IsBasket(kind):
                if (await facts.InstrumentAsync(b.Symbol, cancellationToken) is not { } instrument)
                {
                    return Result.Failure<string>(ProposalErrors.Stale(ProposalCodes.CheckInstrumentTracked, "The instrument is not in the master list."));
                }

                if (kind == ProposalKind.BasketAdd)
                {
                    if (!instrument.Tracked)
                    {
                        return Result.Failure<string>(ProposalErrors.Stale(ProposalCodes.CheckInstrumentTracked, "The instrument is not tracked; this never tracks one."));
                    }

                    var added = await add.HandleAsync(new AddBasketMembersCommand(b.BasketId, new AddMembersInput([instrument.Id], $"Assistant proposal #{id}"), actor), cancellationToken);
                    return added.IsSuccess ? Result.Success($"basket:{b.BasketId:D}") : Result.Failure<string>(added.Error);
                }

                var removed = await remove.HandleAsync(new RemoveBasketMembersCommand(b.BasketId, new RemoveMembersInput([instrument.Id]), actor), cancellationToken);
                return removed.IsSuccess ? Result.Success($"basket:{b.BasketId:D}") : Result.Failure<string>(removed.Error);
            default:
                return Result.Failure<string>(ProposalErrors.Invalid("The payload does not fit the kind."));
        }
    }
}
