namespace XDrishti.Domain.Portfolio;

/// <summary>The codes of <see cref="LedgerClass"/> as they are stored and sent over the API.</summary>
public static class LedgerClasses
{
    public const string FundsIn = "funds_in";

    public const string FundsOut = "funds_out";

    public const string Dividend = "dividend";

    public const string Charges = "charges";

    public const string Other = "other";

    public static string Code(LedgerClass value) => value switch
    {
        LedgerClass.FundsIn => FundsIn,
        LedgerClass.FundsOut => FundsOut,
        LedgerClass.Dividend => Dividend,
        LedgerClass.Charges => Charges,
        _ => Other,
    };

    /// <summary>The class of a code; false for anything that is not one of the five.</summary>
    public static bool TryParse(string? code, out LedgerClass value)
    {
        switch (code)
        {
            case FundsIn:
                value = LedgerClass.FundsIn;
                return true;
            case FundsOut:
                value = LedgerClass.FundsOut;
                return true;
            case Dividend:
                value = LedgerClass.Dividend;
                return true;
            case Charges:
                value = LedgerClass.Charges;
                return true;
            case Other:
                value = LedgerClass.Other;
                return true;
            default:
                value = default;
                return false;
        }
    }

    /// <summary>All classes, in the order they are listed.</summary>
    public static IReadOnlyList<LedgerClass> All { get; } = [LedgerClass.FundsIn, LedgerClass.FundsOut, LedgerClass.Dividend, LedgerClass.Charges, LedgerClass.Other];
}
