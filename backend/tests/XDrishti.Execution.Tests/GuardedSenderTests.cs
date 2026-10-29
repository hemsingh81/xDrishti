using Microsoft.Extensions.Time.Testing;
using XDrishti.Execution.Application;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain;

namespace XDrishti.Execution.Tests;

/// <summary>P12-02b: the send protocol with the settings in force, the token gate and the persistent history (all faked here; the database forms are in Database/).</summary>
public sealed class GuardedSenderTests
{
    private static readonly Guid Account = Guid.NewGuid();
    private static readonly string EntryId = ClientOrderId.Create(new DateOnly(2026, 10, 26), 361, LegKind.Entry, 1).Value.ToString();
    private static readonly string ExitId = ClientOrderId.Create(new DateOnly(2026, 10, 26), 361, LegKind.Exit, 1).Value.ToString();
    private static readonly BrokerLeg Entry = new(EntryId, LegKind.Entry, ExecSide.Buy, "1594", 10, 1500m);
    private static readonly BrokerLeg Exit = new(ExitId, LegKind.Exit, ExecSide.Sell, "1594", 10, 1495m);

    private sealed class Modes : IModeSource
    {
        public Task<ModeAnswer?> AnswerAsync(Guid accountId, CancellationToken cancellationToken) => Task.FromResult<ModeAnswer?>(new ModeAnswer(ExecMode.SemiAuto, true));
    }

    private sealed class Halts : IHaltSource
    {
        public Task<HaltReading> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(HaltReading.Clear);
    }

    private sealed class Open : IPreSendCheck
    {
        public Task<GateDecision> CheckAsync(Guid accountId, long intentId, BrokerLeg leg, CancellationToken cancellationToken) => Task.FromResult(GateDecision.Allowed);
    }

    private sealed class Settings(SendSettings? value) : ISendSettings
    {
        public SendSettings? Current() => value;
    }

    private sealed class Token(GateDecision decision) : ITokenGate
    {
        public int Asked { get; private set; }

        public int Margin { get; private set; }

        public Task<GateDecision> CheckAsync(Guid accountId, int minRemainingMinutes, CancellationToken cancellationToken)
        {
            Asked++;
            Margin = minRemainingMinutes;
            return Task.FromResult(decision);
        }
    }

    private sealed class History(Func<IReadOnlyList<DateTimeOffset>> read) : ISendHistory
    {
        public int Reads { get; private set; }

        public Task<IReadOnlyList<DateTimeOffset>> BeginsSinceAsync(DateTimeOffset since, CancellationToken cancellationToken)
        {
            Reads++;
            return Task.FromResult(read());
        }
    }

    private sealed class Journal : ISendJournal
    {
        public int Begins { get; private set; }

        public Task BeginAsync(Guid accountId, long intentId, BrokerLeg leg, CancellationToken cancellationToken)
        {
            Begins++;
            return Task.CompletedTask;
        }

        public Task DroppedAsync(BrokerLeg leg, string reason, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task OutcomeAsync(BrokerLeg leg, BrokerAck ack, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private static readonly SendSettings Defaults = new(2, 30, 200, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), 1.0m, 20);

    private static (LegSender Sender, FakeBroker Broker, Journal Journal, FakeTimeProvider Clock, Token Token, History History) Make(
        SendSettings? settings = null, GateDecision? token = null, Func<IReadOnlyList<DateTimeOffset>>? history = null, bool noSettings = false)
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 26, 4, 0, 0, TimeSpan.Zero));
        var broker = new FakeBroker(clock);
        var journal = new Journal();
        var tokenGate = new Token(token ?? GateDecision.Allowed);
        var hist = new History(history ?? (() => []));
        var sender = new LegSender(broker, new Modes(), new Halts(), new Open(), journal, new ExecRateLimiter(clock, 99, 99, 99), ExecEnvironment.Sandbox, clock, TimeSpan.FromSeconds(99),
            new Settings(noSettings ? null : settings ?? Defaults), tokenGate, hist);
        return (sender, broker, journal, clock, tokenGate, hist);
    }

    private static SendRequest Req(FakeTimeProvider clock, BrokerLeg? leg = null, decimal? reference = null) => new(Account, 361, leg ?? Entry, clock.GetUtcNow(), reference);

    [Fact]
    public void The_settings_must_be_given_together_with_the_gate_and_the_history_or_not_at_all()
    {
        var clock = new FakeTimeProvider();

        Should.Throw<ArgumentException>(() => new LegSender(new FakeBroker(), new Modes(), new Halts(), new Open(), new Journal(), new ExecRateLimiter(clock, 2, 30, 200), ExecEnvironment.Sandbox, clock, TimeSpan.FromSeconds(5), new Settings(Defaults)));
        Should.Throw<ArgumentException>(() => new LegSender(new FakeBroker(), new Modes(), new Halts(), new Open(), new Journal(), new ExecRateLimiter(clock, 2, 30, 200), ExecEnvironment.Sandbox, clock, TimeSpan.FromSeconds(5), null, new Token(GateDecision.Allowed)));
    }

    [Fact]
    public async Task No_settings_in_force_means_no_send_unavailable_is_never_a_pass()
    {
        var (sender, broker, journal, clock, _, _) = Make(noSettings: true);

        var result = await sender.SendAsync(Req(clock), CancellationToken.None);

        (result.Status, result.Reason).ShouldBe((SendStatus.Refused, "settings_unavailable"));
        broker.Calls.ShouldBe(0);
        journal.Begins.ShouldBe(0);
    }

    [Theory]
    [InlineData("token_unavailable")]
    [InlineData("token_expired")]
    [InlineData("token_expiring")]
    public async Task A_token_that_is_not_good_refuses_before_any_record_or_request(string reason)
    {
        var (sender, broker, journal, clock, token, _) = Make(token: new GateDecision(false, reason));

        var result = await sender.SendAsync(Req(clock), CancellationToken.None);

        (result.Status, result.Reason).ShouldBe((SendStatus.Refused, reason));
        (broker.Calls, journal.Begins, token.Margin).ShouldBe((0, 0, 20));
    }

    [Fact]
    public async Task The_collar_setting_bounds_an_exit_around_the_reference_and_an_exit_without_a_reference_is_refused()
    {
        var (sender, broker, _, clock, _, _) = Make(Defaults with { ExitCollarPct = 1.0m });

        (await sender.SendAsync(Req(clock, Exit), CancellationToken.None)).Reason.ShouldBe("no_reference_price");
        (await sender.SendAsync(Req(clock, Exit, 0m), CancellationToken.None)).Reason.ShouldBe("no_reference_price");
        (await sender.SendAsync(Req(clock, Exit with { Price = 1480m }, 1500m), CancellationToken.None)).Reason.ShouldBe("outside_collar");
        broker.Calls.ShouldBe(0);
        (await sender.SendAsync(Req(clock, Exit with { Price = 1485m }, 1500m), CancellationToken.None)).Status.ShouldBe(SendStatus.Sent);
        broker.Calls.ShouldBe(1);

        var (wide, _, _, c2, _, _) = Make(Defaults with { ExitCollarPct = 2.0m });
        (await wide.SendAsync(Req(c2, Exit with { Price = 1480m }, 1500m), CancellationToken.None)).Status.ShouldBe(SendStatus.Sent);
    }

    [Fact]
    public async Task An_entry_needs_no_reference_price_the_collar_is_for_exits_and_flattens()
    {
        var (sender, _, _, clock, _, _) = Make();

        (await sender.SendAsync(Req(clock), CancellationToken.None)).Status.ShouldBe(SendStatus.Sent);
    }

    [Fact]
    public async Task The_caps_and_the_validity_window_are_the_settings_in_force_not_the_constructor_values()
    {
        var (sender, broker, _, clock, _, _) = Make(Defaults with { PerSecond = 1, OrderValidFor = TimeSpan.FromSeconds(1) });

        (await sender.SendAsync(Req(clock), CancellationToken.None)).Status.ShouldBe(SendStatus.Sent);
        (await sender.SendAsync(Req(clock), CancellationToken.None)).Reason.ShouldBe("rate_limited");
        var old = Req(clock);
        clock.Advance(TimeSpan.FromSeconds(2));
        (await sender.SendAsync(old, CancellationToken.None)).Reason.ShouldBe("stale");
        broker.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task The_day_count_comes_from_the_history_so_a_restart_keeps_it_and_the_history_is_read_once()
    {
        var clock0 = new DateTimeOffset(2026, 10, 26, 4, 0, 0, TimeSpan.Zero);
        var (sender, broker, journal, clock, _, history) = Make(Defaults with { PerDay = 3 }, history: () => [clock0.AddHours(-3), clock0.AddHours(-2), clock0.AddHours(-1)]);

        var result = await sender.SendAsync(Req(clock), CancellationToken.None);

        (result.Status, result.Reason).ShouldBe((SendStatus.Refused, "rate_limited"));
        (await sender.SendAsync(Req(clock), CancellationToken.None)).Reason.ShouldBe("rate_limited");
        (history.Reads, broker.Calls, journal.Begins).ShouldBe((1, 0, 0));
    }

    [Fact]
    public async Task A_history_that_cannot_be_read_is_a_refusal_never_an_empty_day_and_is_tried_again()
    {
        var fail = true;
        var (sender, broker, _, clock, _, history) = Make(history: () => fail ? throw new InvalidOperationException("db down") : []);

        (await sender.SendAsync(Req(clock), CancellationToken.None)).Reason.ShouldBe("send_history_unavailable");
        broker.Calls.ShouldBe(0);
        fail = false;
        (await sender.SendAsync(Req(clock), CancellationToken.None)).Status.ShouldBe(SendStatus.Sent);
        history.Reads.ShouldBe(2);
    }

    [Fact]
    public void The_cache_serves_nothing_before_a_read_and_after_a_stale_one_and_maps_the_registry_values()
    {
        var clock = new FakeTimeProvider();
        var cache = new ExecSettingsCache(clock);
        cache.Current().ShouldBeNull();

        var values = ExecSettingRegistry.All.ToDictionary(s => s.Key, s => s.Default);
        values["exec.send_timeout_s"] = "15";
        values["exec.order_valid_for_s"] = "3";
        values["exec.exit_collar_pct"] = "0.5";
        values["exec.max_orders_per_second"] = "1";
        cache.Set(new ExecSettingsView(values, []));

        var current = cache.Current().ShouldNotBeNull();
        (current.SendTimeout, current.OrderValidFor, current.ExitCollarPct, current.PerSecond, current.PerMinute, current.PerDay, current.TokenMinRemainingMinutes)
            .ShouldBe((TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(3), 0.5m, 1, 30, 200, 20));
        clock.Advance(ExecSettingsCache.MaxAge + TimeSpan.FromSeconds(1));
        cache.Current().ShouldBeNull();
    }

    [Fact]
    public void The_limiter_seed_counts_once_and_a_restart_forgets_nothing()
    {
        var clock = new FakeTimeProvider();
        var limiter = new ExecRateLimiter(clock, 100, 100, 3);
        limiter.Seed([clock.GetUtcNow().AddHours(-1), clock.GetUtcNow().AddHours(-2)]);
        limiter.Seed([clock.GetUtcNow().AddHours(-1)]);

        limiter.TryAdmit().ShouldBeTrue();
        limiter.TryAdmit().ShouldBeFalse("two seeded plus one admitted is the day cap of three");
    }

    [Theory]
    [InlineData("Connected", 60, 20, true, "open")]
    [InlineData("Connected", 20, 20, true, "open")]
    [InlineData("Connected", 19, 20, false, "token_expiring")]
    [InlineData("Connected", 0, 20, false, "token_expired")]
    [InlineData("Connected", -5, 20, false, "token_expired")]
    [InlineData("Invalid", 600, 20, false, "token_unavailable")]
    [InlineData("NotConnected", 600, 20, false, "token_unavailable")]
    [InlineData("connected", 600, 20, false, "token_unavailable")]
    [InlineData(null, 600, 20, false, "token_unavailable")]
    public void The_token_rule(string? status, int minutesLeft, int margin, bool open, string reason)
    {
        var now = new DateTimeOffset(2026, 10, 26, 4, 0, 0, TimeSpan.Zero);

        var decision = TokenGate.Judge(new ConnectionFacts(status, now.AddMinutes(minutesLeft)), now, margin);

        (decision.Open, decision.Reason).ShouldBe((open, reason));
    }

    [Fact]
    public void No_row_and_no_expiry_are_unavailable_never_a_pass()
    {
        var now = DateTimeOffset.UnixEpoch;

        TokenGate.Judge(null, now, 20).Reason.ShouldBe("token_unavailable");
        TokenGate.Judge(new ConnectionFacts("Connected", null), now, 20).Open.ShouldBeFalse();
    }
}
