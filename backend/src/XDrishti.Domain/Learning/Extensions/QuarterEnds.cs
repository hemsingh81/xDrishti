namespace XDrishti.Domain.Learning.Extensions;

/// <summary>Quarter arithmetic on period ends (P15-05, pure): the period end <c>n</c> quarters before another, as the last day of the month three months apart each time.</summary>
public static class QuarterEnds
{
    public static DateOnly Back(DateOnly periodEnd, int quarters)
    {
        if (quarters == 0)
        {
            return periodEnd;
        }

        var first = new DateOnly(periodEnd.Year, periodEnd.Month, 1).AddMonths(-3 * quarters);
        return new DateOnly(first.Year, first.Month, DateTime.DaysInMonth(first.Year, first.Month));
    }
}
