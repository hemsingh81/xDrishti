using XDrishti.Domain.Common;

namespace XDrishti.Domain.Portfolio;

/// <summary>Rules for a dividend the owner enters (ADR 0026). Pure.</summary>
public static class DividendRules
{
    /// <summary>The highest amount of one dividend accepted (rupees).</summary>
    public const decimal MaxAmount = 1_000_000_000m;

    /// <summary>The highest dividend per share accepted (rupees).</summary>
    public const decimal MaxPerShare = 10_000_000m;

    /// <summary>Amounts are stored with this many decimals (paise); more are rounded away from zero.</summary>
    public const int AmountDecimals = 2;

    /// <summary>The per-share amount is stored with this many decimals.</summary>
    public const int PerShareDecimals = 4;

    public const int MaxNoteLength = 200;

    /// <summary>True when an amount (a ledger credit, say) can be entered as a dividend as it is: above 0 and at most <see cref="MaxAmount"/> once rounded to paise.</summary>
    public static bool CanEnter(decimal amount) => AmountProblem(RoundAmount(amount)) is null;

    public static decimal RoundAmount(decimal amount) => Math.Round(amount, AmountDecimals, MidpointRounding.AwayFromZero);

    public static decimal RoundPerShare(decimal perShare) => Math.Round(perShare, PerShareDecimals, MidpointRounding.AwayFromZero);

    public static string? AmountProblem(decimal amount) => amount switch
    {
        <= 0m => "Must be above 0.",
        > MaxAmount => $"At most {MaxAmount:N0}.",
        _ => null,
    };

    public static string? PerShareProblem(decimal perShare) => perShare switch
    {
        <= 0m => "Must be above 0.",
        > MaxPerShare => $"At most {MaxPerShare:N0}.",
        _ => null,
    };

    /// <summary>Adds a field error unless the pay date is between <see cref="TradeRules.EarliestDate"/> and <paramref name="today"/> (India).</summary>
    public static void CheckPayDate(FieldErrors errors, string field, DateOnly date, DateOnly today) => TradeRules.CheckDate(errors, field, date, today);

    /// <summary>The note as stored: control characters (line breaks, tabs) turned into spaces, trimmed; null when empty. Longer than <see cref="MaxNoteLength"/> is a field error for the caller, never cut silently.</summary>
    public static string? CleanNote(string? note)
    {
        var clean = LedgerEntry.Clean(note, int.MaxValue);
        return clean.Length == 0 ? null : clean;
    }
}
