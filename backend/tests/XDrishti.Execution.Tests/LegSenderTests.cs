using Microsoft.Extensions.Time.Testing;
using XDrishti.Execution.Application;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain;

namespace XDrishti.Execution.Tests;

public sealed class LegSenderTests
{
    private static readonly Guid Account = Guid.NewGuid();
    private static readonly string Cid = ClientOrderId.Create(new DateOnly(2026, 10, 26), 361, LegKind.Entry, 1).Value.ToString();
    private static readonly BrokerLeg Entry = new(Cid, LegKind.Entry, ExecSide.Buy, "1594", 10, 1500m);

    private sealed class Modes(ModeAnswer? answer) : IModeSource
    {
        public Task<ModeAnswer?> AnswerAsync(Guid accountId, CancellationToken cancellationToken) => Task.FromResult(answer);
    }

    private sealed class Halts(params HaltReading[] readings) : IHaltSource
    {
        private int _i;

        public Task<HaltReading> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(readings[Math.Min(_i++, readings.Length - 1)]);
    }

    private sealed class PreSend(bool open) : IPreSendCheck
    {
        public Task<GateDecision> CheckAsync(Guid accountId, long intentId, BrokerLeg leg, CancellationToken cancellationToken) =>
            Task.FromResult(open ? GateDecision.Allowed : new GateDecision(false, "check_failed"));
    }

    private sealed class Journal(Action<string>? hook = null) : ISendJournal
    {
        public List<string> Steps { get; } = [];

        public Task BeginAsync(Guid accountId, long intentId, BrokerLeg leg, CancellationToken cancellationToken)
        {
            hook?.Invoke("begin");
            Steps.Add("begin");
            return Task.CompletedTask;
        }

        public Task DroppedAsync(BrokerLeg leg, string reason, CancellationToken cancellationToken)
        {
            Steps.Add("dropped:" + reason);
            return Task.CompletedTask;
        }

        public Task OutcomeAsync(BrokerLeg leg, BrokerAck ack, CancellationToken cancellationToken)
        {
            Steps.Add("outcome:" + ack.Outcome);
            return Task.CompletedTask;
        }
    }

    private static readonly ModeAnswer Open = new(ExecMode.SemiAuto, true);

    private static (LegSender Sender, FakeBroker Broker, Journal Journal, FakeTimeProvider Clock) Make(
        ModeAnswer? mode = null, ExecEnvironment env = ExecEnvironment.Sandbox, IHaltSource? halts = null, bool preSend = true, Journal? journal = null, int perSecond = 2, bool noMode = false)
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 26, 4, 0, 0, TimeSpan.Zero));
        var broker = new FakeBroker();
        journal ??= new Journal();
        var sender = new LegSender(broker, new Modes(noMode ? null : mode ?? Open), halts ?? new Halts(HaltReading.Clear), new PreSend(preSend), journal, new ExecRateLimiter(clock, perSecond, 30, 200), env, clock, TimeSpan.FromSeconds(5));
        return (sender, broker, journal, clock);
    }

    private static SendRequest Req(FakeTimeProvider clock, BrokerLeg? leg = null) => new(Account, 361, leg ?? Entry, clock.GetUtcNow());

    [Theory]
    [InlineData(ExecEnvironment.None, "environment_none")]
    [InlineData(ExecEnvironment.Live, "live_not_enabled")]
    public async Task Only_the_sandbox_environment_can_send_live_is_refused_even_with_everything_open(ExecEnvironment env, string reason)
    {
        var (sender, broker, journal, clock) = Make(env: env);

        var result = await sender.SendAsync(Req(clock), CancellationToken.None);

        (result.Status, result.Reason).ShouldBe((SendStatus.Refused, reason));
        broker.Calls.ShouldBe(0);
        journal.Steps.ShouldBeEmpty();
    }

    [Fact]
    public async Task Each_closed_input_is_a_refusal_with_no_record_and_no_request()
    {
        var cases = new (string Reason, Func<(LegSender, FakeBroker, Journal, FakeTimeProvider)> Build)[]
        {
            ("no_mode_answer", () => Make(noMode: true)),
            ("execution_disabled", () => Make(mode: new ModeAnswer(ExecMode.SemiAuto, false))),
            ("mode_not_executing", () => Make(mode: new ModeAnswer(ExecMode.Paper, true))),
            ("halted", () => Make(halts: new Halts(HaltReading.Halted))),
            ("halted", () => Make(halts: new Halts(HaltReading.Unreadable))),
            ("check_failed", () => Make(preSend: false)),
        };

        foreach (var (reason, build) in cases)
        {
            var (sender, broker, journal, clock) = build();
            var result = await sender.SendAsync(Req(clock), CancellationToken.None);
            (result.Status, result.Reason).ShouldBe((SendStatus.Refused, reason));
            broker.Calls.ShouldBe(0);
            journal.Steps.ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task The_closed_defaults_of_the_host_never_send()
    {
        var clock = new FakeTimeProvider();
        var closedHalts = new Halts(HaltReading.Unreadable);
        var sender = new LegSender(new FakeBroker(), new Modes(null), closedHalts, new PreSend(false), new Journal(), new ExecRateLimiter(clock, 2, 30, 200), ExecEnvironment.Sandbox, clock, TimeSpan.FromSeconds(5));

        (await sender.SendAsync(Req(clock), CancellationToken.None)).Status.ShouldBe(SendStatus.Refused);
    }

    [Fact]
    public async Task A_send_writes_ahead_then_sends_then_records_the_outcome()
    {
        var steps = new List<string>();
        var journal = new Journal(steps.Add);
        var (sender, broker, _, clock) = Make(journal: journal);

        var result = await sender.SendAsync(Req(clock), CancellationToken.None);

        result.Status.ShouldBe(SendStatus.Sent);
        journal.Steps.ShouldBe(["begin", "outcome:Acknowledged"]);
        broker.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task A_failing_write_ahead_means_nothing_is_sent()
    {
        var journal = new Journal(_ => throw new InvalidOperationException("db down"));
        var (sender, broker, _, clock) = Make(journal: journal);

        await Should.ThrowAsync<InvalidOperationException>(() => sender.SendAsync(Req(clock), CancellationToken.None));
        broker.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task A_halt_that_appears_after_the_write_ahead_drops_an_entry_but_not_an_exit()
    {
        var (sender, broker, journal, clock) = Make(halts: new Halts(HaltReading.Clear, HaltReading.Halted));

        var result = await sender.SendAsync(Req(clock), CancellationToken.None);

        (result.Status, result.Reason).ShouldBe((SendStatus.Dropped, "halted_before_send"));
        journal.Steps.ShouldBe(["begin", "dropped:halted_before_send"]);
        broker.Calls.ShouldBe(0);

        var exit = Entry with { ClientOrderId = ClientOrderId.Create(new DateOnly(2026, 10, 26), 361, LegKind.Exit, 1).Value.ToString(), Kind = LegKind.Exit, Side = ExecSide.Sell };
        var (s2, b2, _, c2) = Make(halts: new Halts(HaltReading.Halted));
        (await s2.SendAsync(Req(c2, exit), CancellationToken.None)).Status.ShouldBe(SendStatus.Sent);
        b2.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task A_send_older_than_the_valid_window_is_refused_stale_and_the_rate_limit_holds_under_a_burst()
    {
        var (sender, broker, _, clock) = Make();
        var old = Req(clock);
        clock.Advance(TimeSpan.FromSeconds(6));

        (await sender.SendAsync(old, CancellationToken.None)).Reason.ShouldBe("stale");

        var results = new List<SendResult>();
        for (var i = 0; i < 50; i++)
        {
            results.Add(await sender.SendAsync(Req(clock), CancellationToken.None));
        }

        results.Count(r => r.Status == SendStatus.Sent).ShouldBe(2);
        results.Where(r => r.Status == SendStatus.Refused).ShouldAllBe(r => r.Reason == "rate_limited");
        broker.Calls.ShouldBe(2);
        clock.Advance(TimeSpan.FromSeconds(1));
        (await sender.SendAsync(Req(clock), CancellationToken.None)).Status.ShouldBe(SendStatus.Sent);
    }

    [Fact]
    public void The_limiter_also_holds_the_minute_and_day_caps()
    {
        var clock = new FakeTimeProvider();
        var limiter = new ExecRateLimiter(clock, 100, 3, 5);

        Enumerable.Range(0, 4).Select(_ => limiter.TryAdmit()).ShouldBe([true, true, true, false]);
        clock.Advance(TimeSpan.FromSeconds(61));
        limiter.TryAdmit().ShouldBeTrue();
        limiter.TryAdmit().ShouldBeTrue();
        limiter.TryAdmit().ShouldBeFalse("five in a day");
        clock.Advance(TimeSpan.FromDays(1));
        limiter.TryAdmit().ShouldBeTrue();
    }

    [Fact]
    public async Task A_lost_response_is_resolved_by_lookup_not_by_sending_again()
    {
        var (sender, broker, _, clock) = Make();
        broker.DropNextResponse = true;

        var result = await sender.SendAsync(Req(clock), CancellationToken.None);
        result.Ack!.Outcome.ShouldBe(BrokerOutcome.Unknown);

        var found = await new LegLookup(broker, clock).ResolveAsync(Cid, CancellationToken.None);
        found.Verdict.ShouldBe(LookupVerdict.Found);
        broker.Legs.Count.ShouldBe(1);
        broker.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task An_absent_leg_needs_two_empty_lookups_two_seconds_apart()
    {
        var clock = new FakeTimeProvider();
        var broker = new FakeBroker();
        var task = new LegLookup(broker, clock).ResolveAsync(Cid, CancellationToken.None);

        task.IsCompleted.ShouldBeFalse();
        clock.Advance(TimeSpan.FromSeconds(2));
        (await task).Verdict.ShouldBe(LookupVerdict.NotSent);
    }

    [Fact]
    public async Task A_failing_lookup_keeps_the_leg_unknown()
    {
        var verdict = await new LegLookup(new ThrowingLookup(), new FakeTimeProvider()).ResolveAsync(Cid, CancellationToken.None);

        verdict.Verdict.ShouldBe(LookupVerdict.StillUnknown);
    }

    private sealed class ThrowingLookup : IBrokerOrders
    {
        public Task<BrokerAck> PlaceAsync(BrokerLeg leg, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<BrokerAck> ModifyAsync(string clientOrderId, decimal price, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<BrokerAck> CancelAsync(string clientOrderId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<BrokerLeg?> LookupAsync(string clientOrderId, CancellationToken cancellationToken) => throw new BrokerLookupFailedException("x");
    }

    [Theory]
    [InlineData(LegKind.Entry, 1, false)]
    [InlineData(LegKind.Target, 1, false)]
    [InlineData(LegKind.Stop, 1, true)]
    [InlineData(LegKind.Exit, 2, true)]
    [InlineData(LegKind.Flatten, 3, false)]
    public void Only_protective_and_exit_legs_may_be_resent_at_most_three_attempts_and_only_when_the_lookup_is_verified(LegKind kind, int attempt, bool expected)
    {
        ResendPolicy.MayResend(kind, attempt, lookupVerified: true).ShouldBe(expected);
        ResendPolicy.MayResend(kind, attempt, lookupVerified: false).ShouldBeFalse();
    }

    [Fact]
    public void The_lookup_verified_flag_is_registered_and_defaults_off() =>
        ExecSettingRegistry.Find("exec.lookup_verified").ShouldNotBeNull().Default.ShouldBe("false");

    [Theory]
    [InlineData("sandbox", ExecEnvironment.Sandbox)]
    [InlineData("SANDBOX", ExecEnvironment.Sandbox)]
    [InlineData("Live", ExecEnvironment.Live)]
    [InlineData("none", ExecEnvironment.None)]
    [InlineData("1", ExecEnvironment.None)]
    [InlineData("2", ExecEnvironment.None)]
    [InlineData("None, Sandbox", ExecEnvironment.None)]
    [InlineData("Sandbox,Live", ExecEnvironment.None)]
    [InlineData(" sandbox", ExecEnvironment.None)]
    [InlineData("sandbox ", ExecEnvironment.None)]
    [InlineData("", ExecEnvironment.None)]
    [InlineData(null, ExecEnvironment.None)]
    public void The_environment_text_is_read_strictly(string? text, ExecEnvironment expected) =>
        ExecEnvironmentParser.Parse(text).ShouldBe(expected);

    [Fact]
    public async Task Over_2000_seeds_of_random_faults_no_client_id_is_ever_accepted_twice()
    {
        for (var seed = 0; seed < 2000; seed++)
        {
            var rng = new Random(seed);
            var (sender, broker, _, clock) = Make(perSecond: 5);
            for (var i = 0; i < 4; i++)
            {
                broker.DropNextResponse = rng.Next(3) == 0;
                var r = await sender.SendAsync(Req(clock), CancellationToken.None);
                if (r.Ack?.Outcome == BrokerOutcome.Unknown)
                {
                    // the only allowed follow-up for an entry is a lookup: it is found, and no second order exists
                    (await new LegLookup(broker, clock).ResolveAsync(Cid, CancellationToken.None)).Verdict.ShouldBe(LookupVerdict.Found, $"seed {seed}");
                    break;
                }
            }

            broker.Legs.Count.ShouldBeLessThanOrEqualTo(1, $"seed {seed}");
        }
    }
}
