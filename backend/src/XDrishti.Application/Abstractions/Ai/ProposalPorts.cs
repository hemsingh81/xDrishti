using XDrishti.Domain.Learning;

namespace XDrishti.Application.Abstractions.Ai;

/// <summary>One request to the model for proposals: the versioned system prompt and the pack in a fenced data block, nothing else (spec P9-08 D1: no tools).</summary>
public sealed record ProposalModelRequest(string System, string User, int MaxOutputTokens, long Seed);

/// <summary>The model's raw answer (to be read as data) and what it cost.</summary>
public sealed record ProposalModelAnswer(string Text, int? PromptTokens, int? CompletionTokens, string FinishReason, int LatencyMs);

/// <summary>The only way a pack reaches the model (an adapter over the chat model port, no tools). Throws <see cref="ModelUnavailableException"/> when the server fails.</summary>
public interface IProposalModel
{
    ValueTask<ProposalModelAnswer> ProposeAsync(ProposalModelRequest request, CancellationToken cancellationToken);
}

/// <summary>A proposal to store: only a proposal that passed every engine check is ever made (spec P9-08 AC 3).</summary>
public sealed record NewProposal(
    string Kind, string DedupeKey, DateOnly CreatedOn, string Subject, string PayloadJson, string Reason, string EvidenceJson, string ChecksJson, string PackHash, string? ModelId, string? ModelSha256, string PromptVersion, string CreatedBy, DateTimeOffset ExpiresAt);

/// <summary>A decision to store.</summary>
public sealed record NewProposalDecision(long ProposalId, string Decision, string Actor, bool Auto, long? SettingsId, string? SnapshotHash, string? Reason, string? ResultRef);

/// <summary>A stored decision.</summary>
public sealed record StoredProposalDecision(long ProposalId, string Decision, string Actor, bool Auto, string? Reason, string? ResultRef, DateTimeOffset DecidedAt);

/// <summary>A stored proposal with its decision, if any.</summary>
public sealed record StoredProposal(
    long Id, string Kind, string DedupeKey, DateOnly CreatedOn, string Subject, string PayloadJson, string Reason, string EvidenceJson, string ChecksJson, string PackHash, string? ModelId, string PromptVersion, string CreatedBy,
    DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, StoredProposalDecision? Decision);

/// <summary>The one write the model's job has: an insert-only row in the inbox (spec P9-08 AC 12). Returns false when the same key was stored on the same India date.</summary>
public interface IProposalInbox
{
    ValueTask<long?> InsertAsync(NewProposal proposal, CancellationToken cancellationToken);
}

/// <summary>Reads of the inbox (the proposal service uses these and the insert only).</summary>
public interface IProposalReader
{
    ValueTask<StoredProposal?> GetAsync(long id, CancellationToken cancellationToken);

    /// <summary>The newest proposals first (by id), optionally of one kind.</summary>
    ValueTask<IReadOnlyList<StoredProposal>> ListAsync(string? kind, int limit, CancellationToken cancellationToken);

    /// <summary>The proposals with no decision whose expiry has not passed.</summary>
    ValueTask<IReadOnlyList<StoredProposal>> PendingAsync(DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Whether the owner rejected a proposal with this key since the instant (the repeat block).</summary>
    ValueTask<bool> RejectedSinceAsync(string dedupeKey, DateTimeOffset since, CancellationToken cancellationToken);

    /// <summary>How many auto decisions were made between the two instants, counted from the decision rows.</summary>
    ValueTask<int> CountAutoBetweenAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}

/// <summary>The decisions of the owner (and of the auto option): only the accept, reject and auto handlers use this port.</summary>
public interface IProposalDecisions
{
    /// <summary>Takes the one lock that serialises every accept, reject and auto decision (read, check, apply, decision); released when disposed.</summary>
    ValueTask<IAsyncDisposable> LockAsync(CancellationToken cancellationToken);

    /// <summary>Stores the decision unless the proposal has one; returns the stored decision and whether this call wrote it.</summary>
    ValueTask<(StoredProposalDecision Decision, bool Created)> InsertAsync(NewProposalDecision decision, CancellationToken cancellationToken);
}

/// <summary>A cell of the newest stored snapshot (window all) with the stored run its trades came from.</summary>
public sealed record CellFact(string Key, string Strategy, int N, double? ExpectancyR, double WinRateLower, bool Enough, int MinTrades, string Basis, DateOnly AsOf, string? BaselineRunKey);

/// <summary>A stored run: its strategy and effective parameters, read from its record.</summary>
public sealed record RunFact(string RunKey, string StrategyId, RunDefinition Definition);

/// <summary>A hypothesis that is open (proposed, approved, running or judged).</summary>
public sealed record OpenHypothesisFact(long Id, string State, string? CellKey, string KnobName);

/// <summary>A basket of the owner with its members now (symbols, upper case).</summary>
public sealed record BasketFact(Guid Id, string Name, bool Archived, bool System, IReadOnlyList<string> MemberSymbols);

/// <summary>An instrument of the master list.</summary>
public sealed record InstrumentFact(Guid Id, string Symbol, bool Tracked, bool Retired);

/// <summary>The trades of one instrument in the stored runs, from the report engine: <paramref name="State"/> is value, insufficient or unavailable.</summary>
public sealed record InstrumentTradesFact(string Symbol, string State, int N, double? ExpectancyLowerR, DateOnly? FirstDate, DateOnly? LastDate, IReadOnlyList<string> RunKeys, string Basis, IReadOnlyList<string> Limits);

/// <summary>
/// What the engine knows, read through the same read handlers and stores the screens use (spec P9-08 AC 1, 3): the pack is built from it and every reference of a proposal is read again from it. Read only. An input that cannot be read is reported as unavailable, never as a value.
/// </summary>
public interface IProposalFacts
{
    ValueTask<IReadOnlyList<CellFact>> CellsAsync(CancellationToken cancellationToken);

    ValueTask<CellFact?> CellAsync(string cellKey, CancellationToken cancellationToken);

    ValueTask<RunFact?> RunAsync(string runKey, CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<OpenHypothesisFact>> OpenHypothesesAsync(CancellationToken cancellationToken);

    /// <summary>One basket by id, a system (portfolio) bucket included (flagged); null when none has this id.</summary>
    ValueTask<BasketFact?> BasketAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The owner's manual baskets, archived ones included.</summary>
    ValueTask<IReadOnlyList<BasketFact>> BasketsAsync(CancellationToken cancellationToken);

    ValueTask<InstrumentFact?> InstrumentAsync(string symbol, CancellationToken cancellationToken);

    ValueTask<InstrumentTradesFact> InstrumentTradesAsync(string symbol, CancellationToken cancellationToken);

    /// <summary>Every instrument with trades in the stored runs; null when the report engine cannot answer (unavailable).</summary>
    ValueTask<IReadOnlyList<InstrumentTradesFact>?> AllInstrumentTradesAsync(CancellationToken cancellationToken);
}
