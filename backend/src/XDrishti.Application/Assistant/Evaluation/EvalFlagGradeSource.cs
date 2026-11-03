using XDrishti.Application.Abstractions.Ai;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Ai;
using XDrishti.Domain.Assistant.Evaluation;
using XDrishti.Domain.Learning.Extensions;

namespace XDrishti.Application.Assistant.Evaluation;

/// <summary>
/// The grade protocol L reads (spec P15-08, replacing the unavailable default): the set D result of the newest ACCEPTED eval run, with the model file hash and the <c>announcement-flag</c> prompt hash that run stored. Null (so protocol L is NOT_EVALUABLE, never a pass) when nothing is
/// accepted, when the accepted run did not grade set D, when set D was not evaluable or not run (that is not a failure either), or when the run stored no model hash or no announcement-flag prompt. The grade is read from the stored run, never recomputed, and nothing here calls a model.
/// </summary>
internal sealed class EvalFlagGradeSource(IEvalStore store) : IFlagGradeSource
{
    public async ValueTask<FlagGrade?> NewestAsync(CancellationToken cancellationToken)
    {
        var baseline = await store.CurrentBaselineAsync(cancellationToken);
        if (baseline is null)
        {
            return null;
        }

        var run = await store.GetAsync(baseline.RunId, cancellationToken);
        if (run is null || run.ModelSha256.Length != 64)
        {
            return null;
        }

        var set = EvalJson.ReadSummary(run.SummaryJson)?.Find("events");
        var prompt = EvalPromptJson.Read(run.PromptVersionsJson).FirstOrDefault(p => p.Name == PromptRegistry.AnnouncementFlag.Name);
        if (set is null || set.Outcome.State is not (SetState.Pass or SetState.Fail) || prompt is null || prompt.Sha256.Length != 64)
        {
            return null;
        }

        return new FlagGrade(run.Id.ToString("D"), run.Fingerprint, set.Cases, run.Gating, set.Outcome.State == SetState.Pass, run.ModelSha256, prompt.Sha256);
    }
}
