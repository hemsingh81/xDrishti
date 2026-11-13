using XDrishti.Domain.Common;

namespace XDrishti.Application.Replay.HoldOut;

/// <summary>The expected refusals of the registration and its run (SR-03).</summary>
public static class HoldOutRegistrationErrors
{
    /// <summary>422: the registration cannot be stored; the message starts with the reason word.</summary>
    public const string InvalidCode = "replay.registration_invalid";

    /// <summary>409: a stored input is missing or the sheet changed; nothing was claimed.</summary>
    public const string DriftCode = "replay.registration_drift";

    /// <summary>409: the system look of the block is used.</summary>
    public const string UsedCode = "replay.holdout_used";

    /// <summary>404.</summary>
    public const string NotFoundCode = "replay.registration_not_found";

    /// <summary>409: a newer registration targets the block.</summary>
    public const string SupersededCode = "replay.registration_superseded";

    /// <summary>The name of the auto-register setting.</summary>
    public const string AutoRegister = "replay.holdout.auto_register";

    /// <summary>The name of the auto-run setting.</summary>
    public const string AutoRun = "replay.holdout.auto_run";

    internal static Error Invalid(string reason, string message) => Error.Validation(InvalidCode, reason + ": " + message);

    internal static Error Drift(string message) => Error.Conflict(DriftCode, message + " Nothing was claimed and no bar of the block was read.");

    internal static Error Used(string message) => Error.Conflict(UsedCode, message);

    internal static Error NotFound(Guid id) => Error.NotFound(NotFoundCode, $"No hold-out registration {id:D}.");
}
