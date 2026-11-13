namespace XDrishti.Domain.Lab;

/// <summary>When a sealed block that nobody released becomes a bell notice (HO-01 D4): after <see cref="Sessions"/> sessions sealed.</summary>
public static class HoldOutStall
{
    /// <summary>The number of sealed sessions after which the notice appears.</summary>
    public const int Sessions = 20;

    /// <summary>Whether <paramref name="sealedSessions"/> trading days have passed since the block sealed.</summary>
    public static bool IsStalled(int sealedSessions) => sealedSessions >= Sessions;
}
