namespace XDrishti.Application.Portfolio.BrokerHistory;

/// <summary>A run of whole India dates, <see cref="From"/> to <see cref="To"/> inclusive, that is read from Dhan in one request (trades: one request per page).</summary>
internal readonly record struct DateWindow(DateOnly From, DateOnly To)
{
    /// <summary>How many dates the window covers.</summary>
    public int Days => To.DayNumber - From.DayNumber + 1;
}
