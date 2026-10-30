using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Domain.Execution.Limits;

/// <summary>What a proposed sheet does to the risk against the sheet in force: <see cref="Loosened"/> names every field that lets more through. Any loosened field makes the whole change a raise (a mixed change counts as a raise).</summary>
public sealed record LimitChangeVerdict(IReadOnlyList<string> Loosened)
{
    public bool IsRaise => Loosened.Count > 0;
}

/// <summary>
/// The risk classification of a limits change (spec P12-09 criterion 9). A cap that goes up, a window that starts earlier or ends later, a symbol or product added to a list, and <c>exitAllOnLossHalt</c> switched OFF (the halt then keeps
/// positions instead of closing them) loosen. An unset value allows nothing, so setting one is a raise and unsetting one is a lowering. With no sheet in force every value is compared against the empty sheet. The database judges the same rule
/// again (trigger <c>execution.check_limits</c>). Pure.
/// </summary>
public static class LimitChange
{
    /// <summary>A raise takes effect at this India time of the first trading day after one full closed session.</summary>
    public static readonly TimeOnly EffectiveAt = new(9, 0);

    public static LimitChangeVerdict Classify(ExecutionLimits? inForce, ExecutionLimits proposed)
    {
        ArgumentNullException.ThrowIfNull(proposed);
        var current = inForce ?? ExecutionLimits.Empty;
        var loosened = new List<string>();
        Up(loosened, ExecutionLimits.FieldOrders, current.MaxOrdersPerDay, proposed.MaxOrdersPerDay);
        Up(loosened, ExecutionLimits.FieldNotionalOrder, current.MaxNotionalPerOrder, proposed.MaxNotionalPerOrder);
        Up(loosened, ExecutionLimits.FieldNotionalDay, current.MaxNotionalPerDay, proposed.MaxNotionalPerDay);
        Up(loosened, ExecutionLimits.FieldLoss, current.MaxLossPerDay, proposed.MaxLossPerDay);
        Up(loosened, ExecutionLimits.FieldPositions, current.MaxOpenPositions, proposed.MaxOpenPositions);
        if (proposed.AllowList.Except(current.AllowList, StringComparer.Ordinal).Any())
        {
            loosened.Add(ExecutionLimits.FieldAllowList);
        }

        if (proposed.WindowStart is { } start && proposed.WindowEnd is { } end
            && (current.WindowStart is not { } oldStart || current.WindowEnd is not { } oldEnd || start < oldStart || end > oldEnd))
        {
            loosened.Add(ExecutionLimits.FieldWindow);
        }

        if (proposed.Products.Except(current.Products, StringComparer.Ordinal).Any())
        {
            loosened.Add(ExecutionLimits.FieldProducts);
        }

        if (current.ExitAllOnLossHalt && !proposed.ExitAllOnLossHalt)
        {
            loosened.Add("exitAllOnLossHalt");
        }

        return new LimitChangeVerdict(loosened);
    }

    /// <summary>
    /// When a raise made at <paramref name="now"/> takes effect: 09:00 India time of the next trading day after the first session that lies wholly after <paramref name="now"/> (one full closed session). A change at 08:59 before a Monday
    /// session takes effect on Tuesday at 09:00; a change during Monday's session or after its close takes effect on Wednesday at 09:00.
    /// </summary>
    public static DateTimeOffset EffectiveFrom(DateTimeOffset now, TradingCalendar calendar)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        var day = IndiaTime.Today(now);
        for (var guard = 0; guard < 400; guard++, day = day.AddDays(1))
        {
            if (calendar.SessionOf(day) is { } session && At(day, TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(session.FromMinute))) > now)
            {
                return At(Next(day, calendar), EffectiveAt);
            }
        }

        throw new InvalidOperationException("The calendar has no trading day in the next 400 days.");
    }

    private static DateOnly Next(DateOnly after, TradingCalendar calendar)
    {
        var day = after;
        for (var guard = 0; guard < 400; guard++)
        {
            day = day.AddDays(1);
            if (calendar.IsTradingDay(day))
            {
                return day;
            }
        }

        throw new InvalidOperationException("The calendar has no trading day in the next 400 days.");
    }

    private static DateTimeOffset At(DateOnly day, TimeOnly time) => new(day.ToDateTime(time), IndiaTime.Offset);

    private static void Up<T>(List<string> loosened, string field, T? current, T? proposed)
        where T : struct, IComparable<T>
    {
        if (proposed is { } p && (current is not { } c || p.CompareTo(c) > 0))
        {
            loosened.Add(field);
        }
    }
}
