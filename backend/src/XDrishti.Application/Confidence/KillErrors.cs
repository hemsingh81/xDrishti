using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Confidence;

/// <summary>The actors of a kill decision (spec CF-12 D3, ADR 0219): the owner (<c>owner:...</c>, from the signed-in session) or the automatic setting (<c>auto</c>, never an owner approval).</summary>
public static class KillActors
{
    /// <summary>The actor of an automatic demotion.</summary>
    public const string Auto = "auto";

    /// <summary>Whether the actor is an owner (<c>owner:</c> and something after it, at most 100 characters).</summary>
    public static bool IsOwner(string? actor) => actor is { } a && a.StartsWith("owner:", StringComparison.Ordinal) && a.Length > "owner:".Length && a.Length <= 100;
}

/// <summary>The expected failures of the kill proposals, the switch and the re-enable (spec CF-12 part 12b, ADR 0219). The codes are part of the API.</summary>
public static class KillErrors
{
    /// <summary>No proposal has this id.</summary>
    public const string ProposalNotFoundCode = "confidence.proposal_not_found";

    /// <summary>The proposal was dismissed and cannot be applied.</summary>
    public const string ProposalClosedCode = "confidence.proposal_closed";

    /// <summary>The automatic setting is OFF: an automatic decision is refused.</summary>
    public const string AutoOffCode = "confidence.auto_off";

    /// <summary>The CF-09 failure boundary is proposal only: the automatic setting never executes it.</summary>
    public const string ProposalOnlyCode = "confidence.proposal_only";

    /// <summary>Only the signed-in owner does this.</summary>
    public const string OwnerOnlyCode = "confidence.owner_only";

    /// <summary>The failing condition behind the pause has not cleared (the spec's `confidence.rule_cleared` problem, named so that it reads as what it says).</summary>
    public const string RuleNotClearedCode = "confidence.rule_not_cleared";

    /// <summary>The cell is not paused.</summary>
    public const string NotPausedCode = "confidence.cell_not_paused";

    /// <summary>A reason of 1 to 300 characters is required.</summary>
    public const string ReasonInvalidCode = "confidence.reason_invalid";

    public static Error ProposalNotFound(long id) => Error.NotFound(ProposalNotFoundCode, $"No kill proposal {id}.");

    /// <summary>The record of the proposal refused the decision.</summary>
    public const string DecisionRefusedCode = "confidence.decision_refused";

    public static Error DecisionRefused(long id) => Error.Conflict(DecisionRefusedCode, $"The record refused to mark kill proposal {id} as decided (the automatic setting is not ON now, or the rule is proposal-only). The cells it names may already be paused; read the proposal again.");

    public static Error ProposalClosed(long id) => Error.Conflict(ProposalClosedCode, $"Kill proposal {id} was dismissed and cannot be applied.");

    public static Error AutoOff() => Error.Forbidden(AutoOffCode, "The automatic kill setting is OFF: only the owner applies a kill proposal.");

    public static Error ProposalOnly() => Error.Forbidden(ProposalOnlyCode, "The CF-09 failure boundary is a proposal only: the automatic setting never executes it.");

    public static Error OwnerOnly() => Error.Forbidden(OwnerOnlyCode, "Only the signed-in owner does this.");

    public static Error RuleNotCleared(string cell, string detail) => Error.Conflict(RuleNotClearedCode, $"The cell {cell} stays paused: the condition behind the pause has not cleared. {detail}");

    public static Error NotPaused(string cell, CellState state) => Error.Conflict(NotPausedCode, $"The cell {cell} is {state.Code()}, not paused: there is nothing to re-enable.");

    public static Error ReasonInvalid() => Error.Validation(ReasonInvalidCode, "A reason of 1 to 300 characters is required.");
}
