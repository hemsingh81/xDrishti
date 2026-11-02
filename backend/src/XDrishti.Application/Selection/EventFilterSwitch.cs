using XDrishti.Application.Abstractions.Selection;
using XDrishti.Domain.Selection;

namespace XDrishti.Application.Selection;

/// <summary>
/// <see cref="IEventFilterSwitch"/> (P15-06): the newest row of the decision history; no row is OFF, and a history that cannot be read is OFF (the filter then changes nothing in the live paths). Cancellation passes through.
/// </summary>
internal sealed class EventFilterSwitch(IEventFilterDecisions decisions) : IEventFilterSwitch
{
    public async ValueTask<EventFilterState> CurrentAsync(CancellationToken cancellationToken)
    {
        try
        {
            return EventFilterSettings.StateOf(await decisions.RowsAsync(2, cancellationToken));
        }
#pragma warning disable CA1031 // An unreadable history is OFF (the safe state); cancellation is rethrown by the filter.
        catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
#pragma warning restore CA1031
        {
            _ = failure;
            return EventFilterState.Off;
        }
    }
}
