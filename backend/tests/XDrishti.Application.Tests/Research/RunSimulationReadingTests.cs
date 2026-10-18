using NSubstitute;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Research;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Tests.Research;

/// <summary>Criteria 5, 7 and 9 of P3-04a: the window of the read, the tick, and the discipline of the I/O (order, one read at a time, once per run, cancellation).</summary>
public sealed class RunSimulationReadingTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_read_starts_in_the_year_2000_and_ends_at_23_59_india_time_on_the_last_day_both_with_offset_zero_and_the_engine_gets_every_bar_read()
    {
        var fixture = new RunFixture();
        var abb = fixture.Add("NSE:ABB", RunFixture.ThreeDays());

        var result = await fixture.RunAsync(RunFixture.List([abb], new DateOnly(2026, 3, 3), new DateOnly(2026, 3, 5)), Ct);

        result.IsSuccess.ShouldBeTrue();
        var (id, from, to) = fixture.Store.Reads.Single();
        id.ShouldBe(abb);
        from.Offset.ShouldBe(TimeSpan.Zero, "Npgsql writes a DateTimeOffset to timestamptz only with offset 0");
        to.Offset.ShouldBe(TimeSpan.Zero);
        from.ShouldBe(new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.FromMinutes(330)));
        to.ShouldBe(new DateTimeOffset(2026, 3, 5, 23, 59, 0, TimeSpan.FromMinutes(330)));
        from.UtcDateTime.ShouldBe(new DateTime(1999, 12, 31, 18, 30, 0, DateTimeKind.Utc));
        to.UtcDateTime.ShouldBe(new DateTime(2026, 3, 5, 18, 29, 0, DateTimeKind.Utc));
        var given = fixture.Engine.Replays.Single();
        given.RawBars.Count.ShouldBe(3 * 375, "the replay starts at the first stored session whatever From is: the bars before From are given too");
        (given.From, given.To).ShouldBe((new DateOnly(2026, 3, 3), new DateOnly(2026, 3, 5)));
        given.Options.ShouldBe(ReplayOptions.Default);
    }

    [Fact]
    public async Task A_last_day_before_the_first_bar_reads_nothing_and_the_empty_list_goes_to_the_engine_as_it_is()
    {
        var fixture = new RunFixture();
        var abb = fixture.Add("NSE:ABB", RunFixture.ThreeDays());

        var result = await fixture.RunAsync(RunFixture.List([abb], new DateOnly(2026, 2, 2), new DateOnly(2026, 2, 27)), Ct);

        fixture.Engine.Replays.Single().RawBars.ShouldBeEmpty();
        result.Value.Instruments.Single().Skipped.ShouldBe(SkipReason.NoBars);
    }

    [Fact]
    public async Task Options_are_given_to_the_engine_and_the_calendar_is_the_one_read_once()
    {
        var fixture = new RunFixture();
        var abb = fixture.Add("NSE:ABB", RunFixture.ThreeDays());
        var options = new ReplayOptions(0.8, 0.1);

        var result = await fixture.RunAsync(RunFixture.List([abb]) with { Options = options }, Ct);

        fixture.Engine.Replays.Single().Options.ShouldBe(options);
        result.Value.Options.ShouldBe(options);
    }

    [Theory]
    [InlineData(5.0, 0.05, false)]
    [InlineData(10.0, 0.10, false)]
    [InlineData(null, 0.05, true)]
    public async Task A_valid_tick_or_a_missing_one_reaches_the_engine_as_a_tick_spec_in_rupees(double? paise, double rupees, bool fallback)
    {
        var fixture = new RunFixture();
        var abb = fixture.Add("NSE:ABB", RunFixture.ThreeDays(), tickPaise: paise is null ? null : (decimal)paise);

        var result = await fixture.RunAsync(RunFixture.List([abb]), Ct);

        var tick = fixture.Engine.Replays.Single().Tick.ShouldNotBeNull();
        ((double)tick.Size).ShouldBe(rupees);
        tick.FromFallback.ShouldBe(fallback);
        fixture.Engine.Replays.Single().PresetSkip.ShouldBeNull();
        result.Value.Instruments.Single().TickFromFallback.ShouldBe(fallback);
    }

    [Theory]
    [InlineData(-5)]
    [InlineData(0)]
    [InlineData(150)]
    public async Task An_invalid_tick_skips_that_instrument_with_a_note_and_the_next_one_runs(double paise)
    {
        var fixture = new RunFixture();
        var bad = fixture.Add("BAD", RunFixture.ThreeDays(), tickPaise: (decimal)paise);
        var good = fixture.Add("GOOD", RunFixture.ThreeDays());

        var result = await fixture.RunAsync(RunFixture.List([bad, good]), Ct);

        var run = result.Value;
        run.Instruments.Select(i => i.Skipped).ShouldBe([SkipReason.TickSizeInvalid, null]);
        run.Instruments[0].SkipDetail.ShouldNotBeNullOrEmpty();
        run.Instruments[0].BarsRead.ShouldBe(3 * 375, "a skipped instrument that was read keeps its facts");
        fixture.Engine.Replays.Select(r => r.PresetSkip).ShouldBe([SkipReason.TickSizeInvalid, null]);
    }

    [Fact]
    public async Task A_basket_members_tick_is_used_as_it_is_and_no_instrument_row_is_read_for_it()
    {
        var fixture = new RunFixture();
        var abb = fixture.Add("NSE:ABB", RunFixture.ThreeDays());
        var basket = Guid.CreateVersion7();
        fixture.SetBasket(basket, "One", null, (abb, "NSE:ABB", 0, 10m, false));

        await fixture.RunAsync(RunFixture.Basket(basket), Ct);

        ((double)fixture.Engine.Replays.Single().Tick!.Size).ShouldBe(0.10, "the summary's tick (paise), not a second lookup of the instrument");
        await fixture.BarQueries.DidNotReceive().FindInstrumentAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        typeof(RunSimulationHandler).GetConstructors().Single().GetParameters().Select(p => p.ParameterType).ShouldNotContain(typeof(IInstrumentQueries));
    }

    [Fact]
    public async Task Reads_happen_in_the_order_of_the_universe_and_never_two_at_a_time()
    {
        var fixture = new RunFixture { Store = { AwaitReads = true } };
        var ids = Enumerable.Range(0, 6).Select(i => fixture.Add("S" + i, RunFixture.ThreeDays())).ToList();

        await fixture.RunAsync(RunFixture.List(ids), Ct);

        fixture.Store.Reads.Select(r => r.InstrumentId).ShouldBe(ids);
        fixture.Store.MaxConcurrentReads.ShouldBe(1);
        fixture.Engine.Replays.Select(r => r.InstrumentId).ShouldBe(ids);
    }

    [Fact]
    public async Task A_read_never_starts_before_the_previous_replay_has_finished()
    {
        var fixture = new RunFixture();
        var order = new List<string>();
        fixture.Store.OnRead = id => order.Add("read " + id.ToString("N")[..4]);
        fixture.Engine.OnReplay = input => order.Add("replay " + input.InstrumentId.ToString("N")[..4]);
        var a = fixture.Add("A", RunFixture.ThreeDays(), id: Guid.Parse("aaaa0000-0000-0000-0000-000000000000"));
        var b = fixture.Add("B", RunFixture.ThreeDays(), id: Guid.Parse("bbbb0000-0000-0000-0000-000000000000"));

        await fixture.RunAsync(RunFixture.List([a, b]), Ct);

        order.ShouldBe(["read aaaa", "replay aaaa", "read bbbb", "replay bbbb"]);
    }

    [Fact]
    public async Task The_calendar_is_read_once_the_csv_dates_once_for_all_ids_and_a_merge_is_never_begun()
    {
        var fixture = new RunFixture();
        var ids = Enumerable.Range(0, 4).Select(i => fixture.Add("S" + i, RunFixture.ThreeDays())).ToList();

        await fixture.RunAsync(RunFixture.List(ids), Ct);

        await fixture.Calendar.Received(1).GetAsync(Arg.Any<CancellationToken>());
        await fixture.Actions.Received(1).ListCsvAdjustedThroughAsync(Arg.Is<IReadOnlyCollection<Guid>>(c => c.SequenceEqual(ids)), Arg.Any<CancellationToken>());
        fixture.Store.Sessions.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_token_cancelled_between_two_instruments_ends_the_run_with_the_cancellation_and_no_record()
    {
        var fixture = new RunFixture();
        using var source = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var a = fixture.Add("A", RunFixture.ThreeDays());
        var b = fixture.Add("B", RunFixture.ThreeDays());
        fixture.Engine.OnReplay = _ => source.Cancel();

        var run = async () => await fixture.RunAsync(RunFixture.List([a, b]), source.Token);

        await Should.ThrowAsync<OperationCanceledException>(run);
        fixture.Engine.Replays.Count.ShouldBe(1);
        fixture.Store.Reads.Count.ShouldBe(1, "the second instrument is not read");
    }

    [Fact]
    public async Task A_token_cancelled_before_the_first_instrument_reads_no_bar()
    {
        var fixture = new RunFixture();
        var a = fixture.Add("A", RunFixture.ThreeDays());
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        var run = async () => await fixture.RunAsync(RunFixture.List([a]), source.Token);

        await Should.ThrowAsync<OperationCanceledException>(run);
        fixture.Store.Reads.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_exception_of_the_engine_for_one_instrument_ends_the_run_and_is_not_turned_into_a_skip()
    {
        var fixture = new RunFixture();
        var a = fixture.Add("A", RunFixture.ThreeDays());
        var b = fixture.Add("B", RunFixture.ThreeDays());
        fixture.Engine.Script = input => input.InstrumentId == a ? throw new InvalidOperationException("the bars are not ascending") : FakeSimulationEngine.Default(input);

        var run = async () => await fixture.RunAsync(RunFixture.List([a, b]), Ct);

        (await Should.ThrowAsync<InvalidOperationException>(run)).Message.ShouldContain("ascending");
        fixture.Store.Reads.Count.ShouldBe(1);
    }

    [Fact]
    public async Task The_handler_asks_the_engine_to_prepare_once_per_run_not_per_instrument()
    {
        var fixture = new RunFixture();
        var ids = Enumerable.Range(0, 3).Select(i => fixture.Add("S" + i, RunFixture.ThreeDays())).ToList();

        await fixture.RunAsync(RunFixture.List(ids), Ct);

        fixture.Engine.Prepared.Count.ShouldBe(1);
        fixture.Engine.Replays.Count.ShouldBe(3);
    }

    [Fact]
    public async Task A_list_of_two_thousand_ids_is_accepted()
    {
        var fixture = new RunFixture();
        var ids = Enumerable.Range(0, 2_000).Select(i => fixture.Add("S" + i)).ToList();

        var result = await fixture.RunAsync(RunFixture.List(ids), Ct);

        result.Value.Instruments.Count.ShouldBe(2_000);
        await fixture.Actions.Received(1).ListCsvAdjustedThroughAsync(Arg.Is<IReadOnlyCollection<Guid>>(c => c.Count == 2_000), Arg.Any<CancellationToken>());
    }
}
