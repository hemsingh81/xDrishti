using XDrishti.Application.Abstractions.Lab;
using XDrishti.Domain.Assistant.Drafting;

namespace XDrishti.Application.Abstractions.Ai;

/// <summary>One request to the model for a strategy draft: the versioned system prompt with the language sheet, and the user message with the idea in a fenced data block, nothing else.</summary>
public sealed record DraftModelRequest(string System, string User, int MaxOutputTokens, long Seed);

/// <summary>The model's raw answer (to be parsed as data) and what it cost.</summary>
public sealed record DraftModelAnswer(string Text, int? PromptTokens, int? CompletionTokens, string FinishReason, int LatencyMs);

/// <summary>The only way a draft reaches the model (an adapter over the chat model port, no tools). Throws <see cref="ModelUnavailableException"/> when the server fails.</summary>
public interface IStrategyDrafter
{
    ValueTask<DraftModelAnswer> DraftAsync(DraftModelRequest request, CancellationToken cancellationToken);
}

/// <summary>The grammar sheet of the strategy language, generated from code (spec P9-07 D3): the text and its SHA-256 (hex).</summary>
public interface IDraftSheetSource
{
    string Sheet { get; }

    string Hash { get; }
}

/// <summary>A strategy text as plain JSON (scalars as strings) through the existing safe reader; null when it does not read.</summary>
public interface IStrategyTextReader
{
    string? ToJson(string text);
}

/// <summary>One model attempt of a drafting job, as stored: the raw answer, the text it carried, what the validator and the checks said.</summary>
public sealed record DraftAttempt(int Attempt, string ModelText, string? Yaml, IReadOnlyList<StrategyIssue> Problems, IReadOnlyList<DraftFinding> Findings, int LatencyMs, int? PromptTokens, int? CompletionTokens);

/// <summary>A finished draft to store (the id was reserved when the job started).</summary>
public sealed record NewDraft(
    long Id, string Idea, string Status, string? FailReason, string Via, Guid? BasketId, string? Yaml, DraftAnswer? Answer, IReadOnlyList<DraftAttempt> Attempts, IReadOnlyList<StrategyIssue> Problems,
    IReadOnlyList<DraftFinding> Findings, string? ModelId, string? ModelSha256, string PromptVersion, string SheetHash, string ParamsJson, string RequestedBy);

/// <summary>A stored draft.</summary>
public sealed record StoredDraft(
    long Id, string Idea, string Status, string? FailReason, string Via, Guid? BasketId, string? Yaml, DraftAnswer? Answer, IReadOnlyList<DraftAttempt> Attempts, IReadOnlyList<StrategyIssue> Problems,
    IReadOnlyList<DraftFinding> Findings, string? ModelId, string PromptVersion, string SheetHash, string RequestedBy, DateTimeOffset CreatedAt);

/// <summary>A decision to store.</summary>
public sealed record NewDraftDecision(long DraftId, string Decision, string? StrategyId, int? StrategyVersion, Guid? LabRunId, bool Edited, bool Auto, long? SettingsId, string? SnapshotHash, string Actor, string? RunError = null);

/// <summary>A stored decision.</summary>
public sealed record StoredDraftDecision(long DraftId, string Decision, string? StrategyId, int? StrategyVersion, Guid? LabRunId, bool Edited, bool Auto, string Actor, DateTimeOffset DecidedAt, string? RunError = null);

/// <summary>The draft store (<c>ai.strategy_drafts</c>, <c>ai.strategy_draft_decisions</c>), insert-only.</summary>
public interface IDraftStore
{
    /// <summary>Reserves the id of a draft that is about to be made.</summary>
    ValueTask<long> ReserveIdAsync(CancellationToken cancellationToken);

    ValueTask InsertAsync(NewDraft draft, CancellationToken cancellationToken);

    ValueTask<StoredDraft?> GetAsync(long id, CancellationToken cancellationToken);

    /// <summary>The newest drafts first.</summary>
    ValueTask<IReadOnlyList<StoredDraft>> ListAsync(int limit, CancellationToken cancellationToken);

    /// <summary>How many drafts were made between the two instants.</summary>
    ValueTask<int> CountBetweenAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);

    /// <summary>How many auto decisions (<c>auto = true</c>) were made between the two instants, counted from the decision rows.</summary>
    ValueTask<int> CountAutoBetweenAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);

    /// <summary>Takes the one lock that serialises every confirm (check, create, start, decision); released when disposed. Two confirms of one draft, or of two drafts with one strategy id, therefore cannot both create or start.</summary>
    ValueTask<IAsyncDisposable> LockConfirmAsync(CancellationToken cancellationToken);

    ValueTask<StoredDraftDecision?> DecisionAsync(long draftId, CancellationToken cancellationToken);

    /// <summary>Stores the decision unless the draft has one; returns the stored decision and whether this call wrote it.</summary>
    ValueTask<(StoredDraftDecision Decision, bool Created)> InsertDecisionAsync(NewDraftDecision decision, CancellationToken cancellationToken);
}
