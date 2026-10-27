using XDrishti.Domain.Common;

namespace XDrishti.Application.Ai.Drafting;

/// <summary>The expected failures of the drafting API (spec P9-07 Contract).</summary>
public static class DraftErrors
{
    public const string InvalidCode = "assistant.invalid";
    public const string DisabledCode = "assistant.disabled";
    public const string LimitCode = "assistant.limit";
    public const string BusyCode = "assistant.busy";
    public const string NotFoundCode = "assistant.draft_not_found";
    public const string DraftInvalidCode = "assistant.draft_invalid";
    public const string IdTakenCode = "assistant.draft_id_taken";
    public const string OwnerOnlyCode = "assistant.owner_only";
    public const string CapabilityOffCode = "assistant.capability_off";
    public const string DiscardedCode = "assistant.draft_discarded";
    public const string ConfirmedCode = "assistant.draft_confirmed";

    /// <summary>The owner's own verb on the machine (<c>xd assistant draft</c>): it may ask for a draft, which is text only; a draft it asks for is never an auto candidate.</summary>
    public const string CliActor = "cli:xd";

    public const int MinIdea = 10;
    public const int MaxIdea = 2000;

    public static Error Invalid(string message) => Error.Validation(InvalidCode, message);

    public static Error Disabled() => Error.Conflict(DisabledCode, "Strategy drafting is switched off (Assistant:Drafts:Enabled and the assistant's master switch).");

    public static Error Limit(int max) => Error.Conflict(LimitCode, $"The limit of {max} drafts per day is reached.");

    public static Error Busy() => Error.Conflict(BusyCode, "The model is busy with another job; try again when it is done.");

    public static Error NotFound(long id) => Error.NotFound(NotFoundCode, $"No draft {id} is stored.");

    public static Error DraftInvalid() => Error.Validation(DraftInvalidCode, "The draft is not valid, so it cannot be confirmed. Edit the text so that it validates, or discard it.");

    public static Error IdTaken(string id) => Error.Conflict(IdTakenCode, $"A strategy `{id}` already exists (or is built in); rename the draft's id and confirm again.");

    public static Error OwnerOnly() => Error.Forbidden(OwnerOnlyCode, "Only the signed-in owner decides on a draft.");

    public static Error CapabilityOff() => Error.Conflict(CapabilityOffCode, "Confirming a draft is switched off (ai.settings.allow_draft_confirm); the owner switches it on in the assistant's settings.");

    public static Error Discarded() => Error.Conflict(DiscardedCode, "The draft was discarded.");

    public static Error Confirmed() => Error.Conflict(ConfirmedCode, "The draft was confirmed and cannot be discarded.");
}
