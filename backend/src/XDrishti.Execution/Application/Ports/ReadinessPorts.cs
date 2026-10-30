using XDrishti.Execution.Domain;
using XDrishti.Execution.Domain.Readiness;

namespace XDrishti.Execution.Application.Ports;

/// <summary>The stored broker connection as readiness sees it: status word and token end, three columns, never a token. Null when there is no row or it cannot be read.</summary>
public interface IConnectionFacts
{
    Task<ConnectionFacts?> ReadAsync(Guid accountId, CancellationToken cancellationToken);
}

/// <summary>The settings readiness needs, from the newest good settings read; null when none was read recently (the loop then says unavailable and reads nothing).</summary>
public interface IReadinessSettings
{
    ReadinessSettings? Current();
}

/// <summary>Which accounts are judged (effective mode assisted or above) and whether a date is a trading day.</summary>
public interface IReadinessAccounts
{
    Task<IReadOnlyList<Guid>> AssistedAccountsAsync(CancellationToken cancellationToken);

    Task<bool> IsTradingDayAsync(DateOnly date, CancellationToken cancellationToken);
}

public sealed record ReadinessCheckDraft(DateTimeOffset At, Guid AccountId, DateOnly SessionDate, ReadinessState Verdict, IReadOnlyList<ReadinessReason> Reasons, DateTimeOffset? TokenExpiresAt, DateTimeOffset? ProfileReadAt, DateTimeOffset? IpReadAt, DateOnly? IpModifyAfter);

public sealed record ReadinessCheckRecord(long Id, DateTimeOffset At, Guid AccountId, DateOnly SessionDate, ReadinessState Verdict, IReadOnlyList<ReadinessReason> Reasons);

public sealed record DayStateDraft(Guid AccountId, DateOnly SessionDate, ReadinessState State, IReadOnlyList<ReadinessReason> Reasons, long? CheckId, bool Late, DateTimeOffset At);

/// <summary>The newest readiness check of an account, read only (what the intent creator of P12-08 needs; it cannot write the record).</summary>
public interface IReadinessReads
{
    Task<ReadinessCheckRecord?> LastCheckAsync(Guid accountId, CancellationToken cancellationToken);
}

/// <summary>The insert-only readiness record. There is no update and no delete; a day state exists once per account and session.</summary>
public interface IReadinessStore : IReadinessReads
{
    Task<ReadinessCheckRecord> AppendCheckAsync(ReadinessCheckDraft draft, CancellationToken cancellationToken);

    Task<bool> HasDayStateAsync(Guid accountId, DateOnly session, CancellationToken cancellationToken);

    /// <summary>False when the account already has a state for that session.</summary>
    Task<bool> AppendDayStateAsync(DayStateDraft draft, CancellationToken cancellationToken);

    /// <summary>The newest attestation of this account for this (normal form) address, or null.</summary>
    Task<IpAttestation?> NewestAttestationAsync(Guid accountId, string ip, CancellationToken cancellationToken);
}
