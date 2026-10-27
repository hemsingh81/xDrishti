using XDrishti.Application.Abstractions.Ai;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Assistant.Evaluation;

namespace XDrishti.Application.Assistant.Evaluation;

/// <summary>
/// Whether the accepted baseline still describes the assistant as it is now (spec P9-09 AC 10). The current fingerprint is computed from the files, the prompt list and the model the server last reported, with no network call of its own: a changed prompt version, set,
/// threshold or fixture makes the baseline stale at once. While the model server is not running its identity is unknown and is not compared (the status says so); a baseline is never called stale because a server is off.
/// </summary>
internal sealed class GetEvalStatusHandler(IEvalFiles files, IEvalStore store, IPromptVersions prompts, IEvalEnvironment environment) : IQueryHandler<GetEvalStatusQuery, EvalStatusView>
{
    public const string NoBaselineText = "no accepted eval run";

    public async ValueTask<EvalStatusView> HandleAsync(GetEvalStatusQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var newest = await store.NewestGatingAsync(cancellationToken);
        var baseline = await store.CurrentBaselineAsync(cancellationToken);
        var read = await files.ReadAsync(cancellationToken);
        var world = read.IsSuccess ? EvalWorld.Load(read.Value) : null;
        var model = await environment.ModelAsync(cancellationToken);
        var baselineRun = baseline is null ? null : await store.GetAsync(baseline.RunId, cancellationToken);
        var profile = baselineRun?.Profile ?? "main";
        var current = world is { IsSuccess: true }
            ? new FingerprintInputs(model.Known ? model.Sha256 : baselineRun?.ModelSha256 ?? string.Empty, model.Known ? model.RuntimeDigest : baselineRun?.RuntimeDigest ?? string.Empty, prompts.All, world.Value.SetsHash, world.Value.ThresholdsHash, world.Value.FixtureHash, model.Known ? ProfileOf(model.Id) : profile)
            : new FingerprintInputs(string.Empty, string.Empty, prompts.All, string.Empty, string.Empty, string.Empty, profile);
        var currentFingerprint = EvalFingerprint.Of(current);
        var newestVerdict = newest?.Verdict;
        if (baselineRun is null)
        {
            var failed = newest is not null && newest.Verdict != EvalVerdict.Pass;
            return new EvalStatusView(null, null, currentFingerprint, false, [], failed ? "failed" : "not_yet_evaluated", newest?.FinishedAt, NoBaselineText, newest?.Id, newestVerdict);
        }

        var reasons = new List<string>();
        if (world is not { IsSuccess: true })
        {
            reasons.Add("eval files");
        }
        else
        {
            reasons.AddRange(EvalFingerprint.Differences(current, EvalPrints.Of(baselineRun)));
        }

        var stale = reasons.Count > 0;
        var state = newest is not null && newest.Verdict != EvalVerdict.Pass && newest.FinishedAt > baselineRun.FinishedAt ? "failed" : stale ? "not_yet_evaluated" : "passed";
        var detail = stale ? "the accepted run is stale: " + string.Join(", ", reasons) : model.Known ? "the accepted run matches the assistant as it is" : "the accepted run matches the files and prompts; the model server is not running, so the model was not compared";
        return new EvalStatusView(baselineRun.Id, baselineRun.Fingerprint, currentFingerprint, stale, reasons, state, baselineRun.FinishedAt, detail, newest?.Id, newestVerdict);
    }

    private static string ProfileOf(string modelId) => modelId.Contains("gpt-oss", StringComparison.OrdinalIgnoreCase) ? "gptoss" : "main";
}
