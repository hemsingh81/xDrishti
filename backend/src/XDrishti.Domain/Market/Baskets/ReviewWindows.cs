namespace XDrishti.Domain.Market.Baskets;

/// <summary>The windows around the quarterly index reviews in which the change bound is lifted: from <c>fromDay</c> of a review month to <c>toDay</c> of the next month (default 20th to 7th, months 3, 6, 9, 12).</summary>
public static class ReviewWindows
{
    public static bool Contains(DateOnly date, IReadOnlyCollection<int> months, int fromDay, int toDay)
    {
        ArgumentNullException.ThrowIfNull(months);
        var previous = date.Month == 1 ? 12 : date.Month - 1;
        return (months.Contains(date.Month) && date.Day >= fromDay) || (months.Contains(previous) && date.Day <= toDay);
    }
}
