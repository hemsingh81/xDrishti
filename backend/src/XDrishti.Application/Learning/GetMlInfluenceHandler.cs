using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>The influence predicate for one registered model, term by term (spec CF-10 part 10c-2, ADR 0217). Read only; the gate is what records checks.</summary>
internal sealed class GetMlInfluenceHandler(MlInfluenceEvaluator evaluator, MlInfluenceOptions options) : IQueryHandler<GetMlInfluenceQuery, Result<MlInfluenceView>>
{
    internal static readonly string[] FixedNotes =
    [
        "Advisory: a model's probability is shown beside the cell figure and used for nothing else. It can influence a decision only if every term holds, the model is the champion and the owner's switch is on; the switch is off by default and cannot be turned on in this version.",
        "An unavailable figure is never a pass. The hold-out rows are not scored yet, the shadow candidates do not record which model scored them, and the calibration detector is not stored per model, so those terms cannot hold today.",
    ];

    public async ValueTask<Result<MlInfluenceView>> HandleAsync(GetMlInfluenceQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var e = await evaluator.EvaluateAsync(query.Id, cancellationToken);
        if (e is null)
        {
            return Result.Failure<MlInfluenceView>(ModelErrors.NotFound(query.Id));
        }

        var facts = e.Facts.HoldOut;
        return Result.Success(new MlInfluenceView(
            query.Id,
            e.Model.Role.Code(),
            e.MayInfluence,
            [.. e.Terms.Select(t => new MlTermView(t.Name, t.State.ToString().ToLowerInvariant(), [.. t.Reasons.Select(Reason)]))],
            [.. e.Reasons.Select(Reason)],
            new MlHoldOutView(e.HoldOut.State, ModelHoldOutLooks.FamilyId(e.Model.Model), e.HoldOut.BlockStart, facts?.Rows, facts?.ProbabilityBeatsNoMl, facts?.PolicyEffectR),
            e.Facts.SwitchOn,
            e.SettingHash,
            options.KeyText(),
            FixedNotes));
    }

    private static MlReasonView Reason(MlInfluenceReason r) => new(r.Code, r.Text);
}
