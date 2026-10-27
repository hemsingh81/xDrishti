using XDrishti.Domain.Assistant.Evaluation;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Abstractions.Ai;

/// <summary>The files of the eval as texts (spec P9-09 Scope): the fixture, the question sets and the thresholds. Read from the repo's <c>eval/</c> folder; their hashes are part of the fingerprint.</summary>
public sealed record EvalFileSet(string Fixture, string DataQuestions, string Plans, string Ideas, string Thresholds);

/// <summary>Reads the eval files. Fails with <c>eval.files_missing</c> when the folder or a file is not there.</summary>
public interface IEvalFiles
{
    ValueTask<Result<EvalFileSet>> ReadAsync(CancellationToken cancellationToken);
}

/// <summary>
/// The tool server of the eval (<c>xd-mcp-eval</c>) as the assistant sees it: the same port as the production tool server, a different instance (spec P9-09 AC 13). Registered on its own so that the eval turn can never be handed the production catalog.
/// </summary>
public interface IEvalToolCatalog : IToolCatalog;

/// <summary>Reads <c>eval_marker.fixture_hash</c> of the database behind the eval tool endpoint, through that endpoint (spec AC 13). Null when there is no marker or the endpoint cannot be reached: either way the run does not start.</summary>
public interface IEvalFixtureProbe
{
    ValueTask<string?> ReadFixtureHashAsync(CancellationToken cancellationToken);
}

/// <summary>The prompts the assistant sends, as (name, version, hash). Taken from the prompt registry; a port so that a test can change a version.</summary>
public interface IPromptVersions
{
    IReadOnlyList<PromptStamp> All { get; }
}

/// <summary>The model and runtime as the eval records them. <paramref name="Known"/> is false while the server is not running.</summary>
public sealed record EvalModelInfo(bool Known, LlmState State, string Id, string Sha256, string RuntimeDigest);

/// <summary>Where the eval learns which model and runtime it is measuring.</summary>
public interface IEvalEnvironment
{
    ValueTask<EvalModelInfo> ModelAsync(CancellationToken cancellationToken);
}

/// <summary>One answer of the assistant under test, with the tool results behind it (null: the audit has none, grounding is not evaluable).</summary>
public sealed record EvalAnswer(string Text, Guid? SessionId, IReadOnlyList<string>? ToolResults, int ToolCalls, int LatencyMs, int? PromptTokens, int? CompletionTokens, string FinishReason);

/// <summary>Asks the assistant one question as <c>Origin: eval</c> (spec P9-09 AC 8) and returns its answer; a refusal comes back as the assistant's own error code.</summary>
public interface IEvalAnswerSource
{
    ValueTask<Result<EvalAnswer>> AskAsync(string question, string actor, CancellationToken cancellationToken);
}

/// <summary>A run to store.</summary>
public sealed record NewEvalRun(
    Guid Id, DateTimeOffset StartedAt, DateTimeOffset FinishedAt, string Target, string Profile, IReadOnlyList<string> Sets, int Repeats, bool Gating, string ModelId, string ModelSha256, string RuntimeDigest, string ParamsJson,
    string PromptVersionsJson, string SetsHash, string ThresholdsHash, string FixtureHash, string Fingerprint, string Status, string Verdict, string SummaryJson, string Actor);

/// <summary>A case repeat to store.</summary>
public sealed record NewEvalCase(string Set, string CaseId, int Repeat, bool Passed, bool? Grounded, double? Score, string DetailJson, string Answer, Guid? SessionId, int ToolCalls, int LatencyMs, int? PromptTokens, int? CompletionTokens);

/// <summary>A stored run.</summary>
public sealed record StoredEvalRun(
    Guid Id, DateTimeOffset StartedAt, DateTimeOffset FinishedAt, string Target, string Profile, IReadOnlyList<string> Sets, int Repeats, bool Gating, string ModelId, string ModelSha256, string RuntimeDigest, string ParamsJson,
    string PromptVersionsJson, string SetsHash, string ThresholdsHash, string FixtureHash, string Fingerprint, string Status, string Verdict, string SummaryJson, string Actor);

/// <summary>A stored case repeat.</summary>
public sealed record StoredEvalCase(long Id, Guid RunId, string Set, string CaseId, int Repeat, bool Passed, bool? Grounded, double? Score, string DetailJson, string Answer, Guid? SessionId, int ToolCalls, int LatencyMs, int? PromptTokens, int? CompletionTokens);

/// <summary>A baseline: a run the owner accepted.</summary>
public sealed record StoredEvalBaseline(long Id, Guid RunId, string AcceptedBy, string? Reason, DateTimeOffset AcceptedAt);

/// <summary>
/// The eval store (<c>ai.eval_runs</c>, <c>ai.eval_cases</c>, <c>ai.eval_baselines</c>), insert-only for the runtime role (spec P9-09 AC 14): a run and its cases are written once, in one transaction, when the run ends; a baseline is a row that says "the owner accepted this run".
/// </summary>
public interface IEvalStore
{
    ValueTask InsertRunAsync(NewEvalRun run, IReadOnlyList<NewEvalCase> cases, CancellationToken cancellationToken);

    ValueTask<StoredEvalRun?> GetAsync(Guid id, CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<StoredEvalRun>> ListAsync(int limit, CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<StoredEvalCase>> CasesAsync(Guid runId, CancellationToken cancellationToken);

    /// <summary>The newest run with <c>gating</c> true, or null.</summary>
    ValueTask<StoredEvalRun?> NewestGatingAsync(CancellationToken cancellationToken);

    /// <summary>The newest baseline row, or null.</summary>
    ValueTask<StoredEvalBaseline?> CurrentBaselineAsync(CancellationToken cancellationToken);

    ValueTask<StoredEvalBaseline?> BaselineOfAsync(Guid runId, CancellationToken cancellationToken);

    /// <summary>Stores the baseline unless the run has one; returns the stored row and whether this call wrote it.</summary>
    ValueTask<(StoredEvalBaseline Baseline, bool Created)> InsertBaselineAsync(Guid runId, string acceptedBy, string? reason, DateTimeOffset at, CancellationToken cancellationToken);
}
