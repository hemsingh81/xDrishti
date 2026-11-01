using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using XDrishti.Application.Abstractions.OptionChain;
using XDrishti.Application.OptionChain;
using XDrishti.Application.Tests.Platform.Services;
using static XDrishti.Application.Tests.OptionChain.ChainSamples;

namespace XDrishti.Application.Tests.OptionChain;

/// <summary>
/// The option-chain session over fakes (P14-02 criteria 3 to 6 and 8, a Monday, 2026-10-12): the expiry lists from 09:15, the slots of the grid, the failure outcomes, the spacing that doubles, the rate-limit pause, the overrun, the stock pass
/// between the index slots and its end, the disk guard and the end words. Time is the fake clock; the test moves it to each instant and waits for the session to settle.
/// </summary>
public sealed class OptionChainRecorderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class World : IDisposable
    {
        private readonly CancellationTokenSource _stop = new();

        public World(int hour, int minute, int second = 0, IReadOnlyList<ChainTarget>? stocks = null, OptionChainSettings? settings = null, ILogger<OptionChainRecorder>? logger = null)
        {
            Clock = new SettableClock(At(12, hour, minute, second));
            Client = new FakeChainClient(Clock);
            Settings = settings ?? OptionChainSettings.Off with { Enabled = true };
            Day = new ChainDay(new DateOnly(2026, 10, 12), [FakeChainTargets.Nifty, FakeChainTargets.BankNifty], stocks ?? [FakeChainTargets.Reliance]);
            var provider = new MiniProvider().ScopedInstance<IOptionChainClient>(Client).ScopedInstance<IOptionChainStore>(Store);
            Recorder = new OptionChainRecorder(provider, Settings, Clock, logger ?? NullLogger<OptionChainRecorder>.Instance);
        }

        public SettableClock Clock { get; }

        public FakeChainClient Client { get; }

        public FakeChainStore Store { get; } = new();

        public OptionChainSettings Settings { get; }

        public ChainDay Day { get; }

        public OptionChainRecorder Recorder { get; }

        public Task<ChainSessionResult>? Task { get; private set; }

        public void Start() => Task = Recorder.RunAsync(Day, _stop.Token);

        public async Task AdvanceToAsync(DateTimeOffset instant)
        {
            var wait = instant - Clock.GetUtcNow();
            if (wait > TimeSpan.Zero)
            {
                Clock.Advance(wait);
            }

            await SettleAsync();
        }

        public async Task SettleAsync()
        {
            int last = -1, stable = 0;
            while (stable < 4 && Task is { IsCompleted: false })
            {
                await System.Threading.Tasks.Task.Delay(5, Ct);
                var now = Client.Activity + Store.Activity;
                stable = now == last ? stable + 1 : 0;
                last = now;
            }
        }

        public async Task<ChainSessionResult> StopAsync()
        {
            await _stop.CancelAsync();
            return await Task!.WaitAsync(TimeSpan.FromSeconds(30), Ct);
        }

        /// <summary>
        /// The result of a session that is expected to end by itself. Bounded: a wake-up timer that was registered just after the fake clock moved would otherwise wait for ever, so the clock is nudged forward in small steps
        /// while the session has not ended, and the wait gives up (and fails the test) after 30 seconds of real time.
        /// </summary>
        public async Task<ChainSessionResult> ResultAsync()
        {
            for (var i = 0; i < 300 && Task is { IsCompleted: false }; i++)
            {
                await System.Threading.Tasks.Task.Delay(10, Ct);
                if (!Task.IsCompleted)
                {
                    Clock.Advance(TimeSpan.FromMilliseconds(500));
                }
            }

            return await Task!.WaitAsync(TimeSpan.FromSeconds(30), Ct);
        }

        public void Dispose() => _stop.Dispose();

        public IEnumerable<(ChainRequest Request, TimeSpan Spacing, DateTimeOffset At)> Chains => Client.Calls.Where(c => c.Request.Kind == ChainKind.Chain);
    }

    [Fact]
    public async Task A_slot_asks_each_index_expiry_once_in_order_and_stamps_the_rows_with_the_slot()
    {
        using var world = new World(9, 14, 50);
        world.Start();

        await world.AdvanceToAsync(At(12, 9, 15));
        world.Client.Calls.Select(c => (c.Request.Kind, c.Request.UnderlyingScrip, c.Request.Segment)).ShouldBe([(ChainKind.Expiries, 13L, "IDX_I"), (ChainKind.Expiries, 25L, "IDX_I"), (ChainKind.Expiries, 2885L, "NSE_EQ")], "the lists are asked once per underlying at 09:15");
        await world.AdvanceToAsync(At(12, 9, 20));
        var result = await world.StopAsync();

        world.Chains.Select(c => (c.Request.UnderlyingScrip, c.Request.Expiry)).ShouldBe(
        [
            (13L, new DateOnly(2026, 10, 13)), (13L, new DateOnly(2026, 10, 20)), (13L, new DateOnly(2026, 10, 27)), (13L, new DateOnly(2026, 11, 24)),
            (25L, new DateOnly(2026, 10, 27)), (25L, new DateOnly(2026, 11, 24)),
        ]);
        world.Client.Calls.ShouldAllBe(c => c.Spacing == TimeSpan.FromSeconds(3));
        world.Store.Rows.Count.ShouldBe(6 * 42);
        world.Store.Rows.Values.ShouldAllBe(r => r.Ts == At(12, 9, 20));
        world.Store.Requests.Count(r => r.Kind == "chain").ShouldBe(6);
        world.Store.Requests.Where(r => r.Kind == "chain").ShouldAllBe(r => r.Outcome == "ok" && r.Rows == 42 && r.Slot == At(12, 9, 20));
        result.End.ShouldBe(ChainSessionEnd.Stopped);
        result.Stats.ShouldBe(new ChainRunStats(1, 9, 9, 0, 6 * 42));
    }

    [Fact]
    public async Task A_failed_request_writes_a_request_row_and_no_snapshot_row_and_nothing_is_zero()
    {
        using var world = new World(9, 19, 50);
        world.Client.Responder = (r, _) => r.Kind == ChainKind.Chain && r.UnderlyingScrip == 13 && r.Expiry == new DateOnly(2026, 10, 20) ? FakeChainClient.Failed(ChainOutcomes.ServerError, 503) : null;
        world.Start();

        await world.AdvanceToAsync(At(12, 9, 20));
        var result = await world.StopAsync();

        var failed = world.Store.Requests.Single(r => r.Outcome == "server_error");
        (failed.Kind, failed.Expiry, failed.HttpStatus, failed.Rows).ShouldBe(("chain", new DateOnly(2026, 10, 20), (int?)503, 0));
        world.Store.Rows.Keys.ShouldNotContain(k => k.Item1 == At(12, 9, 20) && k.Item3 == new DateOnly(2026, 10, 20) && k.Item2 == FakeChainTargets.Nifty.InstrumentId);
        world.Store.Rows.Count.ShouldBe(5 * 42);
        world.Store.Rows.Values.ShouldAllBe(r => r.LastPx != 0 && r.BidPx != 0 && r.AskPx != 0);
        result.Stats.Gaps.ShouldBe(1);
    }

    [Theory]
    [InlineData(ChainOutcomes.Timeout, null, null)]
    [InlineData(ChainOutcomes.NoData, 400, "DH-907")]
    [InlineData(ChainOutcomes.RateLimited, 429, "DH-904")]
    public async Task Every_failure_outcome_is_counted_as_a_gap_with_its_status_and_code(string outcome, int? status, string? code)
    {
        using var world = new World(9, 19, 50);
        world.Client.Responder = (r, _) => r.Kind == ChainKind.Chain ? FakeChainClient.Failed(outcome, status, code) : null;
        world.Start();

        await world.AdvanceToAsync(At(12, 9, 20));
        await world.StopAsync();

        world.Store.Requests.Where(r => r.Kind == "chain").ShouldAllBe(r => r.Outcome == outcome && r.HttpStatus == status && r.DhanCode == code);
        world.Store.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_body_that_is_not_the_documented_shape_is_a_bad_body_gap()
    {
        using var world = new World(9, 19, 50);
        world.Client.Responder = (r, _) => r.Kind == ChainKind.Chain ? FakeChainClient.Ok("{\"nope\":1}"u8.ToArray()) : null;
        world.Start();

        await world.AdvanceToAsync(At(12, 9, 20));
        await world.StopAsync();

        world.Store.Requests.Where(r => r.Kind == "chain").ShouldAllBe(r => r.Outcome == "bad_body");
        world.Store.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task After_a_rate_limit_the_spacing_doubles_for_the_rest_of_the_slot_and_is_normal_again_in_the_next()
    {
        using var world = new World(9, 19, 50);
        world.Client.Responder = (r, _) => r.Kind == ChainKind.Chain && r.UnderlyingScrip == 13 && r.Expiry == new DateOnly(2026, 10, 20) ? FakeChainClient.Failed(ChainOutcomes.RateLimited, 429, "DH-904") : null;
        world.Start();

        await world.AdvanceToAsync(At(12, 9, 20));
        var first = world.Chains.Select(c => c.Spacing.TotalSeconds).ToList();
        await world.AdvanceToAsync(At(12, 9, 25));
        var all = world.Chains.Select(c => c.Spacing.TotalSeconds).ToList();
        await world.StopAsync();

        first.ShouldBe([3, 3, 6, 6, 6, 6], "the request that met the limit went out at 3 s; every later one of the slot at 6 s");
        all.Skip(6).Take(6).ShouldBe([3, 3, 6, 6, 6, 6], "the 09:25 slot starts at 3 s again (and meets the same scripted limit)");
    }

    [Fact]
    public async Task Three_slots_in_a_row_with_a_rate_limit_pause_the_recorder_and_nothing_more_is_sent()
    {
        using var world = new World(9, 19, 50);
        world.Client.Responder = (r, _) => r.Kind == ChainKind.Chain ? FakeChainClient.Failed(ChainOutcomes.RateLimited, 429, "DH-904") : null;
        world.Start();

        await world.AdvanceToAsync(At(12, 9, 20));
        await world.AdvanceToAsync(At(12, 9, 25));
        world.Task!.IsCompleted.ShouldBeFalse("two slots are not three");
        await world.AdvanceToAsync(At(12, 9, 30));

        var result = await world.ResultAsync();
        var calls = world.Client.Calls.Count;
        await world.AdvanceToAsync(At(12, 9, 40));
        result.End.ShouldBe(ChainSessionEnd.RatePaused);
        world.Client.Calls.Count.ShouldBe(calls);
        world.Recorder.State.ShouldBe(ChainStates.RatePaused);
        OptionChainRecorder.RatePause.ShouldBe(TimeSpan.FromMinutes(15));
    }

    [Fact]
    public async Task A_clean_slot_between_rate_limited_ones_resets_the_count()
    {
        using var world = new World(9, 19, 50);
        world.Client.Responder = (r, _) => r.Kind == ChainKind.Chain && world.Clock.GetUtcNow().ToOffset(Ist).Minute is 20 or 25 or 35 ? FakeChainClient.Failed(ChainOutcomes.RateLimited, 429, "DH-904") : null;
        world.Start();

        foreach (var minute in new[] { 20, 25, 30, 35, 40 })
        {
            await world.AdvanceToAsync(At(12, 9, minute));
        }

        world.Task!.IsCompleted.ShouldBeFalse();
        (await world.StopAsync()).End.ShouldBe(ChainSessionEnd.Stopped);
    }

    [Fact]
    public async Task A_refused_token_ends_the_session_at_once_and_sends_nothing_more()
    {
        using var world = new World(9, 19, 50);
        world.Client.Responder = (r, _) => r.Kind == ChainKind.Chain ? FakeChainClient.Failed(ChainOutcomes.TokenRefused, 401, "DH-901") : null;
        world.Start();

        await world.AdvanceToAsync(At(12, 9, 20));
        var result = await world.ResultAsync();

        result.End.ShouldBe(ChainSessionEnd.TokenRefused);
        world.Chains.Count().ShouldBe(1);
        world.Store.Requests.Last().Outcome.ShouldBe("token_refused");
    }

    [Fact]
    public async Task No_credentials_at_all_ends_the_session_as_no_token()
    {
        using var world = new World(9, 14, 50);
        world.Client.Responder = (_, _) => FakeChainClient.Failed(ChainOutcomes.TokenRefused, noCredentials: true);
        world.Start();

        await world.AdvanceToAsync(At(12, 9, 15));

        (await world.ResultAsync()).End.ShouldBe(ChainSessionEnd.NoToken);
        world.Client.Calls.Count.ShouldBe(1);
    }

    [Theory]
    [InlineData("DH-902", 403)]
    [InlineData("806", 400)]
    public async Task Not_subscribed_ends_the_session_for_the_day(string code, int status)
    {
        using var world = new World(9, 14, 50);
        world.Client.Responder = (_, _) => FakeChainClient.Failed(ChainOutcomes.NotSubscribed, status, code);
        world.Start();

        await world.AdvanceToAsync(At(12, 9, 15));

        (await world.ResultAsync()).End.ShouldBe(ChainSessionEnd.NotSubscribed);
        world.Client.Calls.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_slot_still_running_when_the_next_starts_is_closed_and_its_missing_requests_are_counted_as_overrun()
    {
        using var world = new World(9, 14, 50);
        world.Client.Responder = (_, n) =>
        {
            if (n == 3)
            {
                world.Client.Latency = TimeSpan.FromSeconds(100); // Dhan is slow from the last list on
            }

            if (n == 7)
            {
                world.Client.Latency = TimeSpan.Zero; // the 09:25 slot is quick again
            }

            return null;
        };
        world.Start();

        await world.AdvanceToAsync(At(12, 9, 15));
        await world.AdvanceToAsync(At(12, 9, 20));
        var result = await world.StopAsync();

        var slot = At(12, 9, 20);
        world.Store.Requests.Where(r => r.Slot == slot && r.Outcome == "ok" && r.Kind == "chain").Count().ShouldBe(3);
        world.Store.Requests.Where(r => r.Slot == slot && r.Outcome == "slot_overrun").Select(r => r.Kind).ShouldBe(["chain", "chain", "chain"]);
        world.Chains.Count(c => c.At < At(12, 9, 25)).ShouldBe(3, "nothing of the 09:20 slot is sent after 09:25");
        result.Stats.Gaps.ShouldBeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task A_slot_that_is_already_over_is_not_caught_up()
    {
        using var world = new World(9, 30, 10);
        world.Start();

        await world.AdvanceToAsync(At(12, 9, 30, 11));
        await world.StopAsync();

        world.Store.Requests.ShouldNotBeEmpty();
        world.Store.Requests.ShouldAllBe(r => r.Slot == At(12, 9, 30), "the slots of 09:20 and 09:25 were missed and stay missed");
        world.Store.Requests.Where(r => r.Kind == "expiries").Count().ShouldBe(2, "the index lists were missing at the slot and are asked first");
    }

    [Fact]
    public async Task The_stock_pass_starts_at_14_30_between_the_index_slots_with_the_stock_strike_window()
    {
        using var world = new World(14, 29, 50);
        world.Start();

        await world.AdvanceToAsync(At(12, 14, 30));
        await world.StopAsync();

        var stock = world.Chains.Where(c => c.Request.UnderlyingScrip == 2885).ToList();
        stock.Select(c => (c.Request.Segment, c.Request.Expiry)).ShouldBe([("NSE_EQ", new DateOnly(2026, 10, 13)), ("NSE_EQ", new DateOnly(2026, 10, 20))], "two expiries of the stock");
        world.Chains.Take(6).ShouldAllBe(c => c.Request.UnderlyingScrip != 2885, "the index slot goes first");
        world.Store.Rows.Values.Where(r => r.UnderlyingId == FakeChainTargets.Reliance.InstrumentId).ShouldAllBe(r => r.Ts == At(12, 14, 30));
        world.Store.Rows.Values.Count(r => r.UnderlyingId == FakeChainTargets.Reliance.InstrumentId).ShouldBe(2 * 16 * 2, "15 strikes either side of the nearest, at the edge of the ladder: 16 strikes, 2 sides, 2 expiries");
    }

    [Fact]
    public async Task What_the_stock_pass_has_not_asked_by_15_25_is_a_window_end_gap_and_nothing_is_sent()
    {
        var stocks = new[] { FakeChainTargets.Reliance, FakeChainTargets.Reliance with { InstrumentId = Guid.NewGuid(), Scrip = 1 }, FakeChainTargets.Reliance with { InstrumentId = Guid.NewGuid(), Scrip = 2 } };
        using var world = new World(15, 25, 0, stocks);
        world.Start();

        await world.AdvanceToAsync(At(12, 15, 25, 1));
        await world.StopAsync();

        world.Store.Requests.Where(r => r.Outcome == "window_end").Count().ShouldBe(3);
        world.Client.Calls.Where(c => c.Request.UnderlyingScrip is 2885 or 1 or 2).ShouldBeEmpty();
        world.Chains.Count().ShouldBe(6, "the index requests of the 15:25 slot are made as usual");
    }

    [Fact]
    public async Task A_stored_size_over_the_own_cap_stops_the_session_before_the_slot_and_the_planned_requests_are_disk_stop_gaps()
    {
        using var world = new World(9, 14, 50);
        world.Store.Stored = world.Settings.MaxStoredBytes + 1;
        world.Start();

        await world.AdvanceToAsync(At(12, 9, 20));
        var result = await world.ResultAsync();

        result.End.ShouldBe(ChainSessionEnd.DiskBudget);
        world.Chains.ShouldBeEmpty();
        world.Store.Requests.Count(r => r.Outcome == "disk_stop").ShouldBe(6);
        world.Recorder.State.ShouldBe("disk_budget");
    }

    [Fact]
    public async Task Free_space_under_the_floor_stops_the_session()
    {
        using var world = new World(9, 14, 50);
        world.Store.Free = world.Settings.MinFreeBytes - 1;
        world.Start();

        await world.AdvanceToAsync(At(12, 9, 20));

        (await world.ResultAsync()).End.ShouldBe(ChainSessionEnd.DiskFree);
        world.Chains.ShouldBeEmpty();
    }

    [Fact]
    public async Task At_80_percent_of_the_cap_it_warns_and_keeps_recording_and_an_unreadable_free_space_leaves_the_size_cap_in_force()
    {
        using var world = new World(9, 14, 50);
        world.Store.Stored = (long)(world.Settings.MaxStoredBytes * 0.8);
        world.Store.Free = null;
        world.Start();

        await world.AdvanceToAsync(At(12, 9, 20));
        var result = await world.StopAsync();

        result.End.ShouldBe(ChainSessionEnd.Stopped);
        world.Chains.Count().ShouldBe(6);
        world.Recorder.FreeSpaceReadable.ShouldBeFalse();
    }

    [Fact]
    public async Task A_store_that_fails_for_one_request_does_not_end_the_session()
    {
        using var world = new World(9, 19, 50);
        world.Store.FailAppends = 1;
        world.Start();

        await world.AdvanceToAsync(At(12, 9, 20));
        var result = await world.StopAsync();

        result.End.ShouldBe(ChainSessionEnd.Stopped);
        world.Chains.Count().ShouldBe(6);
        result.Stats.Gaps.ShouldBe(1, "a request whose rows could not be stored is a gap, not an ok one");
        result.Stats.Ok.ShouldBe(result.Stats.Requests - 1);
    }

    [Fact]
    public async Task Slots_missed_after_the_session_started_are_counted_as_slot_overrun_gaps_for_the_planned_pairs()
    {
        using var world = new World(9, 14, 50);
        world.Start();
        await world.AdvanceToAsync(At(12, 9, 15));

        await world.AdvanceToAsync(At(12, 9, 31)); // the machine slept through 09:20 and 09:25
        await world.StopAsync();

        foreach (var minute in new[] { 20, 25 })
        {
            world.Store.Requests.Where(r => r.Slot == At(12, 9, minute) && r.Outcome == "slot_overrun").Count().ShouldBe(6, $"the 09:{minute} slot");
        }

        world.Store.Requests.Where(r => r.Slot == At(12, 9, 30) && r.Outcome == "ok" && r.Kind == "chain").Count().ShouldBe(6, "the slot in force is recorded as usual");
    }

    [Fact]
    public async Task The_log_carries_counts_ids_and_outcome_words_only_never_a_url_a_token_or_a_body()
    {
        var log = new CapturingLogger();
        using var world = new World(9, 14, 50, logger: log);
        world.Client.Responder = (r, n) => r.Kind == ChainKind.Chain ? (n % 2 == 0 ? FakeChainClient.Failed(ChainOutcomes.RateLimited, 429, "DH-904") : FakeChainClient.Ok("{\"secret\":\"BODY-MARKER https://api.dhan.co/v2/optionchain access-token\"}"u8.ToArray())) : null;
        world.Start();

        await world.AdvanceToAsync(At(12, 9, 15));
        await world.AdvanceToAsync(At(12, 9, 20));
        await world.StopAsync();

        log.Messages.ShouldContain(m => m.StartsWith("options.chain.request", StringComparison.Ordinal));
        log.Messages.ShouldContain(m => m.StartsWith("options.chain.gap kind=chain outcome=rate_limited code=DH-904", StringComparison.Ordinal));
        log.Messages.ShouldContain(m => m.StartsWith("options.chain.gap kind=chain outcome=bad_body", StringComparison.Ordinal));
        log.Messages.ShouldAllBe(m => m.StartsWith("options.chain.", StringComparison.Ordinal));
        log.Messages.ShouldAllBe(m => !m.Contains("http", StringComparison.OrdinalIgnoreCase) && !m.Contains("dhan.co", StringComparison.OrdinalIgnoreCase) && !m.Contains("access-token", StringComparison.OrdinalIgnoreCase) && !m.Contains("BODY-MARKER", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_last_slot_of_the_day_completes_the_session()
    {
        using var world = new World(15, 29, 50);
        world.Start();

        await world.AdvanceToAsync(At(12, 15, 30));
        await world.AdvanceToAsync(At(12, 15, 36));

        (await world.ResultAsync()).End.ShouldBe(ChainSessionEnd.Completed);
        world.Recorder.State.ShouldBe(ChainStates.DayDone);
    }

    private sealed class CapturingLogger : ILogger<OptionChainRecorder>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => NullLogger.Instance.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
