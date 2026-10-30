using XDrishti.Domain.Execution;

namespace XDrishti.Execution.Domain.Readiness;

public enum ReadinessOutcome
{
    Pass,
    Fail,
    Unavailable,
}

public enum ReadinessState
{
    Ready,
    NotReady,
    Unavailable,
}

public static class ReadinessWords
{
    public static string Word(ReadinessOutcome outcome) => outcome switch { ReadinessOutcome.Pass => "pass", ReadinessOutcome.Fail => "fail", _ => "unavailable" };

    public static string Word(ReadinessState state) => state switch { ReadinessState.Ready => "ready", ReadinessState.NotReady => "not_ready", _ => "unavailable" };

    public static ReadinessOutcome Outcome(string? word) => word switch { "pass" => ReadinessOutcome.Pass, "fail" => ReadinessOutcome.Fail, _ => ReadinessOutcome.Unavailable };

    public static ReadinessState State(string? word) => word switch { "ready" => ReadinessState.Ready, "not_ready" => ReadinessState.NotReady, _ => ReadinessState.Unavailable };
}

/// <summary>One check of the verdict. <see cref="Code"/> is the check (<c>connected</c>, <c>token_covers_session</c>, <c>token_live</c>, <c>ip_configured</c>, <c>ip_attested</c>, <c>no_blocking_halt</c>, or <c>ip_rejected</c>/<c>token_rejected</c>); <see cref="Detail"/> is a short word for why. The text never holds a token, an address or a client id.</summary>
public sealed record ReadinessReason(string Code, ReadinessOutcome Outcome, string Detail, string Text, int? AgeSeconds = null, DateTimeOffset? Since = null);

public sealed record ReadinessVerdict(ReadinessState State, IReadOnlyList<ReadinessReason> Reasons)
{
    /// <summary>The same state and the same checks with the same outcomes: ages and texts that only move with the clock are not a change.</summary>
    public bool SameAs(ReadinessState state, IReadOnlyList<ReadinessReason> reasons) =>
        State == state && Reasons.Count == reasons.Count && Reasons.Zip(reasons).All(p => p.First.Code == p.Second.Code && p.First.Outcome == p.Second.Outcome && p.First.Detail == p.Second.Detail);
}

/// <summary>What a broker read produced and when. An unreadable read has no value and a short code.</summary>
public sealed record Reading<T>(bool Readable, T? Value, string? Reason, DateTimeOffset? At);

/// <summary>The profile's answer, reduced to the one fact readiness needs: until when the broker says the token is valid.</summary>
public sealed record ProfileFacts(DateTimeOffset? TokenValidUntil);

/// <summary>What <c>getIP</c> reports as configured at Dhan (never the address a request came from; spec D4). The dates are those of the last change of each slot.</summary>
public sealed record IpFacts(string? Primary, string? Secondary, DateOnly? ModifyDatePrimary, DateOnly? ModifyDateSecondary)
{
    public static readonly TimeSpan LockPeriod = TimeSpan.FromDays(7);

    public bool Configures(string ip) => IpLiteral.TryNormalize(ip, out var wanted) && (Same(Primary, wanted) || Same(Secondary, wanted));

    /// <summary>The first day an IP may be changed again at Dhan (seven days after the newest change), or null when no date is known.</summary>
    public DateOnly? ModifyAfter => (ModifyDatePrimary, ModifyDateSecondary) switch
    {
        (null, null) => null,
        ({ } a, { } b) => (a > b ? a : b).AddDays(7),
        ({ } a, null) => a.AddDays(7),
        (null, { } b) => b.AddDays(7),
    };

    private static bool Same(string? stored, string wanted) => IpLiteral.TryNormalize(stored, out var n) && n == wanted;
}

/// <summary>The owner's claim, from Dhan's web tool, that orders are allowed for this address.</summary>
public sealed record IpAttestation(Guid AccountId, string Ip, bool OrdersAllowed, DateTimeOffset At);

/// <summary>A broker rejection that voids readiness at once (<c>ip_rejected</c> or <c>token_rejected</c>) and the moment it happened.</summary>
public sealed record Rejection(string Code, DateTimeOffset Since);

public sealed record ReadinessSettings(TimeOnly CoverUntil, int MinRemainingMinutes, string ExpectedIp, int AttestationMaxAgeDays, int IntervalSeconds);

public sealed record ReadinessInput(
    DateTimeOffset Now, ConnectionFacts? Connection, Reading<ProfileFacts> Profile, Reading<IpFacts> Ip, IpAttestation? Attestation, bool? OtherHaltActive, IReadOnlyList<Rejection> Rejections, ReadinessSettings Settings);
