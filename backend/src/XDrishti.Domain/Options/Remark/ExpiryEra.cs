namespace XDrishti.Domain.Options.Remark;

/// <summary>One era of the derived expiry weekday table (UNVERIFIED data, ADR 0197): the expiry falls on <see cref="Day"/> for nominal dates from <see cref="From"/> to <see cref="To"/> inclusive (null: open).</summary>
public sealed record ExpiryEra(DateOnly? From, DateOnly? To, DayOfWeek Day)
{
    public bool Covers(DateOnly nominal) => (From is null || nominal >= From) && (To is null || nominal <= To);
}
