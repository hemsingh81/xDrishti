using System.Text;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>
/// <see cref="IMlInfluenceGate"/> (CF-10 part 10c-2, ADR 0217): the predicate plus "is the current champion". Fail closed: a model that is unknown, not champion, or whose predicate does not hold is refused; a score is read only after an allow and a failed read gives no score. Each answer is recorded
/// in <c>research.ml_influence_checks</c> when it differs from the last one stored for the model.
/// </summary>
internal sealed class MlInfluenceGate(
    MlInfluenceEvaluator evaluator,
    IMlInfluenceChecks checks,
    IQueryHandler<ScoreWithModelQuery, Result<ModelScoreView>> scorer,
    TimeProvider time) : IMlInfluenceGate
{
    public async ValueTask<MlInfluenceAnswer> AnswerAsync(long modelId, CancellationToken cancellationToken)
    {
        var evaluation = await evaluator.EvaluateAsync(modelId, cancellationToken);
        if (evaluation is null)
        {
            return MlInfluenceAnswer.Refused(modelId, "model_not_found", "No model with this id is registered.");
        }

        var terms = evaluation.Terms;

        // shortcut: the inputs hash and the stored terms carry reason codes and term states, not the figures behind them, so two evaluations with different figures but the same states dedupe to one row. Put the figures in the term JSON and in the hash when the first real evidence source lands.
        var hash = MlInfluenceEvaluator.Hash(InputsText(evaluation.SettingHash, evaluation.MayInfluence, terms));
        await checks.AppendIfChangedAsync(new MlInfluenceCheck(modelId, time.GetUtcNow(), evaluation.MayInfluence, terms, evaluation.SettingHash, hash), cancellationToken);
        return new MlInfluenceAnswer(modelId, evaluation.MayInfluence, evaluation.Reasons);
    }

    public async ValueTask<MlGatedScore> ScoreAsync(long modelId, IReadOnlyList<double> features, CancellationToken cancellationToken)
    {
        // shortcut: a full answer (registry read, ledger read, one check lookup) is made for every vector; fine while no consumer scores in bulk. Add "answer once, then score many" before a consumer scores a whole plan.
        ArgumentNullException.ThrowIfNull(features);
        var answer = await AnswerAsync(modelId, cancellationToken);
        if (!answer.MayInfluence)
        {
            return new MlGatedScore(answer, null);
        }

        var scored = await scorer.HandleAsync(new ScoreWithModelQuery(modelId, features, 0), cancellationToken);
        return scored.IsSuccess && double.IsFinite(scored.Value.Probability) && scored.Value.Probability is >= 0 and <= 1
            ? new MlGatedScore(answer, scored.Value.Probability)
            : new MlGatedScore(MlInfluenceAnswer.Refused(modelId, "score_unavailable", "The model could not be scored; no score is handed out."), null);
    }

    private static string InputsText(string settingHash, bool may, IReadOnlyList<MlTermResult> terms)
    {
        var text = new StringBuilder(settingHash).Append('|').Append(may ? "may" : "may_not");
        foreach (var t in terms)
        {
            text.Append('|').Append(t.Name).Append('=').Append(t.State).Append(':').AppendJoin(',', t.Reasons.Select(r => r.Code));
        }

        return text.ToString();
    }
}
