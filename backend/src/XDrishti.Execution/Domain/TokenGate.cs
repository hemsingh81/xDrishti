namespace XDrishti.Execution.Domain;

/// <summary>What the stored broker connection says (status word and expiry), read by the xd_exec role from three columns. Null fields are "unknown".</summary>
public sealed record ConnectionFacts(string? Status, DateTimeOffset? ExpiresAt);

/// <summary>
/// The token check of P12-02 criterion 10 as a pure rule. Open only when the connection is <c>Connected</c>, has an expiry, and that expiry is further away than <c>exec.token_min_remaining_min</c>. No row, an unreadable row, a status other than
/// Connected or an unknown expiry is <c>token_unavailable</c>: unavailable never counts as a pass. An expiry in the past is <c>token_expired</c>, one inside the margin <c>token_expiring</c>.
/// </summary>
public static class TokenGate
{
    public static GateDecision Judge(ConnectionFacts? facts, DateTimeOffset now, int minRemainingMinutes)
    {
        if (facts is null || !string.Equals(facts.Status, "Connected", StringComparison.Ordinal) || facts.ExpiresAt is not { } expires)
        {
            return new GateDecision(false, "token_unavailable");
        }

        if (expires <= now)
        {
            return new GateDecision(false, "token_expired");
        }

        return expires - now < TimeSpan.FromMinutes(minRemainingMinutes) ? new GateDecision(false, "token_expiring") : GateDecision.Allowed;
    }
}
