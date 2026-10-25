using XDrishti.Application.Abstractions.Paper;
using XDrishti.Domain.Market;

namespace XDrishti.Application.Paper;

/// <summary>The default gate (spec P10-01): one epoch of kind <c>first</c>, started on the day it is first needed, and every session measured. Forward only: nothing before that day is back-filled.</summary>
internal sealed class SingleEpochGate(IPaperStore store, TimeProvider time) : IEpochGate
{
    public async ValueTask<EpochResolution> ResolveAsync(IReadOnlyList<DateOnly> sessions, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        var now = time.GetUtcNow();
        var epoch = await store.EnsureFirstEpochAsync(IndiaTime.Today(now), now, cancellationToken);
        return new EpochResolution(epoch, sessions.Distinct().ToDictionary(s => s, _ => "measured"));
    }
}
