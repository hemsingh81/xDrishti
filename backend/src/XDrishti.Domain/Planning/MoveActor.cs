namespace XDrishti.Domain.Planning;

/// <summary>Who makes a move. Approving, modifying and rejecting are the owner's alone; the services only lapse, arm, invalidate, expire, trigger and close. There is no third kind: nothing approves on the owner's behalf.</summary>
public enum MoveActor
{
    /// <summary>The owner.</summary>
    Owner,

    /// <summary>A service (the nightly pipeline, the cutoff, the pre-open check, the live flush).</summary>
    System,
}
