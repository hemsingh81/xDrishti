using XDrishti.Application.Abstractions.Depth;
using XDrishti.Application.Abstractions.Ticks;
using XDrishti.Application.Depth;
using XDrishti.Domain.Market;

namespace XDrishti.Application.Ticks;

/// <summary>What the tick recorder does about the day's set at a look.</summary>
internal enum TickSetAction
{
    /// <summary>Copy the depth recorder's stored set (source <c>depth_set</c>).</summary>
    UseDepthSet,

    /// <summary>Before 09:14:30 with no depth set stored yet: wait for the depth recorder's first look.</summary>
    Wait,

    /// <summary>No depth set by 09:14:30: build one the same way (source <c>built</c>).</summary>
    Build,
}

/// <summary>
/// The rule of the day's tick set (P7-16b criterion 5, decision 7) - pure: the depth recorder's stored set when it is there by the first look at or after 09:14:30 India time, otherwise one built with <see cref="DepthRecordingSet.Build"/> and the same
/// <c>Depth:*</c> inputs. Either way it is copied into the tick recorder's own table and fixed for the day; the tick recorder never writes a depth table.
/// </summary>
internal static class TickSetChoice
{
    /// <summary>The state word while it waits for the depth set.</summary>
    public const string WaitingForSet = "waiting_set";

    /// <summary>The first look that may decide without the depth set.</summary>
    public static readonly TimeOnly DecideAt = new(9, 14, 30);

    public static TickSetAction Decide(DateTimeOffset now, bool depthSetStored)
    {
        if (depthSetStored)
        {
            return TickSetAction.UseDepthSet;
        }

        return TimeOnly.FromTimeSpan(IndiaTime.ToIst(now).TimeOfDay) < DecideAt ? TickSetAction.Wait : TickSetAction.Build;
    }

    /// <summary>A built depth-style set as tick entries (source <c>built</c>).</summary>
    public static IReadOnlyList<TickSetEntry> FromBuilt(IReadOnlyList<DepthSetEntry> built) =>
        [.. built.Select(e => new TickSetEntry(e.InstrumentId, e.SecurityId, e.Symbol, TickSources.Built, e.Reason, e.Rank))];
}
