using XDrishti.Domain.Common;

namespace XDrishti.Application.Ai.Proposals;

/// <summary>The expected failures of the proposals API (spec P9-08 Contract).</summary>
public static class ProposalErrors
{
    public const string InvalidCode = "assistant.invalid";
    public const string DisabledCode = "assistant.disabled";
    public const string NotFoundCode = "assistant.proposal_not_found";
    public const string ClosedCode = "assistant.proposal_closed";
    public const string StaleCode = "assistant.proposal_stale";
    public const string OwnerOnlyCode = "assistant.owner_only";
    public const string CapabilityOffCode = "assistant.capability_off";
    public const string BusyCode = "assistant.busy";

    public const int MaxRejectReason = 300;

    /// <summary>The owner's own verb on the machine (<c>xd assistant propose</c>): it may ask for proposals, which are rows in the inbox only.</summary>
    public const string CliActor = "cli:xd";

    public static Error Invalid(string message) => Error.Validation(InvalidCode, message);

    public static Error Disabled() => Error.Conflict(DisabledCode, "Proposals are switched off (Assistant:Proposals:Enabled and the assistant's master switch).");

    public static Error Busy() => Error.Conflict(BusyCode, "The model is busy with another job; try again when it is done.");

    public static Error NotFound(long id) => Error.NotFound(NotFoundCode, $"No proposal {id} is stored.");

    public static Error Closed(string state) => Error.Conflict(ClosedCode, $"The proposal is {state}; it can no longer be decided.");

    public static Error Stale(string code, string detail) => Error.Conflict(StaleCode, $"The proposal no longer passes the engine's checks ({code}): {detail} Nothing was changed.");

    public static Error OwnerOnly() => Error.Forbidden(OwnerOnlyCode, "Only the signed-in owner decides on a proposal.");

    public static Error CapabilityOff() => Error.Conflict(CapabilityOffCode, "Accepting a proposal is switched off (ai.settings.allow_proposal_accept); the owner switches it on in the assistant's settings.");
}
