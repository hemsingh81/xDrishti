using XDrishti.Domain.Common;

namespace XDrishti.Domain.Portfolio;

/// <summary>Stable error codes of the holdings module (part of the API contract).</summary>
public static class PortfolioErrors
{
    public const string NotFoundCode = "portfolio.not_found";
    public const string InvalidCode = "portfolio.invalid";
    public const string NotConnectedCode = "portfolio.not_connected";
    public const string SyncRunningCode = "portfolio.sync_running";
    public const string RoleOffCode = "portfolio.role_off";
    public const string NoBucketCode = "portfolio.no_bucket";
    public const string SyncFailedCode = "portfolio.sync_failed";
    public const string ConfirmationRequiredCode = "portfolio.confirmation_required";
    public const string ConfirmationStaleCode = "portfolio.confirmation_stale";
    public const string SettlingCode = "portfolio.settling";
    public const string ConflictCode = "portfolio.conflict";
    public const string AlertLimitCode = "portfolio.alert_limit";

    /// <summary>The most active price alerts there can be (ADR 0022).</summary>
    public const int MaxActiveAlerts = 200;

    public static readonly Error HoldingNotFound = Error.NotFound(NotFoundCode, "The holding does not exist.");

    public static readonly Error BenchmarkNotFound = Error.NotFound(NotFoundCode, "The benchmark index is not in the instrument master. Refresh the instrument list or choose another index.");

    public static readonly Error ActionNotFound = Error.NotFound(NotFoundCode, "That item is not on the Action center now. Reload the list.");

    public static readonly Error AlertNotFound = Error.NotFound(NotFoundCode, "The price alert does not exist.");

    public static readonly Error AlertLimit = Error.Conflict(AlertLimitCode, "There are 200 active price alerts, the most there can be. Cancel one first.");

    public static readonly Error ActionChanged = Error.Conflict(ConflictCode, "The decision was changed meanwhile. Reload and try again.");

    public static readonly Error LotNotFound = Error.NotFound(NotFoundCode, "The lot does not exist.");

    public static readonly Error NotConnected = Error.Conflict(
        NotConnectedCode, "The account is not connected to Dhan. Sign in to Dhan again (Accounts, Connection) and retry.");

    public static readonly Error TokenRefused = Error.Conflict(
        NotConnectedCode, "Dhan refused the saved access token. Sign in to Dhan again (Accounts, Connection) and retry.");

    /// <summary>Only an automatic first import gets this; it never reaches the API.</summary>
    public static readonly Error Settling = Error.Conflict(SettlingCode, "The account's settings changed a moment ago; the automatic first import waits.");

    public static readonly Error SyncRunning = Error.Conflict(SyncRunningCode, "A holdings sync or import of this account is already running.");

    public static readonly Error RoleOff = Error.Conflict(RoleOffCode, "Switch on the Portfolio role of this account first.");

    public static readonly Error NoBucket = Error.Conflict(NoBucketCode, "The account has no portfolio bucket to put holdings in. Switch the Portfolio role off and on again.");

    public static readonly Error BenchmarkChanged = Error.Conflict(ConflictCode, "The benchmark was changed meanwhile. Reload and try again.");

    public static readonly Error LotChanged = Error.Conflict(ConflictCode, "The lot was changed or closed meanwhile. Reload and try again.");

    public static readonly Error SyncFailed = new(SyncFailedCode, "The sync failed unexpectedly; nothing was changed. See the API log.");

    /// <summary>The text recorded on an automatic run that was held back, and the start of the 409 message.</summary>
    public static string SuspiciousResultText(int listedHoldings, int openHoldings, int wouldClose) => listedHoldings == 0
        ? $"Dhan listed 0 holdings while {openHoldings} {(openHoldings == 1 ? "is" : "are")} open in xDrishti"
        : $"Dhan listed {listedHoldings} {(listedHoldings == 1 ? "holding" : "holdings")} but {wouldClose} of your {openHoldings} open holdings are missing";

    /// <summary>
    /// Dhan's answer would close all or most of the account's open holdings (an empty list, or more than half and at least 3): that is more likely a
    /// broken answer than a sale of everything, and closing then reopening would lose the owner's acquisition dates. Needs an explicit confirmation that
    /// echoes the counts (<c>openHoldings</c>, <c>wouldClose</c>) back.
    /// </summary>
    public static Error ConfirmationRequired(int listedHoldings, int openHoldings, int wouldClose) => new(
        ConfirmationRequiredCode,
        SuspiciousResultText(listedHoldings, openHoldings, wouldClose) + " — nothing was changed; press Sync now to confirm.",
        ErrorKind.Conflict,
        Data: Counts(listedHoldings, openHoldings, wouldClose));

    /// <summary>The confirmation does not fit the answer Dhan gives now (it changed since the owner looked): nothing was changed, the fresh counts are returned.</summary>
    public static Error ConfirmationStale(int listedHoldings, int openHoldings, int wouldClose) => new(
        ConfirmationStaleCode,
        $"Dhan's answer changed since you confirmed ({SuspiciousResultText(listedHoldings, openHoldings, wouldClose)}) — nothing was changed; check the numbers and confirm again.",
        ErrorKind.Conflict,
        Data: Counts(listedHoldings, openHoldings, wouldClose));

    private static Dictionary<string, int> Counts(int listedHoldings, int openHoldings, int wouldClose) =>
        new() { ["listedHoldings"] = listedHoldings, ["openHoldings"] = openHoldings, ["wouldClose"] = wouldClose };

    public static Error Invalid(FieldErrors fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        return fields.ToError(InvalidCode, "Some values are not valid.");
    }

    public static Error Invalid(string field, string message)
    {
        var fields = new FieldErrors();
        fields.Add(field, message);
        return Invalid(fields);
    }
}
