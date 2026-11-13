using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Lab;

/// <summary>The expected failures of the Lab API (spec P5-01, ADR 0057). The store's own codes (<c>lab.version_exists</c>, <c>lab.builtin_readonly</c>, <c>lab.id_clash</c>) pass through.</summary>
public static class LabErrors
{
    /// <summary>The text has at least one error; the located problems are in the error payload under <see cref="ProblemsKey"/>.</summary>
    public const string InvalidStrategyCode = "lab.invalid_strategy";

    /// <summary>A stored text no longer reads under the rules of today, so it cannot be cloned.</summary>
    public const string StoredUnreadableCode = "lab.stored_unreadable";

    /// <summary>No such family or version.</summary>
    public const string NotFoundCode = "lab.not_found";

    /// <summary>The text is larger than <see cref="MaxTextBytes"/>; refused before it is read.</summary>
    public const string TextTooLargeCode = "lab.text_too_large";

    /// <summary>The post of a run is not valid (a missing strategy, two universes, an unknown method, an empty basket, an unknown symbol, a window the wrong way round): the API answers 422.</summary>
    public const string InvalidInputCode = "lab.invalid_input";

    /// <summary>No stored bar falls in the window of the run: 409.</summary>
    public const string NoDataCode = "lab.no_data";

    /// <summary>The run has finished and cannot be cancelled: 409.</summary>
    public const string NotCancellableCode = "lab.not_cancellable";

    /// <summary>The run has no stored full-test result to report on (a quick test, or a run that has not succeeded): 409.</summary>
    public const string NoReportCode = "lab.no_report";

    /// <summary>A full or robust run of a version with no registration while enforcement is on (CF-06): 422.</summary>
    public const string NotRegisteredCode = "lab.not_registered";

    /// <summary>A window that reaches the locked hold-out (CF-06): 422.</summary>
    public const string HoldOutTouchedCode = "lab.holdout_touched";

    /// <summary>The version already has a registration, which is frozen (CF-06): 409.</summary>
    public const string RegistrationFrozenCode = "lab.registration_frozen";

    /// <summary>The version already took its one look at the hold-out (CF-06): 409.</summary>
    public const string HoldOutUsedCode = "lab.holdout_used";

    /// <summary>The stored history is too short to keep a hold-out, or the plan cannot be run on it (CF-06): 409.</summary>
    public const string HoldOutUnavailableCode = "lab.holdout_unavailable";

    /// <summary>The block is still filling: its last session has not closed (HO-01): 409.</summary>
    public const string HoldOutFillingCode = "lab.holdout_filling";

    /// <summary>The block is not the earliest unreleased one (HO-01): 409.</summary>
    public const string HoldOutOrderCode = "lab.holdout_order";

    /// <summary>The block has no fresh look and the release did not say it forfeits it (HO-01): 409.</summary>
    public const string HoldOutUnlookedCode = "lab.holdout_unlooked";

    /// <summary>The block is released already (HO-01): 409.</summary>
    public const string HoldOutReleasedCode = "lab.holdout_released";

    /// <summary>A block that is still filling cannot be looked at or released; it seals when its last session has closed.</summary>
    public static Error HoldOutFilling(DateOnly start, DateOnly end) =>
        Error.Conflict(HoldOutFillingCode, $"The hold-out block that starts on {start:yyyy-MM-dd} is still filling; it seals when the session of {end:yyyy-MM-dd} (or the last session before it) has closed.");

    /// <summary>A release out of order.</summary>
    public static Error HoldOutOrder(DateOnly start, DateOnly earliest) =>
        Error.Conflict(HoldOutOrderCode, $"The hold-out block that starts on {start:yyyy-MM-dd} is not the earliest unreleased block; that is the block that starts on {earliest:yyyy-MM-dd}.");

    /// <summary>An unlooked block released without the forfeit.</summary>
    public static Error HoldOutUnlooked(DateOnly start) =>
        Error.Conflict(HoldOutUnlookedCode, $"The hold-out block that starts on {start:yyyy-MM-dd} has no fresh look. Releasing it forfeits its fresh looks for good; send forfeitLook: true to release it anyway.");

    /// <summary>A second release.</summary>
    public static Error HoldOutReleased(DateOnly start) =>
        Error.Conflict(HoldOutReleasedCode, $"The hold-out block that starts on {start:yyyy-MM-dd} is already released; the release is permanent.");

    /// <summary>A full test on a basket that is not in the owner's registered list (BK-11).</summary>
    public const string UniverseNotRegisteredCode = "lab.universe_not_registered";

    /// <summary>The basket is outside the registered list; the message names the list. An unregistered basket can still be simulated alone (the run is labelled exploratory).</summary>
    public static Error UniverseNotRegistered(string basketName, IReadOnlyList<string> registered) =>
        Error.Validation(UniverseNotRegisteredCode, $"The basket `{basketName}` is not a registered basket, so a full test of a strategy on it is refused. " + XDrishti.Domain.Learning.UniverseSettingsRules.RegisteredMessage(registered) + " The owner changes the list (PUT /api/universe/settings); a simulation of the basket alone is still possible, and is left out of the What-works ranking.");

    /// <summary>True for the codes the API answers with 422.</summary>
    public static bool IsUnprocessable(string code) => code is InvalidInputCode or NotRegisteredCode or HoldOutTouchedCode or UniverseNotRegisteredCode;

    /// <summary>The name of the payload entry that carries the problems.</summary>
    public const string ProblemsKey = "problems";

    /// <summary>The most bytes (UTF-8) of a strategy text the Lab accepts (owner answer to P5-01 question 5); the file reader's own limit is higher.</summary>
    public const int MaxTextBytes = 64 * 1024;

    /// <summary>The text is refused: <see cref="ErrorKind.Validation"/>, the API answers 422.</summary>
    public static Error InvalidStrategy(IReadOnlyList<StrategyIssue> problems) =>
        new(InvalidStrategyCode, "The strategy has problems; nothing was stored.", ErrorKind.Validation, Payload: new Dictionary<string, object> { [ProblemsKey] = problems });

    /// <summary>A family or a version that is not stored.</summary>
    public static Error NotFound(string id, int? version = null) =>
        Error.NotFound(NotFoundCode, version is null ? $"No strategy `{id}` is stored." : $"The strategy `{id}` has no version {version}.");

    /// <summary>A refused post of a run.</summary>
    public static Error InvalidInput(string message) => Error.Validation(InvalidInputCode, message);

    /// <summary>A run id that is not stored.</summary>
    public static Error RunNotFound(Guid id) => Error.NotFound(NotFoundCode, $"No Lab run {id:D} is stored.");

    /// <summary>A basket that is not stored or is archived.</summary>
    public static Error BasketNotFound(Guid id) => Error.NotFound(NotFoundCode, $"No basket {id:D} is stored, or it is archived.");

    /// <summary>The window holds no stored bar.</summary>
    public static Error NoData(DateOnly from, DateOnly to) =>
        Error.Conflict(NoDataCode, $"No stored 1-minute bar falls between {from:yyyy-MM-dd} and {to:yyyy-MM-dd}.");

    /// <summary>A finished run.</summary>
    public static Error NotCancellable(string state) => Error.Conflict(NotCancellableCode, $"The run is {state} and cannot be cancelled.");

    /// <summary>A run without a report.</summary>
    public static Error NoReport(string state, string method) =>
        Error.Conflict(NoReportCode, $"A report needs a succeeded full or robust run; this run is {state} and its method is {method}.");

    /// <summary>The issue for a text over the limit.</summary>
    public static StrategyIssue TooLarge() =>
        new(string.Empty, TextTooLargeCode, $"The strategy text is larger than {MaxTextBytes} bytes.", "error", null, null, null, null, null, null, null);

    /// <summary>A built-in is read-only: a conflict (409), whatever kind the store gave it.</summary>
    public static Error AsConflict(Error storeError) =>
        storeError.Code == StrategyStoreErrors.BuiltInReadOnlyCode ? Error.Conflict(storeError.Code, storeError.Message) : storeError;

    /// <summary>An unregistered version while registration is enforced.</summary>
    public static Error NotRegistered(string id, int version) =>
        Error.Validation(NotRegisteredCode, $"The strategy `{id}` version {version} has no pre-registration; a full or robust run needs one (PUT /api/lab/strategies/{id}/versions/{version}/registration).");

    /// <summary>A window that reaches the hold-out.</summary>
    public static Error HoldOutTouched(DateOnly start, DateOnly from, DateOnly to) =>
        Error.Validation(HoldOutTouchedCode, $"The window {from:yyyy-MM-dd} to {to:yyyy-MM-dd} reaches the locked hold-out, which starts on {start:yyyy-MM-dd}; no Lab run reads it.");

    /// <summary>A second registration.</summary>
    public static Error RegistrationFrozen(string id, int version) =>
        Error.Conflict(RegistrationFrozenCode, $"The strategy `{id}` version {version} is already registered; a registration is frozen. A new version needs its own.");

    /// <summary>A registration written after a full run froze the plan would not be before the search, so it is refused (same code, a 409).</summary>
    public static Error RegistrationAfterFreeze(string id, int version) =>
        Error.Conflict(RegistrationFrozenCode, $"The strategy `{id}` version {version} already has a frozen plan from a full run; a registration written now would not be before the data was searched. A new version needs its own, written first.");

    /// <summary>A second look at the hold-out.</summary>
    public static Error HoldOutUsed(string id, int version) =>
        Error.Conflict(HoldOutUsedCode, $"The strategy `{id}` version {version} has already looked at the hold-out; the stored result is unchanged (GET /api/lab/holdout).");

    /// <summary>No hold-out can be kept or run.</summary>
    public static Error HoldOutUnavailable(string reason) => Error.Conflict(HoldOutUnavailableCode, reason);
}
