using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using XDrishti.Application.Abstractions.OptionBackfill;
using XDrishti.Application.OptionBackfill;
using XDrishti.Application.Recording;
using XDrishti.Application.Tests.Platform.Services;
using static XDrishti.Application.Tests.OptionBackfill.BackfillSamples;

namespace XDrishti.Application.Tests.OptionBackfill;

/// <summary>
/// The backfill job over fakes (P14-03 criteria 3, 4, 5, 7; a Monday evening, 2026-10-12): resume without a second fetch, three failed attempts and given up, the failure outcomes, the rate-limit pause and the halved rate, the daily cap, the night window,
/// the disk guard. Time is the fake clock; every wait is bounded (the clock is moved when a timer of the expected length exists, never by sleeping).
/// </summary>
public sealed class OptionBackfillJobTests
{
    /// <summary>The keys of the small plan on 2026-10-12: 3 series x 2 sides x 12 one-year windows.</summary>
    private const int Keys = 72;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class World
    {
        public World(int hour = 20, int minute = 0, OptionBackfillSettings? settings = null, ILogger<OptionBackfillJob>? logger = null, params BackfillTarget[] targets)
        {
            Clock = new SettableClock(At(12, hour, minute));
            Client = new FakeBackfillClient(Clock);
            Settings = settings ?? Small;
            Targets = targets.Length == 0 ? [Nifty] : targets;
            var provider = new MiniProvider().ScopedInstance<IOptionBackfillClient>(Client).ScopedInstance<IRollingBarStore>(Store).ScopedInstance<IBackfillFetchLog>(Store);
            Job = new OptionBackfillJob(provider, Settings, Clock, logger ?? NullLogger<OptionBackfillJob>.Instance);
        }

        public SettableClock Clock { get; }

        public FakeBackfillClient Client { get; }

        public FakeBackfillStore Store { get; } = new();

        public OptionBackfillSettings Settings { get; }

        public BackfillTarget[] Targets { get; }

        public OptionBackfillJob Job { get; }

        public int Planned => Keys(Settings, Targets);

        public Task<BackfillSessionResult> RunAsync(CancellationToken? token = null) => Job.RunAsync(Targets, token ?? Ct);
    }

    [Fact]
    public async Task A_full_night_fetches_every_key_once_in_plan_order_and_logs_one_row_per_attempt()
    {
        var world = new World();

        var result = await world.RunAsync();

        result.End.ShouldBe(BackfillSessionEnd.Completed);
        world.Planned.ShouldBe(Keys);
        world.Client.Calls.Count.ShouldBe(Keys);
        world.Client.Calls.Select(c => c.Request.From).Distinct().Count().ShouldBe(Keys / 6);
        world.Client.Calls.Take(3).Select(c => (c.Request.ExpiryFlag, c.Request.ExpiryCode, c.Request.Side)).ShouldBe([('W', 1, "CE"), ('W', 1, "CE"), ('W', 1, "CE")]);
        world.Store.Log.Count.ShouldBe(Keys);
        world.Store.Log.ShouldAllBe(r => r.Outcome == BackfillOutcomes.Ok && r.Attempt == 1 && r.Bars == 3);
        world.Store.Bars.Count.ShouldBe(Keys * 3, "the three bars of every key, nothing twice");
        result.Stats.ShouldSatisfyAllConditions(s => s.Requests.ShouldBe(Keys), s => s.Ok.ShouldBe(Keys), s => s.Gaps.ShouldBe(0), s => s.Bars.ShouldBe(Keys * 3L));
        world.Job.State.ShouldBe(BackfillStates.Done);
    }

    [Fact]
    public async Task A_restart_after_a_crash_at_key_k_fetches_only_the_keys_without_a_finished_attempt()
    {
        var world = new World();
        using var stop = new CancellationTokenSource();
        world.Client.Responder = (_, call) =>
        {
            if (call == 30)
            {
                stop.Cancel(); // the process is stopped during the 30th request
            }

            return null;
        };

        var first = await world.RunAsync(stop.Token);
        first.End.ShouldBe(BackfillSessionEnd.Stopped);
        var written = world.Store.Log.Count;
        written.ShouldBe(30, "the 30th answer was written before the cancellation was seen");

        world.Client.Responder = null;
        world.Client.Calls.Clear();
        var second = await world.RunAsync();

        second.End.ShouldBe(BackfillSessionEnd.Completed);
        world.Client.Calls.Count.ShouldBe(Keys - written, "only the keys after k are fetched");
        world.Client.Calls.Select(c => c.Request).Distinct().Count().ShouldBe(world.Client.Calls.Count);
        world.Store.Log.GroupBy(r => r.Key).ShouldAllBe(g => g.Count() == 1, "a done key is never fetched again");
        world.Store.Log.Count.ShouldBe(Keys);
    }

    [Fact]
    public async Task A_third_run_over_a_finished_log_sends_nothing()
    {
        var world = new World();
        await world.RunAsync();
        world.Client.Calls.Clear();

        var again = await world.RunAsync();

        again.End.ShouldBe(BackfillSessionEnd.Completed);
        world.Client.Calls.ShouldBeEmpty();
        again.Stats.Requests.ShouldBe(0);
    }

    [Fact]
    public async Task An_empty_answer_and_a_dh907_are_done_and_never_asked_again()
    {
        var world = new World();
        world.Client.Responder = (request, call) => call switch
        {
            1 => FakeBackfillClient.Ok(Text("""{"data":{"ce":null,"pe":null}}""")),
            2 => FakeBackfillClient.Failed(BackfillOutcomes.NoData, 400, "DH-907"),
            _ => null,
        };

        await world.RunAsync();

        world.Store.Log.Take(2).Select(r => r.Outcome).ShouldBe([BackfillOutcomes.Empty, BackfillOutcomes.NoData]);
        world.Store.Log.Take(2).Select(r => r.Bars).ShouldBe([0, 0]);
        world.Store.Bars.Count.ShouldBe((Keys - 2) * 3);
        world.Client.Calls.Count.ShouldBe(Keys, "each key once");
        var state = await world.Store.LoadStateAsync(Ct);
        state.Values.Count(s => s.DoneOutcome == BackfillOutcomes.Empty).ShouldBe(1);
        state.Values.Count(s => s.DoneOutcome == BackfillOutcomes.NoData).ShouldBe(1);
    }

    [Theory]
    [InlineData(BackfillOutcomes.Timeout, null, null)]
    [InlineData(BackfillOutcomes.ServerError, 500, null)]
    [InlineData(BackfillOutcomes.ServerError, 400, "DH-909")]
    public async Task A_key_that_fails_three_times_is_tried_three_times_then_given_up_and_listed_and_never_tried_again(string outcome, int? status, string? code)
    {
        var world = new World();
        var firstKey = BackfillPlanner.Plan(world.Targets, world.Settings, new DateOnly(2026, 10, 12)).First().Key;
        world.Client.Responder = (request, _) => request.From == firstKey.From && request.ExpiryFlag == 'W' && request.ExpiryCode == 1 && request.Side == "CE" ? FakeBackfillClient.Failed(outcome, status, code) : null;

        var result = await world.RunAsync();

        world.Store.Log.Where(r => r.Key == firstKey).Select(r => (r.Attempt, r.Outcome)).ShouldBe([(1, outcome), (2, outcome), (3, outcome)]);
        result.Stats.ShouldSatisfyAllConditions(s => s.GivenUp.ShouldBe(1), s => s.Gaps.ShouldBe(3));
        var state = (await world.Store.LoadStateAsync(Ct))[firstKey];
        (state.IsGivenUp, state.IsDone, state.Failed).ShouldBe((true, false, 3));

        world.Client.Responder = null;
        world.Client.Calls.Clear();
        await world.RunAsync();
        world.Client.Calls.ShouldBeEmpty("a given-up key is left and listed, not fetched again");
        (await new BackfillProgressReader(new FakeBackfillTargets().AsOnly(Nifty), world.Store, world.Settings, world.Clock).ReadAsync(Ct)).GivenUp.ShouldBe(1);
    }

    [Fact]
    public async Task An_unreadable_body_is_a_bad_body_attempt_and_a_second_try_can_succeed()
    {
        var world = new World();
        world.Client.Responder = (_, call) => call == 1 ? FakeBackfillClient.Ok(Text("{\"data\":3}")) : null;

        await world.RunAsync();

        world.Store.Log.Take(2).Select(r => (r.Attempt, r.Outcome)).ShouldBe([(1, BackfillOutcomes.BadBody), (2, BackfillOutcomes.Ok)]);
        world.Store.Log[0].Bars.ShouldBe(0);
    }

    [Fact]
    public async Task A_rate_limit_pauses_sixty_seconds_and_doubles_the_spacing_for_the_rest_of_the_night()
    {
        var world = new World();
        world.Client.Responder = (_, call) => call == 3 ? FakeBackfillClient.Failed(BackfillOutcomes.RateLimited, 429, "DH-904") : null;

        var run = world.RunAsync();
        await world.Clock.WhenWaitingForAsync(OptionBackfillJob.RatePause).WaitAsync(TimeSpan.FromSeconds(10), Ct);
        world.Client.Calls.Count.ShouldBe(3, "nothing is sent during the pause");
        world.Clock.Advance(OptionBackfillJob.RatePause);
        var result = await run.WaitAsync(TimeSpan.FromSeconds(30), Ct);

        result.End.ShouldBe(BackfillSessionEnd.Completed);
        world.Client.Calls.Take(3).ShouldAllBe(c => c.Spacing == TimeSpan.FromSeconds(1));
        world.Client.Calls.Skip(3).ShouldAllBe(c => c.Spacing == TimeSpan.FromSeconds(2), "the rate is halved for the rest of the night");
        world.Client.Calls[3].Request.ShouldBe(world.Client.Calls[2].Request, "the limited key is tried again after the pause");
        world.Store.Log[2].Outcome.ShouldBe(BackfillOutcomes.RateLimited);
    }

    [Fact]
    public async Task The_spacing_asked_for_is_one_over_the_rate_and_never_under_half_a_second()
    {
        var world = new World(settings: Small with { RequestsPerSecond = 2 });
        await world.RunAsync();
        world.Client.Calls.ShouldAllBe(c => c.Spacing == TimeSpan.FromMilliseconds(500));

        var slow = new World(settings: Small with { RequestsPerSecond = 0.2 });
        await slow.RunAsync();
        slow.Client.Calls.ShouldAllBe(c => c.Spacing == TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData("DH-901", 401, "TokenRefused")]
    [InlineData("807", 400, "TokenRefused")]
    [InlineData("DH-902", 403, "NotSubscribed")]
    [InlineData("806", 400, "NotSubscribed")]
    public async Task A_refused_token_or_a_missing_plan_ends_the_run_without_counting_against_the_key(string code, int status, string expectedEnd)
    {
        var world = new World();
        var outcome = BackfillOutcomeMap.From(status, code);
        world.Client.Responder = (_, call) => call == 5 ? FakeBackfillClient.Failed(outcome, status, code) : null;

        var result = await world.RunAsync();

        result.End.ToString().ShouldBe(expectedEnd);
        world.Client.Calls.Count.ShouldBe(5);
        var key = world.Store.Log[^1].Key;
        (await world.Store.LoadStateAsync(Ct))[key].ShouldSatisfyAllConditions(s => s.Failed.ShouldBe(0), s => s.IsGivenUp.ShouldBeFalse(), s => s.IsDone.ShouldBeFalse());
        world.Client.Responder = null;
        world.Client.Calls.Clear();
        await world.RunAsync();
        world.Client.Calls.Count.ShouldBe(Keys - 4, "the refused key is fetched again with the next token, the four done keys are not");
    }

    [Fact]
    public async Task No_credentials_end_the_run_as_no_token_and_write_no_row()
    {
        var world = new World();
        world.Client.Responder = (_, _) => FakeBackfillClient.Failed(BackfillOutcomes.TokenRefused, noCredentials: true);

        var result = await world.RunAsync();

        result.End.ShouldBe(BackfillSessionEnd.NoToken);
        world.Store.Log.ShouldBeEmpty();
        result.Stats.Requests.ShouldBe(0);
    }

    [Theory]
    [InlineData(16, 29, "WindowEnd", 0)]
    [InlineData(16, 30, "Completed", Keys)]
    [InlineData(8, 29, "Completed", Keys)]
    [InlineData(8, 30, "WindowEnd", 0)]
    [InlineData(12, 0, "WindowEnd", 0)]
    public async Task It_runs_only_between_1630_and_0830_india_time(int hour, int minute, string expectedEnd, int requests)
    {
        var world = new World(hour, minute);

        var result = await world.RunAsync();

        result.End.ToString().ShouldBe(expectedEnd);
        world.Client.Calls.Count.ShouldBe(requests);
    }

    [Fact]
    public async Task A_run_that_crosses_0830_stops_at_the_boundary_and_the_next_night_goes_on_where_it_stopped()
    {
        var world = new World(8, 0);
        world.Client.Responder = (_, call) =>
        {
            if (call == 10)
            {
                world.Clock.Advance(TimeSpan.FromMinutes(31)); // 08:31 after the 10th request
            }

            return null;
        };

        var first = await world.RunAsync();

        first.End.ShouldBe(BackfillSessionEnd.WindowEnd);
        world.Client.Calls.Count.ShouldBe(10);
        world.Clock.Jump(At(12, 17, 0));
        world.Client.Responder = null;
        world.Client.Calls.Clear();
        var second = await world.RunAsync();
        second.End.ShouldBe(BackfillSessionEnd.Completed);
        world.Client.Calls.Count.ShouldBe(Keys - 10);
    }

    [Fact]
    public async Task The_daily_cap_counts_the_requests_of_the_india_day_including_earlier_runs_and_ends_the_run()
    {
        var world = new World(settings: Small with { MaxRequestsPerDay = 1000 });
        world.Store.Log.AddRange(Enumerable.Range(0, 998).Select(i => new BackfillFetchRecord(new BackfillKey(Guid.NewGuid(), 'M', 1, 0, "CE", 5, new DateOnly(2021, 1, 1).AddDays(i)), 1, At(12, 0, 5), At(12, 0, 5), BackfillOutcomes.Timeout, null, null, 0, 1)));

        var result = await world.RunAsync();

        result.End.ShouldBe(BackfillSessionEnd.DailyCap);
        world.Client.Calls.Count.ShouldBe(2, "998 sent earlier today, 1,000 is the cap");
        world.Job.State.ShouldBe(BackfillStates.DailyCap);
    }

    [Fact]
    public async Task The_cap_starts_again_with_the_next_india_day()
    {
        var world = new World(settings: Small with { MaxRequestsPerDay = 1000 });
        world.Store.Log.AddRange(Enumerable.Range(0, 1000).Select(i => new BackfillFetchRecord(new BackfillKey(Guid.NewGuid(), 'M', 1, 0, "CE", 5, new DateOnly(2021, 1, 1).AddDays(i)), 1, At(11, 20, 0), At(11, 20, 0), BackfillOutcomes.Timeout, null, null, 0, 1)));

        var result = await world.RunAsync();

        result.End.ShouldBe(BackfillSessionEnd.Completed, "yesterday's requests do not count");
    }

    [Fact]
    public async Task The_stored_size_over_the_own_cap_stops_the_run_writes_a_disk_stop_row_and_deletes_nothing()
    {
        var world = new World();
        world.Store.Stored = world.Settings.MaxStoredBytes + 1;

        var result = await world.RunAsync();

        result.End.ShouldBe(BackfillSessionEnd.DiskBudget);
        world.Client.Calls.ShouldBeEmpty();
        world.Store.Log.ShouldHaveSingleItem().Outcome.ShouldBe(BackfillOutcomes.DiskStop);
        world.Job.State.ShouldBe(RecorderStates.DiskBudget);
        var state = (await world.Store.LoadStateAsync(Ct)).Values.Single();
        (state.IsDone, state.Failed).ShouldBe((false, 0), "a disk stop does not count against the key");
    }

    [Fact]
    public async Task The_disk_is_looked_at_before_every_window_and_a_stop_in_the_middle_keeps_what_was_stored()
    {
        var world = new World();
        world.Client.Responder = (_, call) =>
        {
            if (call == 7)
            {
                world.Store.Stored = world.Settings.MaxStoredBytes + 1;
            }

            return null;
        };

        var result = await world.RunAsync();

        result.End.ShouldBe(BackfillSessionEnd.DiskBudget);
        world.Client.Calls.Count.ShouldBe(7);
        world.Store.Bars.Count.ShouldBe(21, "nothing was deleted");
    }

    [Fact]
    public async Task Free_space_under_the_shared_floor_stops_it_and_an_unreadable_free_space_leaves_the_size_cap_alone()
    {
        var low = new World();
        low.Store.Free = low.Settings.MinFreeBytes - 1;
        (await low.RunAsync()).End.ShouldBe(BackfillSessionEnd.DiskFree);
        low.Job.State.ShouldBe(RecorderStates.DiskFree);

        var unknown = new World();
        unknown.Store.Free = null;
        (await unknown.RunAsync()).End.ShouldBe(BackfillSessionEnd.Completed);
        unknown.Job.FreeSpaceReadable.ShouldBeFalse();

        var capped = new World();
        capped.Store.Free = null;
        capped.Store.Stored = capped.Settings.MaxStoredBytes + 1;
        (await capped.RunAsync()).End.ShouldBe(BackfillSessionEnd.DiskBudget);
    }

    [Fact]
    public async Task A_store_that_fails_is_a_gap_never_an_ok_and_the_key_is_tried_again_next_night()
    {
        var world = new World();
        world.Store.FailAppends = 1;

        var result = await world.RunAsync();

        result.End.ShouldBe(BackfillSessionEnd.Completed);
        result.Stats.ShouldSatisfyAllConditions(s => s.Gaps.ShouldBe(1), s => s.Ok.ShouldBe(Keys - 1));
        world.Store.Log.Count.ShouldBe(Keys, "all but one answer stored and one row that says the last was sent and thrown away");
        world.Store.Log.Single(r => r.Outcome == BackfillOutcomes.StoreFailed).Bars.ShouldBe(0);
        world.Client.Calls.Clear();
        (await world.RunAsync()).Stats.Requests.ShouldBe(1, "only the unwritten key is fetched on the next run");
    }

    [Fact]
    public async Task Three_answers_in_a_row_that_cannot_be_stored_end_the_run_and_the_daily_cap_still_counts_them_after_the_store_is_back()
    {
        var world = new World(settings: Small with { MaxRequestsPerDay = 1000 });
        world.Store.Log.AddRange(Enumerable.Range(0, 997).Select(i => new BackfillFetchRecord(new BackfillKey(Guid.NewGuid(), 'M', 1, 0, "CE", 5, new DateOnly(2021, 1, 1).AddDays(i)), 1, At(12, 0, 5), At(12, 0, 5), BackfillOutcomes.Timeout, null, null, 0, 1)));
        world.Store.FailAppends = int.MaxValue;

        var first = await world.RunAsync();

        first.End.ShouldBe(BackfillSessionEnd.StoreFailed);
        world.Client.Calls.Count.ShouldBe(OptionBackfillJob.StoreFailuresBeforeStop, "no more real requests are sent into a database that is away");
        world.Job.State.ShouldBe(BackfillStates.StoreFailed);
        world.Store.FailAppends = 0;
        world.Client.Calls.Clear();

        var second = await world.RunAsync();

        second.End.ShouldBe(BackfillSessionEnd.DailyCap);
        world.Client.Calls.ShouldBeEmpty("997 logged and 3 thrown away make the cap of 1000");
    }

    [Fact]
    public async Task A_dh905_bad_request_ends_the_run_without_giving_up_the_key_or_the_plan()
    {
        var world = new World();
        world.Client.Responder = (_, call) => call == 4 ? FakeBackfillClient.Failed(BackfillOutcomeMap.From(400, "DH-905"), 400, "DH-905") : null;

        var result = await world.RunAsync();

        result.End.ShouldBe(BackfillSessionEnd.BadRequest);
        world.Client.Calls.Count.ShouldBe(4);
        world.Store.Log[^1].Outcome.ShouldBe(BackfillOutcomes.BadRequest);
        (await world.Store.LoadStateAsync(Ct))[world.Store.Log[^1].Key].ShouldSatisfyAllConditions(s => s.Failed.ShouldBe(0), s => s.IsGivenUp.ShouldBeFalse());
        world.Job.State.ShouldBe(BackfillStates.BadRequest);
    }

    /// <summary>Runs the job to its end on the fake clock: whenever it waits on a timer the clock is moved to it. Bounded: the waits are yields, never sleeps, and the end is awaited for 10 seconds at most.</summary>
    private static async Task<BackfillSessionResult> FinishAsync(Task<BackfillSessionResult> run, SettableClock clock)
    {
        for (var round = 0; round < 100 && !run.IsCompleted; round++)
        {
            for (var i = 0; i < 1_000_000 && !run.IsCompleted && clock.NextTimerDue is null; i++)
            {
                await Task.Yield();
            }

            if (clock.NextTimerDue is { } due)
            {
                clock.Advance(due - clock.GetUtcNow());
            }
        }

        return await run.WaitAsync(TimeSpan.FromSeconds(10), Ct);
    }

    [Fact]
    public async Task Five_rate_limited_answers_in_a_row_end_the_run_and_leave_the_key_open_not_given_up()
    {
        var world = new World();
        world.Client.Responder = (_, _) => FakeBackfillClient.Failed(BackfillOutcomes.RateLimited, 429, "DH-904");

        var result = await FinishAsync(world.RunAsync(), world.Clock);

        result.End.ShouldBe(BackfillSessionEnd.RateLimited);
        world.Client.Calls.Count.ShouldBe(OptionBackfillJob.RateLimitsBeforeStop);
        var state = (await world.Store.LoadStateAsync(Ct)).Single().Value;
        (state.Failed, state.IsGivenUp, state.IsDone, state.Attempts).ShouldBe((0, false, false, 5), "a rate limit is not the key's fault");
        world.Job.State.ShouldBe(BackfillStates.RatePaused);
    }

    [Fact]
    public async Task The_halved_rate_lasts_the_rest_of_the_night_across_runs_and_a_new_night_starts_at_the_configured_rate()
    {
        var world = new World();
        using var firstStop = new CancellationTokenSource();
        world.Client.Responder = (_, call) =>
        {
            if (call == 10)
            {
                firstStop.Cancel();
            }

            return call == 3 ? FakeBackfillClient.Failed(BackfillOutcomes.RateLimited, 429, "DH-904") : null;
        };
        (await FinishAsync(world.RunAsync(firstStop.Token), world.Clock)).End.ShouldBe(BackfillSessionEnd.Stopped);
        world.Client.Calls.Skip(3).ShouldAllBe(c => c.Spacing == TimeSpan.FromSeconds(2));

        using var secondStop = new CancellationTokenSource();
        world.Client.Calls.Clear();
        world.Client.Responder = (_, call) =>
        {
            if (call == 5)
            {
                secondStop.Cancel();
            }

            return null;
        };
        await world.RunAsync(secondStop.Token);
        world.Client.Calls.ShouldAllBe(c => c.Spacing == TimeSpan.FromSeconds(2), "the second run of the same night keeps the halved rate");

        world.Clock.Jump(At(13, 20, 0));
        world.Client.Calls.Clear();
        world.Client.Responder = null;
        await world.RunAsync();
        world.Client.Calls.ShouldNotBeEmpty();
        world.Client.Calls.ShouldAllBe(c => c.Spacing == TimeSpan.FromSeconds(1), "a new night starts at the configured rate");
    }

    [Fact]
    public async Task A_rate_limited_answer_that_cannot_be_stored_still_pauses_and_halves_the_rate_before_the_next_key()
    {
        var world = new World();
        world.Store.FailWhen = r => r.Outcome == BackfillOutcomes.RateLimited;
        world.Client.Responder = (_, call) => call == 1 ? FakeBackfillClient.Failed(BackfillOutcomes.RateLimited, 429, "DH-904") : null;

        var run = world.RunAsync();
        await world.Clock.WhenWaitingForAsync(OptionBackfillJob.RatePause).WaitAsync(TimeSpan.FromSeconds(10), Ct);
        world.Client.Calls.Count.ShouldBe(1, "nothing is sent during the pause, although the answer could not be stored");
        var result = await FinishAsync(run, world.Clock);

        result.End.ShouldBe(BackfillSessionEnd.Completed);
        world.Client.Calls[1].Spacing.ShouldBe(TimeSpan.FromSeconds(2));
        world.Client.Calls[1].Request.ShouldNotBe(world.Client.Calls[0].Request, "the key whose answer was lost stays open for the next night");
        world.Store.Log.Count(r => r.Outcome == BackfillOutcomes.StoreFailed).ShouldBe(1);
    }

    [Fact]
    public async Task A_refused_token_that_cannot_be_stored_still_ends_the_run_and_does_not_go_on_to_the_next_key()
    {
        var world = new World();
        world.Store.FailWhen = r => r.Outcome == BackfillOutcomes.TokenRefused;
        world.Client.Responder = (_, call) => call == 2 ? FakeBackfillClient.Failed(BackfillOutcomes.TokenRefused, 401, "DH-901") : null;

        var result = await world.RunAsync();

        result.End.ShouldBe(BackfillSessionEnd.TokenRefused);
        world.Client.Calls.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_key_whose_answer_can_never_be_stored_is_given_up_after_three_nights_and_does_not_block_the_rest()
    {
        var world = new World();
        var first = BackfillPlanner.Plan(world.Targets, world.Settings, new DateOnly(2026, 10, 12)).First().Key;
        world.Store.FailWhen = r => r.Key == first && r.Outcome != BackfillOutcomes.StoreFailed;

        var one = await world.RunAsync();
        var two = await world.RunAsync();
        var three = await world.RunAsync();

        (one.End, two.End, three.End).ShouldBe((BackfillSessionEnd.Completed, BackfillSessionEnd.Completed, BackfillSessionEnd.Completed));
        three.Stats.GivenUp.ShouldBe(1);
        (await world.Store.LoadStateAsync(Ct))[first].ShouldSatisfyAllConditions(s => s.StoreFailures.ShouldBe(3), s => s.IsGivenUp.ShouldBeTrue());
        world.Client.Calls.Clear();
        await world.RunAsync();
        world.Client.Calls.ShouldBeEmpty("the given-up key is left and listed; everything else is done");
    }

    [Fact]
    public async Task Five_keys_given_up_in_a_row_end_the_run()
    {
        var world = new World();
        world.Client.Responder = (_, _) => FakeBackfillClient.Failed(BackfillOutcomes.Timeout);

        var result = await world.RunAsync();

        result.End.ShouldBe(BackfillSessionEnd.TooManyGivenUp);
        world.Client.Calls.Count.ShouldBe(OptionBackfillJob.GivenUpBeforeStop * KeyState.MaxFailedAttempts);
        result.Stats.GivenUp.ShouldBe(OptionBackfillJob.GivenUpBeforeStop);
        world.Job.State.ShouldBe(BackfillStates.TooManyFailures);
    }

    [Fact]
    public async Task A_finished_key_between_failures_resets_the_given_up_streak()
    {
        var world = new World();
        world.Client.Responder = (_, call) => call % 4 == 0 ? null : FakeBackfillClient.Failed(BackfillOutcomes.Timeout);

        var result = await world.RunAsync();

        result.End.ShouldBe(BackfillSessionEnd.Completed);
        result.Stats.ShouldSatisfyAllConditions(s => s.GivenUp.ShouldBe(Keys / 2), s => s.Ok.ShouldBe(Keys / 2), s => s.Gaps.ShouldBe(3 * (Keys / 2)));
    }

    [Fact]
    public async Task Cancelling_ends_the_run_as_stopped_and_not_as_an_exception()
    {
        var world = new World();
        using var stop = new CancellationTokenSource();
        await stop.CancelAsync();

        var result = await world.RunAsync(stop.Token);

        result.End.ShouldBe(BackfillSessionEnd.Stopped);
        world.Client.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task No_log_line_carries_a_token_a_url_or_a_symbol()
    {
        var logger = new CapturingLogger<OptionBackfillJob>();
        var world = new World(logger: logger);
        world.Client.Responder = (_, call) => call == 1 ? FakeBackfillClient.Failed(BackfillOutcomes.ServerError, 500) : null;
        await world.RunAsync();

        logger.Lines.ShouldNotBeEmpty();
        logger.Lines.ShouldAllBe(l => l.StartsWith("options.backfill.", StringComparison.Ordinal) && !l.Contains("https", StringComparison.OrdinalIgnoreCase) && !l.Contains("NSE:", StringComparison.Ordinal) && !l.Contains("access-token", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Lines.Add(formatter(state, exception));
    }
}

internal static class FakeBackfillTargetsExtensions
{
    public static FakeBackfillTargets AsOnly(this FakeBackfillTargets targets, BackfillTarget target)
    {
        targets.Targets.Clear();
        targets.Targets.Add(target);
        return targets;
    }
}
