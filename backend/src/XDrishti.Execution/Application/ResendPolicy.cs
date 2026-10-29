using XDrishti.Execution.Domain;

namespace XDrishti.Execution.Application;

/// <summary>After an unresolved or absent leg: only a stop, an exit or a flatten may be sent again (a new client id, attempt + 1, at most 3). An entry never is: a lost entry costs a missed trade, a duplicated one a double position.</summary>
public static class ResendPolicy
{
    public const int MaxAttempts = 3;

    /// <param name="kind">The leg kind.</param>
    /// <param name="attempt">The attempt just made.</param>
    /// <param name="lookupVerified">The owner-only setting <c>exec.lookup_verified</c> (default OFF): the lookup path and the meaning of its not-found answer are unverified against Dhan, so no resend is allowed until the owner confirms them.</param>
    public static bool MayResend(LegKind kind, int attempt, bool lookupVerified) => lookupVerified && kind is LegKind.Stop or LegKind.Exit or LegKind.Flatten && attempt < MaxAttempts;
}
