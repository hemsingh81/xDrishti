using XDrishti.Application.Abstractions.Lab;
using XDrishti.Domain.Lab;

namespace XDrishti.Application.Lab;

/// <summary>The strategy kind's look slot (HO-01 D4, D10): <c>Used</c> when the block's fresh strategy look is taken, <c>Available</c> when a pre-dated version with a stored development pass could still take it, else <c>NoSubject</c>. A failed read throws and the releaser reads it as unavailable.</summary>
internal sealed class StrategyLookSlot(IHoldOutStore store, HoldOutStrategyCandidates candidates) : IHoldOutLookSlot
{
    public string Kind => HoldOutUnlocker.StrategyKind;

    public async ValueTask<LookAvailability> ReadAsync(DateOnly blockStart, CancellationToken cancellationToken)
    {
        if (await store.HasFreshLookAsync(blockStart, Kind, cancellationToken))
        {
            return LookAvailability.Used;
        }

        var block = (await store.ListBlocksAsync(cancellationToken)).Select(b => b.Block).FirstOrDefault(b => b.Start == blockStart);
        return block is not null && (await candidates.FindAsync(block, cancellationToken)).Count > 0 ? LookAvailability.Available : LookAvailability.NoSubject;
    }
}
