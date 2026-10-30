using Microsoft.Extensions.Time.Testing;
using XDrishti.Execution.Application;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Application.Recon;
using XDrishti.Execution.Domain;
using XDrishti.Execution.Domain.Recon;

namespace XDrishti.Execution.Tests.Recon;

/// <summary>P12-06 criteria 4, 5 and 11: the stream and the poll together, the two fill sources, the read budget, the fake-transport stream client and the pinned endpoint.</summary>
public sealed class StreamPollTests
{
    private const string Cid = ReconFixtures.Cid;
    private static readonly int[] Attempts = [0, 1, 2, 3, 4, 5, 6];
    private readonly FakeTimeProvider _clock = new(ReconFixtures.Now);
    private readonly FakeBroker _broker;
    private readonly LegBook _legs = new();
    private readonly UpdateCounters _counters = new();
    private readonly StreamStatus _status;
    private readonly UpdateApplier _applier;
    private readonly InMemoryFillWriter _fills = new();
    private readonly PollService _poll;

    public StreamPollTests()
    {
        _broker = new FakeBroker(_clock) { MaxFillPerStep = 4 };
        _status = new StreamStatus(_clock, _counters);
        _applier = new UpdateApplier(_legs, _counters, _clock);
        _poll = new PollService(_broker, _applier, _legs, _status, new ReadBudget(_clock), _fills, _clock);
    }

    private async Task PlaceEntryAsync()
    {
        (await _broker.PlaceAsync(new BrokerLeg(Cid, LegKind.Entry, ExecSide.Buy, ReconFixtures.Security, 10, 100m), CancellationToken.None)).Outcome.ShouldBe(BrokerOutcome.Acknowledged);
        _legs.Add(Cid, 361, LegKind.Entry, 10);
    }

    [Fact]
    public async Task With_the_stream_dropped_for_40_seconds_the_poll_alone_keeps_the_legs_right_and_the_streams_alive_flag_follows_the_poll()
    {
        await PlaceEntryAsync();
        (await _poll.PollOnceAsync(true, CancellationToken.None)).ShouldBe(PollOutcome.Done);
        _status.StreamsAlive.ShouldBeTrue();

        for (var second = 0; second < 40; second += 5)
        {
            _clock.Advance(TimeSpan.FromSeconds(5));
            if (second == 10)
            {
                _broker.SetPrice(ReconFixtures.Security, 99m);
            }

            (await _poll.PollOnceAsync(true, CancellationToken.None)).ShouldBe(PollOutcome.Done);
            _status.StreamsAlive.ShouldBeTrue($"second {second}");
        }

        _legs.ByClient(Cid)!.Leg.FilledQty.ShouldBe(4, "no stream message ever arrived");
        _legs.ByClient(Cid)!.Leg.Status.ShouldBe(LegStatus.PartTraded);
    }

    [Fact]
    public async Task After_a_reconnect_one_gap_fill_poll_runs_before_the_streams_are_alive_again()
    {
        await PlaceEntryAsync();
        await _poll.PollOnceAsync(true, CancellationToken.None);
        _status.StreamsAlive.ShouldBeTrue();

        _status.Connected();

        _status.StreamsAlive.ShouldBeFalse("a poll is owed after every (re)connect");
        _status.GapOwed.ShouldBeTrue();
        (await _poll.GapFillAsync(CancellationToken.None)).ShouldBe(PollOutcome.Done);
        _status.StreamsAlive.ShouldBeTrue();
    }

    [Fact]
    public async Task With_no_connection_ever_the_streams_are_alive_only_while_the_last_poll_is_at_most_15_seconds_old()
    {
        _status.StreamsAlive.ShouldBeFalse("no poll yet");
        await _poll.PollOnceAsync(false, CancellationToken.None);
        _clock.Advance(TimeSpan.FromSeconds(15));
        _status.StreamsAlive.ShouldBeTrue();
        _clock.Advance(TimeSpan.FromSeconds(1));
        _status.StreamsAlive.ShouldBeFalse();
    }

    [Fact]
    public async Task A_poll_the_broker_could_not_answer_is_not_a_poll_and_changes_nothing()
    {
        await PlaceEntryAsync();
        _broker.TokenExpired = true;

        (await _poll.PollOnceAsync(true, CancellationToken.None)).ShouldBe(PollOutcome.Unreadable);

        _status.StreamsAlive.ShouldBeFalse();
        _poll.Latest.ShouldBeNull();
        _legs.ByClient(Cid)!.Leg.Status.ShouldBe(LegStatus.New);
    }

    [Fact]
    public async Task A_stream_fill_of_4_raises_the_leg_at_once_and_the_fill_row_with_its_exchange_trade_id_comes_with_the_next_poll()
    {
        await PlaceEntryAsync();
        _applier.Apply(OrderUpdateParser.Parse(ReconFixtures.Msg("B1", Cid, "PENDING", 10, 4, "t1", avg: "100.25")).Update!);

        _legs.ByClient(Cid)!.Leg.FilledQty.ShouldBe(4);
        _legs.ByClient(Cid)!.AvgPrice.ShouldBe(100.25m);
        _fills.Rows.ShouldBeEmpty("the stream carries no trade id");

        _broker.SetPrice(ReconFixtures.Security, 99m);
        await _poll.PollOnceAsync(true, CancellationToken.None);

        var row = _fills.Rows.ShouldHaveSingleItem();
        (row.ExchangeTradeId, row.Qty, row.ClientOrderId).ShouldBe(("T1", 4, Cid));
        await _poll.PollOnceAsync(true, CancellationToken.None);
        _fills.Rows.Count.ShouldBe(1, "a second poll does not write the trade again");
    }

    [Fact]
    public void A_trade_book_fill_that_disagrees_with_the_legs_cumulative_quantity_is_a_fill_mismatch()
    {
        var input = ReconFixtures.Clean() with { Legs = [ReconFixtures.Leg(filled: 10)], Fills = [ReconFixtures.Fill(qty: 6)], BrokerTrades = [ReconFixtures.Trade(qty: 6)], BrokerPositions = [new BrokerPositionView(ReconFixtures.Security, 6)], Positions = [new OurPositionView(ReconFixtures.Security, 6)] };

        Reconciler.Compare(input).Breaks.ShouldContain(b => b.Kind == BreakKind.FillMismatch);
    }

    [Fact]
    public void The_read_budget_is_three_reads_a_second_on_average_over_ten_seconds_and_is_kept_apart_from_the_send_limiter()
    {
        var budget = new ReadBudget(_clock);

        budget.TryAdmit(30).ShouldBeTrue();
        budget.TryAdmit(1).ShouldBeFalse();
        _clock.Advance(TimeSpan.FromSeconds(10));
        budget.TryAdmit(3).ShouldBeTrue();
        typeof(ReadBudget).GetConstructors().Single().GetParameters().Select(p => p.ParameterType).ShouldNotContain(typeof(ExecRateLimiter));
    }

    [Fact]
    public async Task A_poll_that_does_not_fit_the_budget_is_throttled_not_made()
    {
        var budget = new ReadBudget(_clock);
        budget.TryAdmit(30).ShouldBeTrue();
        var poll = new PollService(_broker, _applier, _legs, _status, budget, _fills, _clock);

        (await poll.PollOnceAsync(true, CancellationToken.None)).ShouldBe(PollOutcome.Throttled);
    }

    [Theory]
    [InlineData(true, true, 5)]
    [InlineData(false, true, 30)]
    [InlineData(true, false, null)]
    public void The_poll_interval_is_busy_idle_or_none_outside_the_session(bool exposed, bool inSession, int? seconds) =>
        ReconSettings.Default.NextPoll(exposed, inSession).ShouldBe(seconds is { } s ? TimeSpan.FromSeconds(s) : null);

    [Fact]
    public void The_reconnect_backoff_doubles_to_the_cap_with_a_jitter_of_20_percent_and_out_of_range_settings_give_none()
    {
        var s = ReconSettings.Default;

        Attempts.Select(a => s.Backoff(a, 0).TotalSeconds).ShouldBe([1d, 2, 4, 8, 16, 30, 30]);
        s.Backoff(2, 1).TotalSeconds.ShouldBe(4.8, 1e-9);
        s.Backoff(2, -1).TotalSeconds.ShouldBe(3.2, 1e-9);
        ReconSettings.FromValues(new Dictionary<string, string> { ["exec.poll_busy_s"] = "1" }).ShouldBeNull();
        ReconSettings.FromValues(new Dictionary<string, string> { ["exec.recon_grace_s"] = "500" }).ShouldBeNull();
    }

    // The stream client over an in-process transport: nothing here can reach a network.
    private sealed class FakeTransport(params string?[] script) : IOrderUpdateTransport
    {
        private readonly Queue<string?> _script = new(script);

        public List<string> Sent { get; } = [];

        public Uri? Connected { get; private set; }

        public bool ThrowAtEnd { get; init; }

        public Task ConnectAsync(Uri uri, CancellationToken cancellationToken)
        {
            Connected = uri;
            return Task.CompletedTask;
        }

        public Task SendAsync(string text, CancellationToken cancellationToken)
        {
            Sent.Add(text);
            return Task.CompletedTask;
        }

        public Task<string?> ReceiveAsync(CancellationToken cancellationToken) =>
            _script.Count > 0 ? Task.FromResult(_script.Dequeue()) : ThrowAtEnd ? throw new InvalidOperationException("dropped") : Task.FromResult<string?>(null);

        public Task CloseAsync() => Task.CompletedTask;
    }

    [Fact]
    public async Task The_stream_client_logs_in_once_applies_updates_ignores_a_login_answer_and_drops_garbage_with_a_poll_request()
    {
        await PlaceEntryAsync();
        var transport = new FakeTransport("{\"Type\":\"login_response\"}", ReconFixtures.Msg("B1", Cid, "PENDING", 10, 3, "t1"), "}{ garbage", ReconFixtures.Msg("B1", Cid, "PENDING", 10, 3, "t1"));
        var feed = new OrderUpdateFeed(transport, new Uri("wss://sandbox.dhan.co"), () => OrderUpdateParser.LoginMessage("CID", "TOK"), _applier, _status, _counters);

        (await feed.RunOnceAsync(CancellationToken.None)).ShouldBe(StreamEnd.Closed);

        transport.Sent.ShouldBe([OrderUpdateParser.LoginMessage("CID", "TOK")]);
        transport.Connected.ShouldBe(new Uri("wss://sandbox.dhan.co"));
        _legs.ByClient(Cid)!.Leg.FilledQty.ShouldBe(3);
        (_counters.Applied, _counters.Dropped).ShouldBe((1, 1));
        _status.PollRequested.ShouldBeTrue("a dropped message asks for a poll");
        _status.View().Connected.ShouldBeFalse();
        _status.View().LastMessageAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task A_connection_that_drops_ends_the_run_the_next_connect_owes_a_gap_fill_and_silence_is_not_an_error()
    {
        await _poll.PollOnceAsync(false, CancellationToken.None);
        var feed = new OrderUpdateFeed(new FakeTransport { ThrowAtEnd = true }, new Uri("wss://sandbox.dhan.co"), () => "login", _applier, _status, _counters);

        (await feed.RunOnceAsync(CancellationToken.None)).ShouldBe(StreamEnd.Closed);

        _status.GapOwed.ShouldBeTrue();
        _status.StreamsAlive.ShouldBeFalse();
        _status.View().Reconnects24h.ShouldBe(1);
    }

    [Fact]
    public async Task Without_an_endpoint_the_client_connects_to_nothing()
    {
        var transport = new FakeTransport();
        var feed = new OrderUpdateFeed(transport, null, () => "login", _applier, _status, _counters);

        (await feed.RunOnceAsync(CancellationToken.None)).ShouldBe(StreamEnd.NoEndpoint);

        transport.Connected.ShouldBeNull();
        transport.Sent.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(ExecEnvironment.Live, "sandbox.dhan.co", null)]
    [InlineData(ExecEnvironment.None, "sandbox.dhan.co", null)]
    [InlineData(ExecEnvironment.Sandbox, null, null)]
    [InlineData(ExecEnvironment.Sandbox, "", null)]
    [InlineData(ExecEnvironment.Sandbox, "api-order-update.dhan.co", null)]
    [InlineData(ExecEnvironment.Sandbox, "sandbox.evil.com", null)]
    [InlineData(ExecEnvironment.Sandbox, "sandbox.dhan.co.evil.com", null)]
    [InlineData(ExecEnvironment.Sandbox, "sandbox-live.dhan.co", null)]
    [InlineData(ExecEnvironment.Sandbox, "sandbox.dhan.co:443", null)]
    [InlineData(ExecEnvironment.Sandbox, "user@sandbox.dhan.co", null)]
    [InlineData(ExecEnvironment.Sandbox, "sandbox.dhan.co/path", null)]
    [InlineData(ExecEnvironment.Sandbox, "sandbox.dhan.co.", null)]
    [InlineData(ExecEnvironment.Sandbox, " sandbox.dhan.co", null)]
    [InlineData(ExecEnvironment.Sandbox, "sandbox.dhan.co", "wss://sandbox.dhan.co/")]
    [InlineData(ExecEnvironment.Sandbox, "SANDBOX.Dhan.CO", "wss://sandbox.dhan.co/")]
    public void The_socket_endpoint_is_never_the_live_host_and_exists_only_for_the_sandbox_with_an_explicit_host(ExecEnvironment environment, string? host, string? expected) =>
        StreamEndpoint.Resolve(environment, host)?.ToString().ShouldBe(expected);

    [Fact]
    public void The_stream_status_view_carries_the_fields_of_the_endpoint()
    {
        var v = _status.View();

        (v.Connected, v.LastMessageAt, v.LastPollAt, v.Reconnects24h, v.Dropped, v.Applied, v.Ignored, v.StreamsAlive).ShouldBe((false, null, null, 0, 0L, 0L, 0L, false));
    }
}
