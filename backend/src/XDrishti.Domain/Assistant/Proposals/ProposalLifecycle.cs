namespace XDrishti.Domain.Assistant.Proposals;

/// <summary>
/// The lifecycle of a proposal (spec P9-08 AC 8, 10): pending, accepted, rejected or expired. Expiry is computed on read (a pending proposal whose expiry has passed is expired); nothing is updated, because the tables are insert-only. Pure.
/// </summary>
public static class ProposalLifecycle
{
    public const string Pending = "pending";
    public const string Accepted = "accepted";
    public const string Rejected = "rejected";
    public const string Expired = "expired";

    public const int RepeatBlockDays = 30;
    public const int HypothesisExpiryDays = 7;
    public const int BasketExpiryDays = 3;

    public static IReadOnlyList<string> States { get; } = [Pending, Accepted, Rejected, Expired];

    /// <summary>When a proposal made at <paramref name="createdAt"/> stops being open.</summary>
    public static DateTimeOffset ExpiryOf(string kind, DateTimeOffset createdAt, int hypothesisDays = HypothesisExpiryDays, int basketDays = BasketExpiryDays) =>
        createdAt.AddDays(kind == ProposalKind.Hypothesis ? hypothesisDays : basketDays);

    /// <summary>The state of a proposal now: the decision if there is one, else pending or expired by the clock.</summary>
    public static string StateOf(string? decision, DateTimeOffset expiresAt, DateTimeOffset now) =>
        decision is not null ? decision : expiresAt <= now ? Expired : Pending;
}
