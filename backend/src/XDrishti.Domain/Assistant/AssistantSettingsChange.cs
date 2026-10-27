using XDrishti.Domain.Checklist;
using XDrishti.Domain.Common;

namespace XDrishti.Domain.Assistant;

/// <summary>
/// A change of the assistant's switches (spec P9-03, ADR 0137, 0138): the only way a new <c>ai.settings</c> row comes to be. Only an owner actor (<c>owner:...</c>, from the signed-in identity, never from a body) with a reason of 1 to 500 characters and values in range.
/// <c>RaisesExposure</c> is the rule of the password: a change that only reduces exposure is free (the owner can always switch it off); anything that turns something on or raises it needs a fresh password.
/// </summary>
public sealed record AssistantSettingsChange
{
    /// <summary>The longest reason (the column is 500).</summary>
    public const int MaxReasonLength = 500;

    private AssistantSettingsChange(string actor, string reason, bool enabled, bool portfolioContext, int maxToolCalls, bool allowDraftConfirm, bool autoLabQuickTest, bool allowProposalAccept, bool autoAcceptHypothesis)
    {
        AllowDraftConfirm = allowDraftConfirm;
        AutoLabQuickTest = autoLabQuickTest;
        AllowProposalAccept = allowProposalAccept;
        AutoAcceptHypothesis = autoAcceptHypothesis;
        Actor = actor;
        Reason = reason;
        Enabled = enabled;
        PortfolioContext = portfolioContext;
        MaxToolCalls = maxToolCalls;
    }

    /// <summary>The owner who made it.</summary>
    public string Actor { get; }

    /// <summary>Why, trimmed.</summary>
    public string Reason { get; }

    /// <summary>The new value of the master switch.</summary>
    public bool Enabled { get; }

    /// <summary>The new value of the portfolio switch.</summary>
    public bool PortfolioContext { get; }

    /// <summary>The new budget of tool calls per turn.</summary>
    public int MaxToolCalls { get; }

    /// <summary>Whether the owner may confirm a strategy draft.</summary>
    public bool AllowDraftConfirm { get; }

    /// <summary>Whether the dedicated drafting job may queue a quick test of a valid draft on its own.</summary>
    public bool AutoLabQuickTest { get; }

    /// <summary>Used by P9-08.</summary>
    public bool AllowProposalAccept { get; }

    /// <summary>Used by P9-08.</summary>
    public bool AutoAcceptHypothesis { get; }

    /// <summary>Makes the change, or says why not: an owner actor, a reason that fits and a budget in range.</summary>
    public static Result<AssistantSettingsChange> Create(string? actor, string? reason, bool enabled, bool portfolioContext, int maxToolCalls, bool allowDraftConfirm = false, bool autoLabQuickTest = false, bool allowProposalAccept = false, bool autoAcceptHypothesis = false)
    {
        if (!OwnerConfirmation.IsOwnerActor(actor) || actor!.Length > 100)
        {
            return Result.Failure<AssistantSettingsChange>(Error.Forbidden("assistant.owner_only", "Only the signed-in owner changes the assistant's settings."));
        }

        var text = reason?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > MaxReasonLength)
        {
            return Result.Failure<AssistantSettingsChange>(Error.Validation("assistant.reason_invalid", "A change needs a reason of 1 to 500 characters."));
        }

        if (!AssistantSettings.ValuesAreValid(maxToolCalls))
        {
            return Result.Failure<AssistantSettingsChange>(Error.Validation("assistant.values_invalid", $"The budget of tool calls is {AssistantSettings.LowestMaxToolCalls} to {AssistantSettings.HighestMaxToolCalls}."));
        }

        return Result.Success(new AssistantSettingsChange(actor, text, enabled, portfolioContext, maxToolCalls, allowDraftConfirm, autoLabQuickTest, allowProposalAccept, autoAcceptHypothesis));
    }

    /// <summary>
    /// Whether a change to these values turns something on or raises something relative to <paramref name="current"/> (the assistant on, the portfolio context on, a larger budget). Such a change needs the owner's password for this change; a change that only lowers or keeps does not.
    /// A pure function of the states (table test), so the endpoint can ask it before it has an actor.
    /// </summary>
    public static bool RaisesExposure(AssistantSettings current, bool enabled, bool portfolioContext, int maxToolCalls, bool allowDraftConfirm = false, bool autoLabQuickTest = false, bool allowProposalAccept = false, bool autoAcceptHypothesis = false)
    {
        ArgumentNullException.ThrowIfNull(current);
        return (enabled && !current.Enabled) || (portfolioContext && !current.PortfolioContext) || maxToolCalls > current.MaxToolCalls
            || (allowDraftConfirm && !current.AllowDraftConfirm) || (autoLabQuickTest && !current.AutoLabQuickTest) || (allowProposalAccept && !current.AllowProposalAccept) || (autoAcceptHypothesis && !current.AutoAcceptHypothesis);
    }
}
