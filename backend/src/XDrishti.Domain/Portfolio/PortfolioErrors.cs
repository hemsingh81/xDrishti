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
    public const string LotFromTradeCode = "portfolio.lot_from_trade";
    public const string PreviewStaleCode = "portfolio.preview_stale";
    public const string TokenExpiredCode = "portfolio.token_expired";
    public const string HistoryTooLargeCode = "portfolio.history_too_large";
    public const string PossibleDuplicateCode = "portfolio.possible_duplicate";
    public const string WrongFileKindCode = "portfolio.wrong_file_kind";

    /// <summary>The most active price alerts there can be (ADR 0022).</summary>
    public const int MaxActiveAlerts = 200;

    public static readonly Error HoldingNotFound = Error.NotFound(NotFoundCode, "The holding does not exist.");

    public static readonly Error BenchmarkNotFound = Error.NotFound(NotFoundCode, "The benchmark index is not in the instrument master. Refresh the instrument list or choose another index.");

    public static readonly Error ActionNotFound = Error.NotFound(NotFoundCode, "That item is not on the Action center now. Reload the list.");

    public static readonly Error AlertNotFound = Error.NotFound(NotFoundCode, "The price alert does not exist.");

    public static readonly Error AlertLimit = Error.Conflict(AlertLimitCode, "There are 200 active price alerts, the most there can be. Cancel one first.");

    public static readonly Error ActionChanged = Error.Conflict(ConflictCode, "The decision was changed meanwhile. Reload and try again.");

    public static readonly Error AccountNotFound = Error.NotFound(NotFoundCode, "The account does not exist or has no Portfolio role.");

    public static readonly Error TradeNotFound = Error.NotFound(NotFoundCode, "The trade does not exist.");

    public static readonly Error DividendNotFound = Error.NotFound(NotFoundCode, "The dividend does not exist.");

    public static readonly Error SuggestionNotFound = Error.NotFound(NotFoundCode, "That suggested dividend does not exist (any more).");

    /// <summary>The same account, holding, pay date and amount is entered already: two genuine payments are possible, so the owner confirms (<c>confirm: true</c>) to add the second.</summary>
    public static readonly Error PossibleDuplicate = Error.Conflict(
        PossibleDuplicateCode, "A dividend of the same amount on the same date is entered already for this holding. If it is a second payment, add it again with confirm.");

    /// <summary>The suggestion was accepted or dismissed meanwhile.</summary>
    public static readonly Error SuggestionDecided = Error.Conflict(ConflictCode, "That suggested dividend was accepted or dismissed meanwhile. Reload the list.");

    public static readonly Error TradeBatchNotFound = Error.NotFound(NotFoundCode, "The import does not exist.");

    public static readonly Error DividendBatchNotFound = Error.NotFound(NotFoundCode, "The dividend import does not exist.");

    /// <summary>
    /// The file is a different kind of file than the screen expects (ADR 0028): <c>detected</c> (an extension member of the problem) says which kind the header looks like — <c>dividends</c>, <c>trades</c>, or <c>tradebook_without_sides</c>
    /// (a tradebook that cannot be read because it has no buy/sell column). A 400, so the screen can send the owner to the right import.
    /// </summary>
    public static Error WrongFileKind(string detected, string message) => new(
        WrongFileKindCode,
        message,
        ErrorKind.Validation,
        Payload: new Dictionary<string, object> { ["detected"] = detected });

    public static readonly Error LotFromTrade = Error.Conflict(
        LotFromTradeCode, "This lot was rebuilt from your trades, so its date is the trade's. Void or add a trade, then rebuild the lots, to change it.");

    public static readonly Error LotNotFound = Error.NotFound(NotFoundCode, "The lot does not exist.");

    public static readonly Error NotConnected = Error.Conflict(
        NotConnectedCode, "The account is not connected to Dhan. Sign in to Dhan again (Accounts, Connection) and retry.");

    public static readonly Error TokenRefused = Error.Conflict(
        NotConnectedCode, "Dhan refused the saved access token. Sign in to Dhan again (Accounts, Connection) and retry.");

    /// <summary>The account's Dhan access token ran out (they last a day): the scheduled trade and ledger read reports it as <c>token_expired</c> and the owner signs in again.</summary>
    public static readonly Error TokenExpired = Error.Conflict(
        TokenExpiredCode, "The Dhan access token has expired. Sign in to Dhan again (Accounts, Connection) and retry.");

    /// <summary>Only an automatic first import gets this; it never reaches the API.</summary>
    public static readonly Error Settling = Error.Conflict(SettlingCode, "The account's settings changed a moment ago; the automatic first import waits.");

    public static readonly Error SyncRunning = Error.Conflict(SyncRunningCode, "A sync of this account (holdings, trades or ledger) is already running.");

    public static readonly Error RoleOff = Error.Conflict(RoleOffCode, "Switch on the Portfolio role of this account first.");

    public static readonly Error NoBucket = Error.Conflict(NoBucketCode, "The account has no portfolio bucket to put holdings in. Switch the Portfolio role off and on again.");

    public static readonly Error BenchmarkChanged = Error.Conflict(ConflictCode, "The benchmark was changed meanwhile. Reload and try again.");

    /// <summary>A holding changed (a sync or a quote saved it) between the automatic rebuild reading it and saving; the rebuild is skipped and the next run retries (ADR 0025).</summary>
    public static readonly Error HoldingChanged = Error.Conflict(ConflictCode, "The holding was changed meanwhile. The next run tries again.");

    /// <summary>A lot edit that collided with a change of the lot or of its holding (a close, a sync, a quote, a rebuild), also after the one repeat.</summary>
    public static readonly Error LotChanged = Error.Conflict(ConflictCode, "The holding changed while you edited; reload and try again");

    /// <summary>Dhan answered more pages for one window than the 200 a window may have, even after the window was halved: nothing of it is stored and the watermark stays (ADR 0025).</summary>
    public static readonly Error HistoryTooLarge = Error.Upstream(HistoryTooLargeCode, "Dhan returned more trades for one stretch of dates than can be read in one go. Nothing was stored for it and the next run reads it again; if it keeps failing, the size of the date windows has to be made smaller (a setting of the application).");

    /// <summary>
    /// The database refused to save a window's rows in one go (the same trades or entries were stored meanwhile by a tradebook import, or a row broke a rule of the table). Internal to the broker history run: the window is then saved row by row
    /// (a row that is stored already counts as a duplicate, a row the database refuses is dropped and counted), so one bad row never keeps a window - and every later one - from being read.
    /// </summary>
    public static readonly Error HistoryChanged = Error.Conflict(ConflictCode, "The trades or ledger entries were changed while the window was saved. The next run reads the window again.");

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

    /// <summary>The start date of a trades or ledger read leaves a hole after the dates already read (the watermark): the dates in between would never be read.</summary>
    public static Error HistoryStartTooLate(DateOnly latestStart) =>
        Invalid("from", string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Start on or before {latestStart:yyyy-MM-dd}: dates in between were never read."));

    public static Error Invalid(string field, string message)
    {
        var fields = new FieldErrors();
        fields.Add(field, message);
        return Invalid(fields);
    }
}
