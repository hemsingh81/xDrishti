namespace XDrishti.Domain.Market.Calendar;

/// <summary>
/// Where a calendar day came from, in order of trust (ADR 0017 §1): the owner (<see cref="Manual"/>), what the stored bars show (<see cref="Observed"/>, only ever
/// proposed) and NSE's public holiday list (<see cref="Nse"/>, best effort).
/// </summary>
public enum CalendarSource
{
    Manual,
    Observed,
    Nse,
}

/// <summary>The codes of <see cref="CalendarSource"/> used in the database and the API: <c>manual</c> · <c>observed</c> · <c>nse</c>.</summary>
public static class CalendarSourceCodes
{
    public const string Manual = "manual";
    public const string Observed = "observed";
    public const string Nse = "nse";

    public static string ToCode(this CalendarSource source) => source switch
    {
        CalendarSource.Manual => Manual,
        CalendarSource.Observed => Observed,
        CalendarSource.Nse => Nse,
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Unknown calendar source."),
    };
}
