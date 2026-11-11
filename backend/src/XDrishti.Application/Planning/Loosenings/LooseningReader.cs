using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Domain.Planning;

namespace XDrishti.Application.Planning;

/// <summary>
/// Reads the loosenings the plan screen shows (BK-10b), on read and never stored: one query for every undismissed loosening, then the pure rule for each plan. A store that cannot be read gives an empty list and
/// <c>Readable = false</c>, so the screen says so instead of showing nothing (and the plan itself is still returned).
/// </summary>
internal sealed class LooseningReader(ILooseningStore store, ILogger<LooseningReader> logger)
{
    /// <summary>Every undismissed loosening, oldest first.</summary>
    public async ValueTask<(IReadOnlyList<LooseningNotice> Notices, bool Readable)> ReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            return (await store.ListUndismissedAsync(cancellationToken), true);
        }
#pragma warning disable CA1031 // a banner that cannot be read must say so; the plan is still returned
        catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
#pragma warning restore CA1031
        {
            LooseningLog.ReadFailed(logger, failure.GetType().Name);
            return ([], false);
        }
    }

    /// <summary>The plan view with the loosenings it shows.</summary>
    public static PlanView With(PlanView view, IReadOnlyList<LooseningNotice> notices, bool readable)
    {
        ArgumentNullException.ThrowIfNull(view);
        return view with { Loosenings = [.. LooseningNotices.Shown(notices, view.CreatedAt).Select(LooseningView.Of)], LooseningsUnavailable = !readable };
    }
}
