using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Lab;
using XDrishti.Domain.Lab;

namespace XDrishti.Application.Learning;

/// <summary>The model kind's look slot (CF-10e part 3, HO-01 D4 and D10): <c>Used</c> when the block's fresh model look is taken, <c>Available</c> when a pre-dated Eligible champion could still take it, else <c>NoSubject</c>. A failed read throws and the releaser reads it as unavailable, which keeps the block.</summary>
internal sealed class ModelLookSlot(IHoldOutStore store, ModelHoldOutCandidates candidates) : IHoldOutLookSlot
{
    public string Kind => ModelHoldOutLooks.ModelKind;

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
