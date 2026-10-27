using XDrishti.Application.Abstractions.Ai;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Domain.Assistant.Drafting;
using XDrishti.Domain.Lab;

namespace XDrishti.Application.Ai.Drafting;

/// <summary>What the owner's confirm made: the stored version, the quick test and whether the owner edited the text. <c>RunError</c> is the code of the refusal when a requested quick test could not start (the version is stored anyway).</summary>
public sealed record DraftConfirmed(string StrategyId, int Version, Guid? LabRunId, bool Edited, bool Auto, string? RunError);

/// <summary>The deterministic summary of the linked quick test (spec P9-07 AC 9): counts and the run's own labels, never a win rate, expectancy or verdict.</summary>
public sealed record DraftRunSummary(string State, int? InstrumentsRun, int? Skipped, int? Trades, int? OpenAtEnd, IReadOnlyList<string> Labels, IReadOnlyList<LabProblem> Problems, string? Error);

/// <summary>A draft as the API serves it (spec P9-07 Contract).</summary>
public sealed record DraftView(
    long Id,
    string Status,
    string Idea,
    IReadOnlyList<string> Understood,
    IReadOnlyList<DraftAssumption> Assumptions,
    IReadOnlyList<DraftNotExpressed> NotExpressed,
    IReadOnlyList<string> Unsupported,
    DraftRequestedTest RequestedTest,
    string? Yaml,
    IReadOnlyList<StrategyIssue> Problems,
    IReadOnlyList<DraftFinding> Findings,
    int Attempts,
    string? ModelId,
    string? PromptVersion,
    string? FailReason,
    DraftConfirmed? Confirmed,
    DraftRunSummary? RunSummary,
    DateTimeOffset? CreatedAt)
{
    /// <summary>A job that has not finished: only the idea is known.</summary>
    public static DraftView InProgress(long id, string idea) =>
        new(id, DraftStatus.Drafting, idea, [], [], [], [], DraftRequestedTest.None, null, [], [], 0, null, null, null, null, null, null);

    /// <summary>The served form of a stored draft, with its decision and the linked run when there are any.</summary>
    public static DraftView Of(StoredDraft d, StoredDraftDecision? decision, LabRun? run)
    {
        ArgumentNullException.ThrowIfNull(d);
        var answer = d.Answer;
        var status = decision is null ? d.Status : decision.Decision == "confirmed" ? DraftStatus.Confirmed : DraftStatus.Discarded;
        var confirmed = decision is { Decision: "confirmed" } c ? new DraftConfirmed(c.StrategyId ?? string.Empty, c.StrategyVersion ?? 0, c.LabRunId, c.Edited, c.Auto, c.RunError) : null;
        return new DraftView(
            d.Id, status, d.Idea, answer?.Understood ?? [], answer?.Assumptions ?? [], answer?.NotExpressed ?? [], answer?.Unsupported ?? [], answer?.RequestedTest ?? DraftRequestedTest.None, d.Yaml, d.Problems, d.Findings,
            d.Attempts.Count, d.ModelId, d.PromptVersion, d.FailReason, confirmed, run is null ? null : Summary(run), d.CreatedAt);
    }

    private static DraftRunSummary Summary(LabRun run)
    {
        var state = LabRunStates.Code(run.State);
        var counts = run.State == LabRunState.Succeeded ? run.Counts : null;
        var problems = run.State is LabRunState.Failed or LabRunState.Cancelled or LabRunState.Interrupted ? run.Problems : [];
        return new DraftRunSummary(state, counts?.InstrumentsRun, counts?.Skipped, counts?.Trades, counts?.OpenAtEnd, run.Labels, problems, run.State == LabRunState.Succeeded ? null : run.Error);
    }
}
