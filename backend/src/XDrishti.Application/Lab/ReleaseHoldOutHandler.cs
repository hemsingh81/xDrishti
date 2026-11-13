using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Lab;

/// <summary>The owner's release of a sealed hold-out block (HO-01, D4): the password was checked at the endpoint; <see cref="HoldOutReleaser"/> holds the rules and stores the release row and the next block in one transaction.</summary>
internal sealed class ReleaseHoldOutHandler(HoldOutReleaser releaser) : ICommandHandler<ReleaseHoldOutCommand, HoldOutBlockView>
{
    public async ValueTask<Result<HoldOutBlockView>> HandleAsync(ReleaseHoldOutCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return await releaser.ReleaseByOwnerAsync(command.Start, command.ForfeitLook, command.Actor, cancellationToken);
    }
}
