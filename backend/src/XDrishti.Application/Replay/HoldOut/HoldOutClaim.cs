using XDrishti.Application.Abstractions.Lab;
using XDrishti.Domain.Common;
using XDrishti.Domain.Lab;

namespace XDrishti.Application.Replay.HoldOut;

/// <summary>
/// The proof that the one <c>system</c> look of a block was claimed (SR-03 D4): it can only be made from a stored, fresh look of kind <c>system</c>, so a <see cref="HoldOutBlockReader"/> (which needs one) cannot exist before the claim. An architecture test pins that
/// only the claim handler and the runner name this type.
/// </summary>
internal sealed class HoldOutClaim
{
    private HoldOutClaim(StoredUnlock look, Guid registrationId)
    {
        Look = look;
        RegistrationId = registrationId;
    }

    /// <summary>The stored look.</summary>
    public StoredUnlock Look { get; }

    /// <summary>The registration that owns the look.</summary>
    public Guid RegistrationId { get; }

    /// <summary>The look's block start.</summary>
    public DateOnly BlockStart => Look.BlockStart;

    /// <summary>Makes the claim of a stored look; refused for any other kind and for a re-used look (which never counts and reads nothing).</summary>
    public static Result<HoldOutClaim> From(StoredUnlock look, Guid registrationId)
    {
        ArgumentNullException.ThrowIfNull(look);
        return look.Kind != HoldOutHorizon.SystemKind || look.ReusedWindow || look.StrategyId != HoldOutRegistrationIds.LookKey(registrationId)
            ? Result.Failure<HoldOutClaim>(Error.Conflict(HoldOutRegistrationErrors.UsedCode, "There is no fresh system look for this registration."))
            : Result.Success(new HoldOutClaim(look, registrationId));
    }
}
