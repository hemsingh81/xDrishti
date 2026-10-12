namespace XDrishti.Domain.Market.Calendar;

/// <summary>
/// An exception to "every weekday has the regular session" (ADR 0017 §1): a weekday without a session (<see cref="Holiday"/>) or a date with a session
/// of its own, such as a Saturday trading session or the Muhurat evening (<see cref="Special"/>).
/// </summary>
public enum CalendarDayKind
{
    Holiday,
    Special,
}

/// <summary>The codes of <see cref="CalendarDayKind"/> used in the database and the API: <c>holiday</c> · <c>special</c>.</summary>
public static class CalendarDayKindCodes
{
    public const string Holiday = "holiday";
    public const string Special = "special";

    public static string ToCode(this CalendarDayKind kind) => kind switch
    {
        CalendarDayKind.Holiday => Holiday,
        CalendarDayKind.Special => Special,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown calendar day kind."),
    };

    /// <summary>Exact, lower-case codes only (the API contract).</summary>
    public static bool TryParse(string? code, out CalendarDayKind kind)
    {
        switch (code)
        {
            case Holiday:
                kind = CalendarDayKind.Holiday;
                return true;
            case Special:
                kind = CalendarDayKind.Special;
                return true;
            default:
                kind = default;
                return false;
        }
    }
}
