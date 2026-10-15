using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Abstractions.Portfolio;

/// <summary>
/// The health of one sync kind of an account (P2-16, ADR 0027): the India days in a row whose last attempt failed, the latest failure (reason code and time), how many attempts failed on that day (the quiet retries use it), how far earlier history was read
/// (trades) and the version of the Dhan mappers the kind was last read with. A kind without a row is healthy: <see cref="None"/>.
/// </summary>
public sealed record SyncHealth(
    Guid AccountId,
    SyncHealthKind Kind,
    int ConsecutiveFailedDays,
    string? LastFailureReason,
    DateTimeOffset? LastFailedAt,
    int Attempts,
    DateOnly? DeepenedTo,
    bool DeepenExhausted,
    DateOnly? DeepenedOn,
    int MapperVersion)
{
    public static SyncHealth None(Guid accountId, SyncHealthKind kind) => new(accountId, kind, 0, null, null, 0, null, false, null, 0);

    /// <summary>True while the latest attempt failed and nothing has succeeded since.</summary>
    public bool IsFailing => LastFailureReason is not null;
}
