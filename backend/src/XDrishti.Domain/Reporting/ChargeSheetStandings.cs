namespace XDrishti.Domain.Reporting;

/// <summary>One stored row of the charge-sheet confirmation log (spec P8-23): an act, who did it and when (the database's clock).</summary>
/// <param name="Id">The row; the highest id is the newest.</param>
/// <param name="Kind">The act.</param>
/// <param name="SheetId">The sheet of a confirmation or of the revoke that withdrew it; null for a capability change.</param>
/// <param name="RatesSha256">The rates hash of that sheet as confirmed; null for a capability change.</param>
/// <param name="ConfirmedOn">India date the database stamped on a confirmation; null for the other acts.</param>
/// <param name="Actor">The owner, <c>owner:...</c>.</param>
/// <param name="Reason">Why.</param>
/// <param name="At">When.</param>
public sealed record ChargeSheetEntry(long Id, ChargeSheetActKind Kind, string? SheetId, string? RatesSha256, DateOnly? ConfirmedOn, string Actor, string Reason, DateTimeOffset At);

/// <summary>What stands about the confirmation now.</summary>
public enum ChargeSheetState
{
    /// <summary>The owner never confirmed a sheet.</summary>
    NotConfirmed,

    /// <summary>The newest act is a confirmation and the stored sheet still has the confirmed rates.</summary>
    Confirmed,

    /// <summary>The newest act is a confirmation, but the stored sheet's rates are no longer the confirmed ones.</summary>
    Stale,

    /// <summary>The newest act is a confirmation, but no sheet of that name is stored any more.</summary>
    SheetMissing,

    /// <summary>The newest act is a revoke.</summary>
    Revoked,
}

/// <summary>
/// The one rule that says whether a confirmation stands (spec P8-23, ADR 0127, ADR 0100 decision 6): only when the newest act on the confirmation is a confirmation AND the stored sheet it names still has exactly the rates hash that was confirmed. The store overwrites a sheet in place, so
/// a confirmation of a name would silently cover other rates; a changed hash makes it stale, never still valid.
/// </summary>
public static class ChargeSheetStandings
{
    /// <summary>Evaluates the newest confirm-or-revoke row against the rates hash of the sheet it names (null when no such sheet is stored).</summary>
    public static ChargeSheetState Evaluate(ChargeSheetEntry? newestAct, string? storedRatesSha256)
    {
        if (newestAct is null)
        {
            return ChargeSheetState.NotConfirmed;
        }

        if (newestAct.Kind == ChargeSheetActKind.Revoke)
        {
            return ChargeSheetState.Revoked;
        }

        if (newestAct.Kind != ChargeSheetActKind.Confirm || newestAct.ConfirmedOn is null || newestAct.RatesSha256 is null)
        {
            return ChargeSheetState.NotConfirmed; // not a confirmation: never read as one
        }

        if (storedRatesSha256 is null)
        {
            return ChargeSheetState.SheetMissing;
        }

        return string.Equals(storedRatesSha256, newestAct.RatesSha256, StringComparison.Ordinal) ? ChargeSheetState.Confirmed : ChargeSheetState.Stale;
    }

    /// <summary>The words for the state, for the screen and the verb.</summary>
    public static string Word(ChargeSheetState state) => state switch
    {
        ChargeSheetState.NotConfirmed => "not_confirmed",
        ChargeSheetState.Confirmed => "confirmed",
        ChargeSheetState.Stale => "stale",
        ChargeSheetState.SheetMissing => "sheet_missing",
        ChargeSheetState.Revoked => "revoked",
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };
}
