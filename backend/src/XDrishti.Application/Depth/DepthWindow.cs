using XDrishti.Domain.Market;

namespace XDrishti.Application.Depth;

/// <summary>The part of the day that is stored (P7-16 decision 9): 09:15:00 up to but not including 15:30:00 India time. The pre-open and closing call auctions are not recorded; the service window (09:14 to 15:31) is wider so that no second is lost. Pure.</summary>
internal static class DepthWindow
{
    public static readonly TimeOnly Open = new(9, 15);

    public static readonly TimeOnly Close = new(15, 30);

    /// <summary>True when the instant lies in [09:15:00, 15:30:00) India time.</summary>
    public static bool Contains(DateTimeOffset instant)
    {
        var time = TimeOnly.FromTimeSpan(IndiaTime.ToIst(instant).TimeOfDay);
        return time >= Open && time < Close;
    }
}
