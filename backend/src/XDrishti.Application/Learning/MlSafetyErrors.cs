using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>The actors of an ML safety decision (spec CF-10 D7 and D8, part 10d-1, ADR 0220): the owner (<c>owner:...</c>, from the signed-in session) or the automatic setting (<c>auto</c>, never an owner approval).</summary>
public static class MlActors
{
    /// <summary>The actor of an automatic promotion or rollback.</summary>
    public const string Auto = "auto";

    /// <summary>The prefix of an owner actor.</summary>
    public const string OwnerPrefix = "owner:";

    /// <summary>Whether the actor is an owner (<c>owner:</c> and something after it, at most 100 characters).</summary>
    public static bool IsOwner(string? actor) => actor is { } a && a.StartsWith("owner:", StringComparison.Ordinal) && a.Length > "owner:".Length && a.Length <= 100;
}

/// <summary>The expected failures of promotion, rollback and the ML switches (spec CF-10 part 10d-1, ADR 0220). The codes are part of the API.</summary>
public static class MlSafetyErrors
{
    /// <summary>No rollback proposal has this id (404).</summary>
    public const string RollbackNotFoundCode = "learning.rollback_not_found";

    /// <summary>The rollback proposal was dismissed and cannot be applied (409).</summary>
    public const string RollbackClosedCode = "learning.rollback_closed";

    /// <summary>The automatic rollback setting is OFF: an automatic decision is refused (403).</summary>
    public const string RollbackAutoOffCode = "learning.rollback_auto_off";

    /// <summary>The record refused to mark the rollback proposal as decided (409).</summary>
    public const string RollbackDecisionRefusedCode = "learning.rollback_decision_refused";

    /// <summary>Only the signed-in owner does this (403).</summary>
    public const string OwnerOnlyCode = "learning.owner_only";

    /// <summary>The challenger does not meet the promotion rule (409): the condition does not hold.</summary>
    public const string PromotionRefusedCode = "learning.promotion_refused";

    /// <summary>The previous champion's latest evaluation does not satisfy the predicate without the switch (409).</summary>
    public const string RepromotionRefusedCode = "learning.repromotion_refused";

    /// <summary>The automatic promotion setting is OFF (403).</summary>
    public const string AutoPromoteOffCode = "learning.auto_promote_off";

    /// <summary>A switch change that names no switch, or a reason outside 1 to 500 characters (400).</summary>
    public const string SettingsInvalidCode = "learning.ml_settings_invalid";

    /// <summary>The owner's local designation may not name as champion a model that has a rollback proposal (409).</summary>
    public const string DesignateRolledBackCode = "learning.designate_rolled_back";

    public static Error DesignateRolledBack(long id) =>
        Error.Conflict(DesignateRolledBackCode, $"Model {id} has a rollback proposal (it was, or was proposed to be, moved back to shadow), so it is not named champion from the command line. It comes back only through the owner's promote route (POST /api/learning/models/{id}/promote, with a fresh password), which checks that its latest evaluation still satisfies the influence predicate without the switch.");

    public static Error RollbackNotFound(long id) => Error.NotFound(RollbackNotFoundCode, $"No rollback proposal {id}.");

    public static Error RollbackClosed(long id) => Error.Conflict(RollbackClosedCode, $"Rollback proposal {id} was dismissed and cannot be applied.");

    public static Error RollbackAutoOff() => Error.Forbidden(RollbackAutoOffCode, "The automatic rollback setting (ml.auto_rollback) is OFF: only the owner applies a rollback proposal.");

    public static Error RollbackDecisionRefused(long id) => Error.Conflict(RollbackDecisionRefusedCode, $"The record refused to mark rollback proposal {id} as decided (ml.auto_rollback is not ON now). The champion may already be demoted; read the proposal again.");

    public static Error OwnerOnly() => Error.Forbidden(OwnerOnlyCode, "Only the signed-in owner does this.");

    public static Error PromotionRefused(long id, IReadOnlyList<MlInfluenceReason> reasons) =>
        Error.Conflict(PromotionRefusedCode, $"Model {id} is not promoted: {string.Join(" ", reasons.Select(r => r.Text))}");

    public static Error RepromotionRefused(long id, IReadOnlyList<MlInfluenceReason> reasons) =>
        Error.Conflict(RepromotionRefusedCode, $"Model {id} is not re-promoted: its latest evaluation does not satisfy the predicate without the switch. {string.Join(" ", reasons.Select(r => r.Text))}");

    public static Error AutoPromoteOff() => Error.Forbidden(AutoPromoteOffCode, "The automatic promotion setting (ml.auto_promote) is OFF: only the owner promotes a challenger.");

    public static Error SettingsInvalid(string message) => Error.Validation(SettingsInvalidCode, message);
}
