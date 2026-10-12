using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Market.Bars.Gaps;

/// <summary>A gap of the plan: its record, the bars the day had before the fetch and whether this fetch asks for the day (<see cref="Refetch"/>).</summary>
internal sealed record GapItem(BarGap Gap, int FoundBefore, bool Refetch);

/// <summary>A session inside the ordinary fetch window that is incomplete before the fetch: it becomes a gap only if the fetch does not complete it.</summary>
internal readonly record struct DeferredDay(DateOnly Date, int Expected, int Found);

/// <summary>What the Dhan phase does about the missing bars of one instrument: extra windows to fetch, and the days to check again afterwards.</summary>
internal sealed record GapPlan(IReadOnlyList<FetchWindow> Windows, IReadOnlyList<GapItem> Items, IReadOnlyList<DeferredDay> Deferred)
{
    public static GapPlan None { get; } = new([], [], []);
}

/// <summary>What the fetch did to the gaps of one instrument. <c>Found</c> counts the sessions this fetch asked for (known gaps and new ones), <c>StillOpen</c> those that stay open.</summary>
internal readonly record struct GapOutcome(int Found, int Filled, int Unfillable, int StillOpen)
{
    public static GapOutcome None => default;
}
