using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Lab;
using XDrishti.Domain.Lab;

namespace XDrishti.Application.Learning;

/// <summary>The model kind's look slot (CF-10e part 3, HO-01 D4 and D10): <c>Used</c> when the block's fresh model look is taken, <c>Available</c> when a pre-dated Eligible champion could still take it or finish its own claimed look, <c>Used</c> when the fresh look is taken and finished, else <c>NoSubject</c>. A failed read throws and the releaser reads it as unavailable, which keeps the block.</summary>
internal sealed class ModelLookSlot(IHoldOutStore store, ModelHoldOutCandidates candidates) : IHoldOutLookSlot
{
    public string Kind => ModelHoldOutLooks.ModelKind;

    public async ValueTask<LookAvailability> ReadAsync(DateOnly blockStart, CancellationToken cancellationToken)
    {
        var block = (await store.ListBlocksAsync(cancellationToken)).Select(b => b.Block).FirstOrDefault(b => b.Start == blockStart);

        // a champion that may still take, or finish, the look keeps the block: its slot is Available (never NoSubject), so the auto release waits and no forfeit hides it; an input that is missing is the act's refusal, not an absent subject
        if (block is not null && (await candidates.FindAsync(block, cancellationToken)).Count > 0)
        {
            return LookAvailability.Available;
        }

        return await store.HasFreshLookAsync(blockStart, Kind, cancellationToken) ? LookAvailability.Used : LookAvailability.NoSubject;
    }

    public async ValueTask<string?> StallNoteAsync(DateOnly blockStart, CancellationToken cancellationToken)
    {
        var block = (await store.ListBlocksAsync(cancellationToken)).Select(b => b.Block).FirstOrDefault(b => b.Start == blockStart);
        return block is not null && await candidates.HasOpenLookAsync(block, cancellationToken)
            ? "The model's claimed look keeps failing (see the log, learning.model.holdout_pass_failed); it ends as failed after its resume window, and then the block can be released."
            : null;
    }
}
