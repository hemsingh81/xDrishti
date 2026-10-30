using XDrishti.Execution.Domain;
using XDrishti.Execution.Domain.Readiness;

namespace XDrishti.Execution.Tests.Readiness;

/// <summary>P12-11 criteria 2 and 8: each of the six checks as pass, fail and unavailable; any unavailable makes the verdict unavailable (which blocks like not_ready); a broker rejection is not_ready at once.</summary>
public sealed class ReadinessPolicyTests
{
    private static readonly DateTimeOffset Now = Ist.At(27, 9, 10, 30);

    private static readonly ReadinessSettings Settings = new(new TimeOnly(16, 45), 20, MemReadiness.Ip, 30, 300);

    private static ReadinessInput Good() => new(
        Now, new ConnectionFacts("Connected", Ist.At(27, 17, 30)), Read(new ProfileFacts(Ist.At(27, 17, 30))), Read(new IpFacts(MemReadiness.Ip, null, null, null)),
        new IpAttestation(MemReadiness.Account, MemReadiness.Ip, true, Now.AddDays(-1)), false, [], Settings);

    private static Reading<T> Read<T>(T value, int secondsAgo = 0) => new(true, value, null, Now.AddSeconds(-secondsAgo));

    private static ReadinessOutcome Of(ReadinessVerdict verdict, string code) => verdict.Reasons.Single(r => r.Code == code).Outcome;

    [Fact]
    public void Everything_passing_is_ready()
    {
        var verdict = ReadinessPolicy.Evaluate(Good());

        verdict.State.ShouldBe(ReadinessState.Ready);
        verdict.Reasons.Select(r => r.Code).ShouldBe(["connected", "token_covers_session", "token_live", "ip_configured", "ip_attested", "no_blocking_halt"]);
        verdict.Reasons.ShouldAllBe(r => r.Outcome == ReadinessOutcome.Pass);
    }

    public static TheoryData<string, string, ReadinessOutcome, ReadinessState> Table => new()
    {
        { "connected", "no connection row", ReadinessOutcome.Unavailable, ReadinessState.Unavailable },
        { "connected", "disconnected", ReadinessOutcome.Fail, ReadinessState.NotReady },
        { "token_covers_session", "expiry before the cut-off", ReadinessOutcome.Fail, ReadinessState.NotReady },
        { "token_covers_session", "no expiry", ReadinessOutcome.Unavailable, ReadinessState.Unavailable },
        { "token_live", "profile unreadable", ReadinessOutcome.Unavailable, ReadinessState.Unavailable },
        { "token_live", "profile older than 5 minutes", ReadinessOutcome.Unavailable, ReadinessState.Unavailable },
        { "token_live", "profile validity ended", ReadinessOutcome.Fail, ReadinessState.NotReady },
        { "token_live", "profile validity disagrees with the stored expiry", ReadinessOutcome.Fail, ReadinessState.NotReady },
        { "ip_configured", "no expected ip", ReadinessOutcome.Fail, ReadinessState.NotReady },
        { "ip_configured", "ip not configured at dhan", ReadinessOutcome.Fail, ReadinessState.NotReady },
        { "ip_configured", "getIP unreadable", ReadinessOutcome.Unavailable, ReadinessState.Unavailable },
        { "ip_configured", "getIP older than 15 minutes", ReadinessOutcome.Unavailable, ReadinessState.Unavailable },
        { "ip_attested", "no attestation", ReadinessOutcome.Fail, ReadinessState.NotReady },
        { "ip_attested", "orders not allowed", ReadinessOutcome.Fail, ReadinessState.NotReady },
        { "ip_attested", "attestation 31 days old", ReadinessOutcome.Fail, ReadinessState.NotReady },
        { "ip_attested", "attestation of another ip", ReadinessOutcome.Fail, ReadinessState.NotReady },
        { "no_blocking_halt", "a halt is active", ReadinessOutcome.Fail, ReadinessState.NotReady },
        { "no_blocking_halt", "halt record unreadable", ReadinessOutcome.Unavailable, ReadinessState.Unavailable },
    };

    [Theory]
    [MemberData(nameof(Table))]
    public void Each_check_is_judged_on_its_own(string code, string scene, ReadinessOutcome expected, ReadinessState verdict)
    {
        var good = Good();
        var input = scene switch
        {
            "no connection row" => good with { Connection = null },
            "disconnected" => good with { Connection = new("Disconnected", Ist.At(27, 17, 30)) },
            "expiry before the cut-off" => good with { Connection = new("Connected", Ist.At(27, 15, 0)), Profile = Read(new ProfileFacts(Ist.At(27, 15, 0))) },
            "no expiry" => good with { Connection = new("Connected", null) },
            "profile unreadable" => good with { Profile = new(false, null, "timeout", null) },
            "profile older than 5 minutes" => good with { Profile = Read(new ProfileFacts(Ist.At(27, 17, 30)), 301) },
            "profile validity ended" => good with { Profile = Read(new ProfileFacts(Now.AddMinutes(-1))) },
            "profile validity disagrees with the stored expiry" => good with { Profile = Read(new ProfileFacts(Ist.At(27, 17, 30).AddMinutes(6))) },
            "no expected ip" => good with { Settings = Settings with { ExpectedIp = string.Empty } },
            "ip not configured at dhan" => good with { Ip = Read(new IpFacts("198.51.100.9", null, null, null)) },
            "getIP unreadable" => good with { Ip = new(false, null, "timeout", null) },
            "getIP older than 15 minutes" => good with { Ip = Read(new IpFacts(MemReadiness.Ip, null, null, null), 901) },
            "no attestation" => good with { Attestation = null },
            "orders not allowed" => good with { Attestation = good.Attestation! with { OrdersAllowed = false } },
            "attestation 31 days old" => good with { Attestation = good.Attestation! with { At = Now.AddDays(-31) } },
            "attestation of another ip" => good with { Attestation = good.Attestation! with { Ip = "198.51.100.9" } },
            "a halt is active" => good with { OtherHaltActive = true },
            "halt record unreadable" => good with { OtherHaltActive = null },
            _ => throw new InvalidOperationException(scene),
        };

        var result = ReadinessPolicy.Evaluate(input);

        Of(result, code).ShouldBe(expected, scene);
        result.State.ShouldBe(verdict, scene);
    }

    [Fact]
    public void An_unavailable_check_makes_the_verdict_unavailable_even_when_another_fails_and_it_never_reads_as_ready()
    {
        var input = Good() with { Connection = new("Disconnected", Ist.At(27, 17, 30)), OtherHaltActive = null };

        ReadinessPolicy.Evaluate(input).State.ShouldBe(ReadinessState.Unavailable);
    }

    [Fact]
    public void The_broker_refusing_the_profile_with_a_token_code_is_not_ready_with_the_token_rejected_word()
    {
        var verdict = ReadinessPolicy.Evaluate(Good() with { Profile = new(false, null, "token_invalid", null) });

        verdict.Reasons.Single(r => r.Code == "token_live").Detail.ShouldBe("token_rejected");
        verdict.State.ShouldBe(ReadinessState.NotReady);
    }

    [Fact]
    public void A_rejected_ip_stays_until_an_attestation_newer_than_the_rejection_never_by_time()
    {
        var rejection = new Rejection("ip_rejected", Now.AddHours(-2));
        var older = ReadinessPolicy.Evaluate(Good() with { Rejections = [rejection] });
        var fresh = ReadinessPolicy.Evaluate(Good() with { Rejections = [rejection], Attestation = new(MemReadiness.Account, MemReadiness.Ip, true, Now.AddHours(-1)) });
        var allowedFalse = ReadinessPolicy.Evaluate(Good() with { Rejections = [rejection], Attestation = new(MemReadiness.Account, MemReadiness.Ip, false, Now.AddHours(-1)) });

        older.State.ShouldBe(ReadinessState.NotReady);
        older.Reasons.ShouldContain(r => r.Code == "ip_rejected" && r.Since == rejection.Since);
        fresh.State.ShouldBe(ReadinessState.Ready);
        allowedFalse.State.ShouldBe(ReadinessState.NotReady);
    }

    [Fact]
    public void A_rejection_is_not_ready_even_when_something_else_is_unavailable()
    {
        var verdict = ReadinessPolicy.Evaluate(Good() with { Rejections = [new("ip_rejected", Now.AddHours(-2))], OtherHaltActive = null });

        verdict.State.ShouldBe(ReadinessState.NotReady);
    }

    [Fact]
    public void A_rejected_token_stays_until_a_profile_read_after_it_passes()
    {
        var rejection = new Rejection("token_rejected", Now.AddSeconds(-10));
        var before = ReadinessPolicy.Evaluate(Good() with { Rejections = [rejection], Profile = Read(new ProfileFacts(Ist.At(27, 17, 30)), 60) });
        var after = ReadinessPolicy.Evaluate(Good() with { Rejections = [rejection], Profile = Read(new ProfileFacts(Ist.At(27, 17, 30)), 1) });

        before.State.ShouldBe(ReadinessState.NotReady);
        after.State.ShouldBe(ReadinessState.Ready);
    }

    [Fact]
    public void The_texts_never_hold_the_address_or_a_token()
    {
        var verdict = ReadinessPolicy.Evaluate(Good() with { Ip = Read(new IpFacts("198.51.100.9", null, null, null)), Attestation = null });

        verdict.Reasons.ShouldAllBe(r => !r.Text.Contains(MemReadiness.Ip, StringComparison.Ordinal) && !r.Text.Contains("198.51.100.9", StringComparison.Ordinal));
    }
}
