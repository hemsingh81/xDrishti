using XDrishti.Domain.Assistant.Text;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Abstractions.Ai;

/// <summary>What the fact source read: the facts and what to log about them (never the text).</summary>
public sealed record BriefingInput(BriefingFacts Facts, int InjectionFlags);

/// <summary>Builds the facts of a briefing from stored engine outputs (spec P9-06 D1, D6). Read only; owner-typed free text never enters.</summary>
public interface IBriefingFactSource
{
    /// <summary>The plan date a daily briefing is for when none is named: the newest plan's date, or null when no plan is stored.</summary>
    ValueTask<DateOnly?> NewestPlanDateAsync(CancellationToken cancellationToken);

    /// <summary>The facts of <paramref name="kind"/> and <paramref name="subject"/>; <c>assistant.briefing_not_found</c> when there is no plan, <c>assistant.briefing_not_evaluable</c> when a week has no digest.</summary>
    ValueTask<Result<BriefingInput>> BuildAsync(string kind, string subject, bool portfolioContext, CancellationToken cancellationToken);
}

/// <summary>One request to the model for a briefing text: the versioned system prompt and the facts block, nothing else (spec P9-06).</summary>
public sealed record BriefingRequest(string System, string User, int MaxOutputTokens, long Seed);

/// <summary>The model's answer: the text and what it cost.</summary>
public sealed record BriefingWrite(string Text, int? PromptTokens, int? CompletionTokens, string FinishReason, int LatencyMs);

/// <summary>The only way a briefing reaches the model (an adapter over the chat model port). Throws <see cref="ModelUnavailableException"/> when the server fails.</summary>
public interface IBriefingWriter
{
    ValueTask<BriefingWrite> WriteAsync(BriefingRequest request, CancellationToken cancellationToken);
}

/// <summary>A refused attempt's finding, as stored.</summary>
public sealed record StoredFinding(string Code, string Token, int Offset);

/// <summary>One model attempt, stored with its text and findings.</summary>
public sealed record BriefingAttemptRecord(int Attempt, string Text, bool Passed, IReadOnlyList<StoredFinding> Findings, int LatencyMs, int? PromptTokens, int? CompletionTokens);

/// <summary>A briefing to store. The version is chosen by the store.</summary>
public sealed record NewBriefing(
    string Kind, string Subject, string Source, string? Reason, string Text, string DeterministicText, string FactsJson, string FactsHash, string? ModelId, string? ModelSha256,
    string PromptVersion, string ParamsJson, int WordCount, string BuiltBy, IReadOnlyList<BriefingAttemptRecord> Attempts, bool NewVersion);

/// <summary>A stored briefing.</summary>
public sealed record StoredBriefing(
    long Id, string Kind, string Subject, int Version, string Source, string? Reason, string Text, string DeterministicText, string FactsJson, string FactsHash, string? ModelId, string PromptVersion,
    int WordCount, DateTimeOffset CreatedAt, IReadOnlyList<BriefingAttemptRecord> Attempts);

/// <summary>The briefing store (<c>ai.briefings</c>, <c>ai.briefing_attempts</c>), insert-only: the first row of a (kind, subject) wins and a later version is appended only on request.</summary>
public interface IBriefingStore
{
    /// <summary>Stores the briefing unless its (kind, subject) has one and no new version was asked for; returns the stored row and whether this call wrote it.</summary>
    ValueTask<(StoredBriefing Stored, bool Created)> InsertAsync(NewBriefing briefing, CancellationToken cancellationToken);

    /// <summary>The newest version of a (kind, subject), or null.</summary>
    ValueTask<StoredBriefing?> FindAsync(string kind, string subject, CancellationToken cancellationToken);

    /// <summary>The newest briefings first (attempts included), optionally of one kind.</summary>
    ValueTask<IReadOnlyList<StoredBriefing>> ListAsync(string? kind, int limit, CancellationToken cancellationToken);
}
