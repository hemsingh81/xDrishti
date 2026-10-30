namespace XDrishti.Domain.Execution.Confirm;

/// <summary>The problem codes of the confirm ticket (spec P12-08 Contract). Each names one cause; nothing is sent in any of them.</summary>
public static class ConfirmCodes
{
    public const string TicketExpired = "execution.ticket_expired";
    public const string TextChanged = "execution.text_changed";
    public const string PriceMoved = "execution.price_moved";
    public const string PriceUnavailable = "execution.price_unavailable";
    public const string NotUnlocked = "execution.not_unlocked";
    public const string ModeChanged = "execution.mode_changed";
    public const string Halted = "execution.halted";
    public const string NotReady = "execution.not_ready";
    public const string Disabled = "execution.disabled";
    public const string TicketClosed = "execution.ticket_closed";
    public const string TooManyOpen = "execution.too_many_open";
    public const string OutsidePlan = "execution.outside_plan";
    public const string SoftSetChanged = "execution.soft_set_changed";
    public const string PasswordRequired = "execution.password_required";
    public const string ReasonRequired = "execution.reason_required";
    public const string NotFound = "execution.not_found";
    public const string ValuesInvalid = "execution.values_invalid";
    public const string OwnerOnly = "execution.owner_only";
    public const string NotTradingDay = "execution.not_trading_day";
    public const string ModeNotExecuting = "execution.mode_not_executing";
    public const string UnlockClosed = "execution.unlock_closed";

    /// <summary>The short reason stored on an outcome row for a code (the part after <c>execution.</c>).</summary>
    public static string Reason(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        return code.StartsWith("execution.", StringComparison.Ordinal) ? code["execution.".Length..] : code;
    }
}
