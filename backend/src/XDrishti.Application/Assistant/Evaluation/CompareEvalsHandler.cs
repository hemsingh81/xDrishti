using XDrishti.Application.Abstractions.Ai;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Assistant.Evaluation;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Assistant.Evaluation;

/// <summary>
/// Compares a run with a baseline set by set over the same cases (spec P9-09 AC 12, D3): counts, discordant pairs, the exact sign-test p and the plain sentence about how many pairs it takes. It flags a regression and never blocks by itself: the gate is the thresholds. Read only.
/// </summary>
internal sealed class CompareEvalsHandler(IEvalStore store) : IQueryHandler<CompareEvalsQuery, Result<EvalComparisonView>>
{
    public async ValueTask<Result<EvalComparisonView>> HandleAsync(CompareEvalsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var run = await store.GetAsync(query.RunId, cancellationToken);
        if (run is null)
        {
            return Result.Failure<EvalComparisonView>(EvalErrors.NotFound(query.RunId));
        }

        StoredEvalRun? baseline;
        if (query.BaselineId is { } named)
        {
            baseline = await store.GetAsync(named, cancellationToken);
            if (baseline is null)
            {
                return Result.Failure<EvalComparisonView>(EvalErrors.NotFound(named));
            }
        }
        else
        {
            var current = await store.CurrentBaselineAsync(cancellationToken);
            baseline = current is null ? null : await store.GetAsync(current.RunId, cancellationToken);
            if (baseline is null)
            {
                return Result.Failure<EvalComparisonView>(EvalErrors.NoBaseline());
            }
        }

        var now = Passes(await store.CasesAsync(run.Id, cancellationToken));
        var before = Passes(await store.CasesAsync(baseline.Id, cancellationToken));
        var sets = RunComparison.Compare(before, now, EvalMapping.SummaryOf(run));
        var differences = EvalFingerprint.Differences(EvalPrints.Of(run), EvalPrints.Of(baseline));
        return Result.Success(new EvalComparisonView(run.Id, baseline.Id, sets, sets.Any(s => s.Regression), differences));
    }

    /// <summary>Set to case id to "all repeats passed" (pass^k).</summary>
    private static Dictionary<string, IReadOnlyDictionary<string, bool>> Passes(IReadOnlyList<StoredEvalCase> cases) =>
        cases.GroupBy(c => c.Set, StringComparer.Ordinal).ToDictionary(
            g => g.Key,
            g => (IReadOnlyDictionary<string, bool>)g.GroupBy(c => c.CaseId, StringComparer.Ordinal).ToDictionary(c => c.Key, c => c.All(x => x.Passed), StringComparer.Ordinal),
            StringComparer.Ordinal);
}

/// <summary>The fingerprint inputs of a stored run, rebuilt from its own columns.</summary>
internal static class EvalPrints
{
    public static FingerprintInputs Of(StoredEvalRun run) =>
        new(run.ModelSha256, run.RuntimeDigest, EvalPromptJson.Read(run.PromptVersionsJson), run.SetsHash, run.ThresholdsHash, run.FixtureHash, run.Profile);
}
