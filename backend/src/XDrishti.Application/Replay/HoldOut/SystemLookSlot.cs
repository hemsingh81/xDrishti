using XDrishti.Application.Abstractions.Replay;
using XDrishti.Application.Lab;
using XDrishti.Domain.Lab;

namespace XDrishti.Application.Replay.HoldOut;

/// <summary>
/// The system kind's look slot (HO-01 D4, D10; SR-03): a block with no registration holds nothing (<c>NoSubject</c>); the newest registration for the block with no stored result keeps the block (<c>Available</c>, so the auto release waits for the run, AC 10);
/// one with a result is <c>Used</c>. A read that fails throws and the releaser keeps the block.
/// </summary>
internal sealed class SystemLookSlot(IHoldOutRegistrationStore store) : IHoldOutLookSlot
{
    public string Kind => HoldOutHorizon.SystemKind;

    public async ValueTask<LookAvailability> ReadAsync(DateOnly blockStart, CancellationToken cancellationToken)
    {
        if (await store.NewestForBlockAsync(blockStart, cancellationToken) is not { } newest)
        {
            return LookAvailability.NoSubject;
        }

        return await store.GetResultAsync(newest.Id, cancellationToken) is null ? LookAvailability.Available : LookAvailability.Used;
    }
}
