using XDrishti.Domain.Checklist;
using XDrishti.Domain.Common;
using XDrishti.Domain.Research;

namespace XDrishti.Domain.Reporting;

/// <summary>What an owner act on the charge-sheet confirmation is (spec P8-23, ADR 0127). The stored word is the one in <see cref="ChargeSheetActKinds"/>.</summary>
public enum ChargeSheetActKind
{
    /// <summary>The capability to confirm is switched on. Default is OFF: no row means off.</summary>
    GateOn,

    /// <summary>The capability to confirm is switched off.</summary>
    GateOff,

    /// <summary>The owner confirmed one stored sheet, at the hash of its rates at that time.</summary>
    Confirm,

    /// <summary>The owner withdrew the confirmation in force.</summary>
    Revoke,
}

/// <summary>The words the database stores for <see cref="ChargeSheetActKind"/>.</summary>
public static class ChargeSheetActKinds
{
    public static string Word(ChargeSheetActKind kind) => kind switch
    {
        ChargeSheetActKind.GateOn => "gate_on",
        ChargeSheetActKind.GateOff => "gate_off",
        ChargeSheetActKind.Confirm => "confirm",
        ChargeSheetActKind.Revoke => "revoke",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>The kind for a stored word, or null for a word that is none of the four.</summary>
    public static ChargeSheetActKind? Parse(string? word) => word switch
    {
        "gate_on" => ChargeSheetActKind.GateOn,
        "gate_off" => ChargeSheetActKind.GateOff,
        "confirm" => ChargeSheetActKind.Confirm,
        "revoke" => ChargeSheetActKind.Revoke,
        _ => null,
    };

    /// <summary>Whether the kind is about the confirmation itself (confirm or revoke) rather than the capability.</summary>
    public static bool IsAboutConfirmation(ChargeSheetActKind kind) => kind is ChargeSheetActKind.Confirm or ChargeSheetActKind.Revoke;
}

/// <summary>
/// An owner act on the charge-sheet confirmation (spec P8-23, ADR 0127): the only way a row comes to be. It needs an owner actor (taken from the authenticated session by the caller, never from a request) and a reason. A confirmation names ONE stored sheet and the
/// SHA-256 of its rates as the owner saw them; a revoke and a capability change name neither (a revoke takes them from the confirmation it withdraws). The type holds no flag that means "confirmed": what stands is read from the rows (<see cref="ChargeSheetStandings"/>).
/// </summary>
public sealed record ChargeSheetAct
{
    /// <summary>The longest reason (the column is 500).</summary>
    public const int MaxReasonLength = 500;

    private ChargeSheetAct(ChargeSheetActKind kind, string actor, string reason, string? sheetId, string? ratesSha256)
    {
        Kind = kind;
        Actor = actor;
        Reason = reason;
        SheetId = sheetId;
        RatesSha256 = ratesSha256;
    }

    public ChargeSheetActKind Kind { get; }

    /// <summary>The owner, <c>owner:...</c>.</summary>
    public string Actor { get; }

    /// <summary>Why, trimmed.</summary>
    public string Reason { get; }

    /// <summary>The confirmed sheet; null unless the act is a confirmation.</summary>
    public string? SheetId { get; }

    /// <summary>The rates hash the owner saw: 64 lower-case hex; null unless the act is a confirmation.</summary>
    public string? RatesSha256 { get; }

    /// <summary>Makes the act, or says why not: an owner actor, a reason that fits and, for a confirmation, a storable sheet id and a rates hash.</summary>
    public static Result<ChargeSheetAct> Create(ChargeSheetActKind kind, string? actor, string? reason, string? sheetId = null, string? ratesSha256 = null)
    {
        if (!OwnerConfirmation.IsOwnerActor(actor) || actor!.Length > 100)
        {
            return Result.Failure<ChargeSheetAct>(Error.Forbidden("chargesheet.owner_only", "Only the signed-in owner confirms or revokes a charge sheet."));
        }

        var text = reason?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > MaxReasonLength)
        {
            return Result.Failure<ChargeSheetAct>(Error.Validation("chargesheet.reason_invalid", "An act needs a reason of 1 to 500 characters."));
        }

        if (kind != ChargeSheetActKind.Confirm)
        {
            return Result.Success(new ChargeSheetAct(kind, actor, text, null, null));
        }

        if (sheetId is null || !CostSheet.IsValidId(sheetId) || sheetId.StartsWith(CostSheet.TestPrefix, StringComparison.Ordinal))
        {
            return Result.Failure<ChargeSheetAct>(Error.Validation("chargesheet.sheet_invalid", "Name one stored charge sheet (lower case letters, digits, '.', '_' or '-'; a test- sheet cannot be confirmed)."));
        }

        if (ratesSha256 is null || ratesSha256.Length != 64 || !ratesSha256.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f')))
        {
            return Result.Failure<ChargeSheetAct>(Error.Validation("chargesheet.rates_invalid", "Give the SHA-256 of the sheet's rates as shown (64 lower-case hex characters)."));
        }

        return Result.Success(new ChargeSheetAct(kind, actor, text, sheetId, ratesSha256));
    }
}
