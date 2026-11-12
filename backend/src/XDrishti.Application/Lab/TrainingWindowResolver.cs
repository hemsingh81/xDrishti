using XDrishti.Domain.Common;
using XDrishti.Domain.Confidence;

namespace XDrishti.Application.Lab;

/// <summary>A training window that ends before the locked hold-out and its embargo.</summary>
/// <param name="From">First session of the window.</param>
/// <param name="To">Last session of the window; always before <see cref="HoldOutStart"/> by at least the embargo.</param>
/// <param name="HoldOutStart">The first session of the locked hold-out block the window stays clear of.</param>
public sealed record TrainingWindow(DateOnly From, DateOnly To, DateOnly HoldOutStart);

/// <summary>
/// The window of a training simulation (spec AU-11, ADR 0243): from the first stored session to the day before the hold-out block start less the embargo, by the same <see cref="LabHoldOutGuard"/> policy as every Lab run. Fail closed: with no stored history or no
/// determinable block there is no window. Unlike a Lab window the refusal does not depend on the owner's <c>Enforce</c> switch: a training run never reaches the hold-out, off or on. The simulator is given only this window, so no bar of the hold-out is read and the
/// results up to a day do not depend on any later day (no look-ahead).
/// </summary>
internal sealed class TrainingWindowResolver(LabHoldOutGuard guard, HoldOutOptions options)
{
    /// <summary>The window for the request: an omitted end is the latest allowed; an explicit end or start inside the hold-out or its embargo is refused.</summary>
    public async ValueTask<Result<TrainingWindow>> ResolveAsync(DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        if (await guard.HistoryAsync(cancellationToken) is not { } history)
        {
            return Result.Failure<TrainingWindow>(LabErrors.HoldOutUnavailable("No bars are stored, so no hold-out block can be determined; a training run does not start."));
        }

        if (await guard.EnsureAsync(history.First, history.Last, cancellationToken) is not { } block)
        {
            return Result.Failure<TrainingWindow>(LabErrors.HoldOutUnavailable("The stored history is too short to keep a hold-out block; a training run does not start."));
        }

        var latest = HoldOutOptions.TrainingCut(block.Start, options.TrainingEmbargoDays);
        var start = from ?? history.First;
        var end = to ?? latest;
        if (start > latest || end > latest)
        {
            return Result.Failure<TrainingWindow>(LabErrors.HoldOutTouched(block.Start, start, end));
        }

        return start > end
            ? Result.Failure<TrainingWindow>(LabErrors.InvalidInput("The training window starts after it ends."))
            : Result.Success(new TrainingWindow(start, end, block.Start));
    }
}
