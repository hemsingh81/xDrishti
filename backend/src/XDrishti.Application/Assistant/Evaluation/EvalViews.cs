using XDrishti.Domain.Assistant.Evaluation;

namespace XDrishti.Application.Assistant.Evaluation;

/// <summary>A run as the API and the verb show it (spec P9-09 Contract). <paramref name="IsBaseline"/>: the owner accepted it.</summary>
public sealed record EvalRunView(
    Guid Id, DateTimeOffset StartedAt, DateTimeOffset FinishedAt, string Target, string Profile, IReadOnlyList<string> Sets, int Repeats, bool Gating, string ModelId, string ModelSha256, string RuntimeDigest, string Fingerprint,
    string Status, string Verdict, string Actor, bool IsBaseline, RunSummary Summary);

/// <summary>One stored case repeat. The answer is fixture data only.</summary>
public sealed record EvalCaseView(string Set, string CaseId, int Repeat, bool Passed, bool? Grounded, double? Score, string Detail, string Answer, Guid? SessionId, int ToolCalls, int LatencyMs, int? PromptTokens, int? CompletionTokens);

/// <summary>A run with its cases and the prompts and parameters it measured.</summary>
public sealed record EvalDetailView(EvalRunView Run, IReadOnlyList<EvalCaseView> Cases, string PromptVersions, string Parameters);

/// <summary>The comparison of two runs (spec AC 12).</summary>
public sealed record EvalComparisonView(Guid RunId, Guid BaselineId, IReadOnlyList<SetComparison> Sets, bool Regression, IReadOnlyList<string> Differences);

/// <summary>
/// The status of the evaluation (spec AC 10). <paramref name="Stale"/> is true when the current fingerprint differs from the baseline's; <paramref name="Reasons"/> names what differs. <paramref name="State"/> feeds the assistant status:
/// <c>passed</c> only while a baseline exists and is not stale, <c>failed</c> when the newest gating run is not PASS, else <c>not_yet_evaluated</c>.
/// </summary>
public sealed record EvalStatusView(Guid? BaselineRunId, string? BaselineFingerprint, string CurrentFingerprint, bool Stale, IReadOnlyList<string> Reasons, string State, DateTimeOffset? At, string Detail, Guid? NewestGatingRunId, string? NewestGatingVerdict);
