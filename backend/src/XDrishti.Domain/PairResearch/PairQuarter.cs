using System.Globalization;
using XDrishti.Domain.Learning.Extensions;

namespace XDrishti.Domain.PairResearch;

/// <summary>The calendar quarters of protocol S (P14-13): January, April, July and October starts, cut by the same unit code as the other folds (<see cref="CalendarFolds"/>, quarter unit). One quarter is one trading window and one fold.</summary>
public static class PairQuarter
{
    /// <summary>The first day of the quarter that holds the date.</summary>
    public static DateOnly StartOf(DateOnly date) => new(date.Year, ((date.Month - 1) / 3 * 3) + 1, 1);

    /// <summary>The last day of the quarter that starts on <paramref name="start"/>.</summary>
    public static DateOnly EndOf(DateOnly start) => CalendarFolds.EndOf(start, FoldUnit.Quarter);

    /// <summary>The label, <c>2025Q3</c>.</summary>
    public static string Label(DateOnly start) => string.Create(CultureInfo.InvariantCulture, $"{start.Year}Q{((start.Month - 1) / 3) + 1}");

    /// <summary>The quarter starts of every quarter that has a day from <paramref name="from"/> to <paramref name="to"/>, ascending.</summary>
    public static IReadOnlyList<DateOnly> Between(DateOnly from, DateOnly to)
    {
        var list = new List<DateOnly>();
        if (from > to)
        {
            return list;
        }

        for (var start = StartOf(from); start <= to; start = start.AddMonths(3))
        {
            list.Add(start);
        }

        return list;
    }
}
