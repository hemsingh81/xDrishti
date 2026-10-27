using XDrishti.Application.Abstractions.Ai;
using XDrishti.Domain.Assistant.Evaluation;

namespace XDrishti.Application.Assistant.Evaluation;

/// <summary>Maps stored runs and cases to the views (spec P9-09 Contract).</summary>
internal static class EvalMapping
{
    public static EvalRunView Run(NewEvalRun run, RunSummary summary, bool baseline) =>
        new(run.Id, run.StartedAt, run.FinishedAt, run.Target, run.Profile, run.Sets, run.Repeats, run.Gating, run.ModelId, run.ModelSha256, run.RuntimeDigest, run.Fingerprint, run.Status, run.Verdict, run.Actor, baseline, summary);

    public static EvalRunView Run(StoredEvalRun run, RunSummary summary, bool baseline) =>
        new(run.Id, run.StartedAt, run.FinishedAt, run.Target, run.Profile, run.Sets, run.Repeats, run.Gating, run.ModelId, run.ModelSha256, run.RuntimeDigest, run.Fingerprint, run.Status, run.Verdict, run.Actor, baseline, summary);

    public static EvalCaseView Case(StoredEvalCase c) =>
        new(c.Set, c.CaseId, c.Repeat, c.Passed, c.Grounded, c.Score, c.DetailJson, c.Answer, c.SessionId, c.ToolCalls, c.LatencyMs, c.PromptTokens, c.CompletionTokens);

    /// <summary>The summary stored with a run, or an empty INCOMPLETE one when the stored text is damaged (a damaged row is shown, never trusted).</summary>
    public static RunSummary SummaryOf(StoredEvalRun run) => EvalJson.ReadSummary(run.SummaryJson) ?? new RunSummary(EvalVerdict.Incomplete, true, [], string.Empty);
}
