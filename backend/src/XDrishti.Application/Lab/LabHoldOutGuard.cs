using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Domain.Common;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Lab;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Lab;

/// <summary>
/// Sizes and fixes the locked hold-out (spec CF-06, D4): the policy over the stored history, the block stored once at first use (it never moves earlier; a longer block is a new one set by an owner act, a follow-up), and the cut of a Lab window. It reads
/// the first and last stored session, never a bar inside the hold-out.
/// </summary>
internal sealed class LabHoldOutGuard(IHoldOutStore store, IBarQueries bars, HoldOutOptions options, TimeProvider time)
{
    /// <summary>Whether the Lab refuses an explicit window that reaches the hold-out and caps a verdict without a kept hold-out. Default windows are always cut.</summary>
    public bool Enforce => options.Enforce;

    /// <summary>The first and last stored session, or null when no bar is stored.</summary>
    public async ValueTask<(DateOnly First, DateOnly Last)?> HistoryAsync(CancellationToken cancellationToken)
    {
        var totals = await bars.GetBarTotalsAsync(time.GetUtcNow(), cancellationToken);
        return totals.FirstBar is { } first && totals.LastBar is { } last
            ? (SessionClock.DateOf(first.ToUnixTimeSeconds()), SessionClock.DateOf(last.ToUnixTimeSeconds()))
            : null;
    }

    /// <summary>The sizing for the history.</summary>
    public HoldOutPlan Plan(DateOnly first, DateOnly last) => HoldOutPolicy.FromDates(first, last, options, new SelectionOptions());

    /// <summary>The stored block, or the one the policy gives now, stored; null when the history is too short to keep any block.</summary>
    public async ValueTask<HoldOutBlock?> EnsureAsync(DateOnly first, DateOnly last, CancellationToken cancellationToken)
    {
        var current = await store.CurrentAsync(cancellationToken);
        if (current is not null)
        {
            return current;
        }

        var plan = Plan(first, last);
        if (plan.Start is not { } start)
        {
            return null;
        }

        // shortcut: two concurrent first posts may each insert a block; the store reads the earliest one back, so both runs then use the same block
        var block = new HoldOutBlock(start, first, last, plan.LengthMonths, options.SnapshotHash(), time.GetUtcNow());
        await store.InsertBlockAsync(block, cancellationToken);
        return await store.CurrentAsync(cancellationToken) ?? block;
    }

    /// <summary>The window of a run against the block: the (possibly shortened) end, a note when it was shortened, or the refusal (spec CF-06, AC8).</summary>
    public static Result<(DateOnly To, string? Note)> Cut(HoldOutBlock block, DateOnly from, DateOnly to, bool explicitEnd, bool enforce)
    {
        ArgumentNullException.ThrowIfNull(block);
        if (explicitEnd)
        {
            // an explicit window is the owner's own; it is refused only under enforcement (otherwise the report carries the warning)
            return enforce && (from >= block.Start || to >= block.Start)
                ? Result.Failure<(DateOnly, string?)>(LabErrors.HoldOutTouched(block.Start, from, to))
                : Result.Success<(DateOnly, string?)>((to, null));
        }

        if (from >= block.Start)
        {
            return Result.Failure<(DateOnly, string?)>(LabErrors.HoldOutTouched(block.Start, from, to));
        }

        return to >= block.Start
            ? Result.Success<(DateOnly, string?)>((block.Start.AddDays(-1), $"The window was cut to end on {block.Start.AddDays(-1):yyyy-MM-dd}: the sessions from {block.Start:yyyy-MM-dd} are the locked hold-out and no Lab run reads them."))
            : Result.Success<(DateOnly, string?)>((to, null));
    }
}
