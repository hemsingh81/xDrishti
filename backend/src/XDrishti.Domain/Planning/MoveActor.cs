namespace XDrishti.Domain.Planning;

/// <summary>Who makes a move. Approving, modifying and rejecting are the owner's alone; the services only lapse, arm, invalidate, expire, trigger and close. There is no third kind here: nothing approves on the owner's behalf. The one exception is the optional rule of P7-12, which is not a move of this table at all: it is written by its own store method as an approval of kind auto (never as the owner), only while the owner's setting is ON.</summary>
public enum MoveActor
{
    /// <summary>The owner.</summary>
    Owner,

    /// <summary>A service (the nightly pipeline, the cutoff, the pre-open check, the live flush).</summary>
    System,
}
