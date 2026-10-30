using XDrishti.Domain.Execution;

namespace XDrishti.Execution.Domain.Readiness;

/// <summary>
/// The readiness verdict as a pure rule (spec P12-11 criteria 2 and 8). Six checks, each pass, fail or unavailable. A rejection by the broker (<c>ip_rejected</c>, <c>token_rejected</c>) is not_ready whatever else is unavailable; otherwise any unavailable check makes the
/// verdict unavailable, which blocks exactly like not_ready. Only a verdict where every check passes is ready. Nothing is guessed: an unread, stale or unparsable fact is unavailable.
/// </summary>
public static class ReadinessPolicy
{
    public const string Connected = "connected";
    public const string TokenCoversSession = "token_covers_session";
    public const string TokenLive = "token_live";
    public const string IpConfigured = "ip_configured";
    public const string IpAttested = "ip_attested";
    public const string NoBlockingHalt = "no_blocking_halt";
    public const string IpRejected = "ip_rejected";
    public const string TokenRejected = "token_rejected";

    public static readonly TimeSpan ProfileMaxAge = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan IpMaxAge = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan TokenAgreement = TimeSpan.FromMinutes(5);

    public static ReadinessVerdict Evaluate(ReadinessInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        List<ReadinessReason> reasons = [Connection(input), TokenCovers(input), TokenLiveCheck(input), IpConfiguredCheck(input), IpAttestedCheck(input), HaltCheck(input)];
        foreach (var rejection in input.Rejections)
        {
            if (rejection.Code == IpRejected && !(input.Attestation is { OrdersAllowed: true } a && a.At > rejection.Since))
            {
                reasons.Add(new(IpRejected, ReadinessOutcome.Fail, "ip_rejected", "An order was rejected for an invalid IP: check the line and the address at Dhan, then record a new attestation.", null, rejection.Since));
            }
            else if (rejection.Code == TokenRejected && !(input.Profile is { Readable: true, At: { } at } && at > rejection.Since && reasons.First(r => r.Code == TokenLive).Outcome == ReadinessOutcome.Pass))
            {
                reasons.Add(new(TokenRejected, ReadinessOutcome.Fail, "token_rejected", "The broker rejected the token: connect Dhan again.", null, rejection.Since));
            }
        }

        var state = reasons.Any(r => r.Code is IpRejected or TokenRejected && r.Outcome == ReadinessOutcome.Fail) ? ReadinessState.NotReady
            : reasons.Any(r => r.Outcome == ReadinessOutcome.Unavailable) ? ReadinessState.Unavailable
            : reasons.Any(r => r.Outcome == ReadinessOutcome.Fail) ? ReadinessState.NotReady
            : ReadinessState.Ready;
        return new(state, reasons);
    }

    /// <summary>The verdict when nothing could be judged (settings or the whole probe unavailable): one unavailable reason, never ready.</summary>
    public static ReadinessVerdict Unjudged(string detail) =>
        new(ReadinessState.Unavailable, [new("probe", ReadinessOutcome.Unavailable, detail, "Readiness could not be judged.")]);

    private static ReadinessReason Connection(ReadinessInput i) => i.Connection switch
    {
        null => new(Connected, ReadinessOutcome.Unavailable, "connection_unreadable", "The broker connection could not be read."),
        { Status: "Connected" } => new(Connected, ReadinessOutcome.Pass, "connected", "Dhan is connected."),
        _ => new(Connected, ReadinessOutcome.Fail, "not_connected", "Dhan is not connected: connect it."),
    };

    private static ReadinessReason TokenCovers(ReadinessInput i)
    {
        var r = TokenCover.Evaluate(i.Connection?.ExpiresAt, i.Now, i.Settings.CoverUntil, i.Settings.MinRemainingMinutes);
        return new(TokenCoversSession, r.Outcome, r.Detail, r.Text);
    }

    private static ReadinessReason TokenLiveCheck(ReadinessInput i)
    {
        var p = i.Profile;
        if (!p.Readable)
        {
            return p.Reason is "token_invalid" or "unauthorized"
                ? new(TokenLive, ReadinessOutcome.Fail, TokenRejected, "The broker rejected the token: connect Dhan again.")
                : new(TokenLive, ReadinessOutcome.Unavailable, "profile_unreadable", "The broker's profile could not be read.");
        }

        var age = p.At is { } at ? (int)Math.Max(0, (i.Now - at).TotalSeconds) : (int?)null;
        if (p.At is not { } readAt || i.Now - readAt > ProfileMaxAge)
        {
            return new(TokenLive, ReadinessOutcome.Unavailable, "profile_stale", "The broker's profile was not read in the last 5 minutes.", age);
        }

        if (p.Value?.TokenValidUntil is not { } valid)
        {
            return new(TokenLive, ReadinessOutcome.Unavailable, "profile_no_validity", "The profile does not say until when the token is valid.", age);
        }

        if (valid <= i.Now)
        {
            return new(TokenLive, ReadinessOutcome.Fail, "token_expired", "The broker says the token has ended: connect Dhan again.", age);
        }

        return i.Connection?.ExpiresAt is { } stored && (stored - valid).Duration() > TokenAgreement
            ? new(TokenLive, ReadinessOutcome.Fail, "token_mismatch", "The broker's token end differs from the stored one: connect Dhan again.", age)
            : new(TokenLive, ReadinessOutcome.Pass, "token_live", "The broker accepts the token.", age);
    }

    private static ReadinessReason IpConfiguredCheck(ReadinessInput i)
    {
        if (!IpLiteral.TryNormalize(i.Settings.ExpectedIp, out var expected))
        {
            return new(IpConfigured, ReadinessOutcome.Fail, "ip_not_declared", "No expected static IP is set.");
        }

        var r = i.Ip;
        if (!r.Readable || r.Value is null)
        {
            return new(IpConfigured, ReadinessOutcome.Unavailable, "ip_unreadable", "The IP configured at Dhan could not be read.");
        }

        var age = r.At is { } at ? (int)Math.Max(0, (i.Now - at).TotalSeconds) : (int?)null;
        if (r.At is not { } readAt || i.Now - readAt > IpMaxAge)
        {
            return new(IpConfigured, ReadinessOutcome.Unavailable, "ip_stale", "The IP at Dhan was not read in the last 15 minutes.", age);
        }

        return r.Value.Configures(expected)
            ? new(IpConfigured, ReadinessOutcome.Pass, "ip_configured", "The expected IP is set at Dhan.", age)
            : new(IpConfigured, ReadinessOutcome.Fail, "ip_not_configured", "The expected IP is not set at Dhan.", age);
    }

    private static ReadinessReason IpAttestedCheck(ReadinessInput i)
    {
        if (!IpLiteral.TryNormalize(i.Settings.ExpectedIp, out var expected))
        {
            return new(IpAttested, ReadinessOutcome.Fail, "ip_not_declared", "No expected static IP is set.");
        }

        if (i.Attestation is not { } a || !IpLiteral.TryNormalize(a.Ip, out var attested) || attested != expected)
        {
            return new(IpAttested, ReadinessOutcome.Fail, "no_attestation", "No attestation of this IP: record it after the sandbox check at Dhan.");
        }

        var age = (int)Math.Max(0, (i.Now - a.At).TotalSeconds);
        if (!a.OrdersAllowed)
        {
            return new(IpAttested, ReadinessOutcome.Fail, "orders_not_allowed", "The attestation says orders are not allowed for this IP.", age);
        }

        return i.Now - a.At > TimeSpan.FromDays(i.Settings.AttestationMaxAgeDays)
            ? new(IpAttested, ReadinessOutcome.Fail, "attestation_old", $"The attestation is older than {i.Settings.AttestationMaxAgeDays} days: record it again.", age)
            : new(IpAttested, ReadinessOutcome.Pass, "ip_attested", "The IP is attested.", age);
    }

    // Halts that this slice clears itself (token_invalid, ip_not_allowed) are not counted: they end when the verdict is ready, so counting them would keep them for ever. Any other active halt, or a halt record that cannot be read, is not ready.
    private static ReadinessReason HaltCheck(ReadinessInput i) => i.OtherHaltActive switch
    {
        null => new(NoBlockingHalt, ReadinessOutcome.Unavailable, "halts_unreadable", "The halt record could not be read."),
        true => new(NoBlockingHalt, ReadinessOutcome.Fail, "halt_active", "A halt is in force."),
        false => new(NoBlockingHalt, ReadinessOutcome.Pass, "no_halt", "No halt blocks entries."),
    };
}
