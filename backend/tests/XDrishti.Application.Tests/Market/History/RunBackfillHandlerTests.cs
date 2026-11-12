#pragma warning disable CA2012 // NSubstitute setup on ValueTask members is not awaited
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Market.Actions;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.History;
using XDrishti.Application.Market.Actions;
using XDrishti.Application.Market.History;
using XDrishti.Application.Tests.Market.Bars;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Actions;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.History;

namespace XDrishti.Application.Tests.Market.History;

/// <summary>CF-04 Part B criteria 1, 3, 5, 6, 7, 11 and 17 of the runner, on fakes (the real database is in the integration tests).</summary>
public sealed class RunBackfillHandlerTests
{
    private static readonly DateTimeOffset Seam = new(2024, 4, 1, 9, 15, 0, IndiaTime.Offset);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Bar At(DateTimeOffset when, double open = 100, double close = 100) => new(when.ToUnixTimeSeconds(), open, Math.Max(open, close) + 1, Math.Min(open, close) - 1, close, 10);

    private sealed class World
    {
        public World()
        {
            Clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 27, 6, 30, 0, TimeSpan.Zero));
            Target = new FetchTarget(Id, "ABC", "share", "1333", "NSE_EQ", null);
            Targets.Targets.Add(Target);
            Store.CurrentSeams[Id] = Seam;
            Bars.Stored[Id] = [At(Seam, open: 252.10)];
            Source.Respond = t => new IntradayResult(t.InstrumentId, [At(t.Windows[0].From.AddHours(9).AddMinutes(15))], 1);
            Adjustments.GetAsync(Id, Arg.Any<CancellationToken>()).Returns(AdjustmentFactors.None);
            Actions.ListCsvAdjustedThroughAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(new Dictionary<Guid, DateOnly>());
        }

        public Guid Id { get; } = Guid.NewGuid();

        public FetchTarget Target { get; }

        public FakeTimeProvider Clock { get; }

        public FakeBackfillSettings Settings { get; } = new();

        public FakeBackfillStore Store { get; } = new();

        public FakeBackfillCalendar Calendar { get; } = new();

        public FakeBarStore Bars { get; } = new();

        public FakeIntradaySource Source { get; } = new();

        public FakeFetchTargets Targets { get; } = new();

        public FakeDisk Disk { get; set; } = new(long.MaxValue);

        public ICorporateActionQueries Actions { get; } = Substitute.For<ICorporateActionQueries>();

        public IAdjustmentFactors Adjustments { get; } = Substitute.For<IAdjustmentFactors>();

        public RunBackfillHandler Handler() =>
            new(Settings, new FakeDataSettings(), Source, FakeAccountStatus.Connected(), Targets, Bars, Store, Calendar, Disk, Actions, Adjustments, Clock, NullLogger<RunBackfillHandler>.Instance);
    }

    [Fact]
    public async Task With_the_switch_off_nothing_is_requested_read_or_written_and_the_refusal_is_history_disabled()
    {
        var world = new World();
        world.Settings.Enabled = false;

        var result = await world.Handler().HandleAsync(new RunBackfillCommand(), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(BackfillErrors.DisabledCode);
        world.Source.Plans.ShouldBeEmpty();
        world.Store.SeamWrites.ShouldBe(0);
        world.Store.Chunks.ShouldBeEmpty();
        world.Bars.Sessions.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_first_csv_bar_of_2024_04_01_is_ten_requests_newest_first_and_none_reaches_the_seam()
    {
        var world = new World();

        var result = await world.Handler().HandleAsync(new RunBackfillCommand(), Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ChunksDone.ShouldBe(10);
        result.Value.Requests.ShouldBe(10);
        world.Source.Plans.Count.ShouldBe(10);
        var first = world.Source.Plans[0].Targets[0].Windows[0];
        first.From.ShouldBe(new DateTimeOffset(2024, 1, 2, 0, 0, 0, IndiaTime.Offset));
        first.To.ShouldBe(new DateTimeOffset(2024, 4, 1, 0, 0, 0, IndiaTime.Offset));
        world.Source.Plans.ShouldAllBe(p => p.Targets[0].Windows[0].To <= Seam);
        world.Source.Plans.Select(p => p.Targets[0].Windows[0].From).ShouldBe([.. world.Source.Plans.Select(p => p.Targets[0].Windows[0].From).OrderDescending()]);
        world.Store.Chunks.Values.ShouldAllBe(c => c.Status == "done");
    }

    [Fact]
    public async Task An_interrupted_run_resumes_at_the_fifth_chunk_and_a_complete_rerun_writes_nothing_and_changes_no_ledger_row()
    {
        var world = new World();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var calls = 0;
        var respond = world.Source.Respond;
        world.Source.Respond = t =>
        {
            if (++calls == 4)
            {
                cts.Cancel();
            }

            return respond(t);
        };

        await Should.ThrowAsync<OperationCanceledException>(async () => await world.Handler().HandleAsync(new RunBackfillCommand(), cts.Token));
        world.Store.Chunks.Values.Count(c => c.Status == "done").ShouldBe(4);
        world.Store.Chunks.Values.Count(c => c.Status == "planned").ShouldBe(6);
        world.Source.Respond = respond;
        world.Source.Plans.Clear();

        var second = await world.Handler().HandleAsync(new RunBackfillCommand(), Ct);

        second.Value.ChunksDone.ShouldBe(6);
        world.Source.Plans.Count.ShouldBe(6);
        world.Source.Plans[0].Targets[0].Windows[0].To.ShouldBe(new DateTimeOffset(2023, 4, 7, 0, 0, 0, IndiaTime.Offset)); // the fifth newest chunk: 4 x 90 days before the seam
        var before = world.Store.Chunks.ToDictionary(c => c.Key, c => c.Value);
        var writes = world.Store.ChunkWrites;
        world.Source.Plans.Clear();
        world.Bars.Sessions.Clear();

        var third = await world.Handler().HandleAsync(new RunBackfillCommand(), Ct);

        third.Value.ChunksPending.ShouldBe(0);
        third.Value.Rows.ShouldBe(0);
        world.Source.Plans.ShouldBeEmpty();
        world.Bars.Sessions.ShouldBeEmpty();
        world.Store.ChunkWrites.ShouldBe(writes);
        world.Store.Chunks.ShouldBe(before);
    }

    [Fact]
    public async Task A_failed_chunk_is_tried_three_times_then_stays_failed_with_its_error_and_the_instrument_is_partial_and_the_run_is_not_failed()
    {
        var world = new World();
        var bad = new DateTimeOffset(2024, 1, 2, 0, 0, 0, IndiaTime.Offset);
        var respond = world.Source.Respond;
        world.Source.Respond = t => t.Windows[0].From == bad ? new IntradayResult(t.InstrumentId, [], 1, new IntradayFailure(IntradayFailureKind.Failed, "Dhan answered 500")) : respond(t);

        for (var run = 0; run < 3; run++)
        {
            (await world.Handler().HandleAsync(new RunBackfillCommand(), Ct)).IsSuccess.ShouldBeTrue();
        }

        var chunk = world.Store.Chunks[(world.Id, new DateOnly(2024, 1, 2))];
        chunk.Status.ShouldBe("failed");
        chunk.Attempts.ShouldBe(3);
        chunk.Error.ShouldBe("Dhan answered 500");
        world.Source.Plans.Clear();

        var fourth = await world.Handler().HandleAsync(new RunBackfillCommand(), Ct);

        fourth.Value.ChunksPending.ShouldBe(0);
        world.Source.Plans.ShouldBeEmpty();
        var counts = (await world.Store.CountChunksAsync(Ct)).Single();
        BackfillStatus.Of(counts.Total, counts.Finished, counts.Failed, ProbeStatus.None, false).ShouldBe(BackfillStatus.Partial);
    }

    [Fact]
    public async Task A_bar_at_or_after_the_first_csv_bar_is_dropped_counted_beyond_seam_and_never_written()
    {
        var world = new World();
        var respond = world.Source.Respond;
        world.Source.Respond = t => t.Windows[0].To == new DateTimeOffset(2024, 4, 1, 0, 0, 0, IndiaTime.Offset)
            ? new IntradayResult(t.InstrumentId, [At(Seam.AddMinutes(-1)), At(Seam), At(Seam.AddMinutes(1)), At(Seam.AddDays(2))], 1)
            : respond(t);

        var result = await world.Handler().HandleAsync(new RunBackfillCommand(), Ct);

        result.Value.BeyondSeam.ShouldBe(3);
        world.Bars.Sessions.SelectMany(s => s.Bars).ShouldAllBe(b => b.EpochSeconds < Seam.ToUnixTimeSeconds());
        world.Bars.Sessions.ShouldAllBe(s => s.Source == BarSource.Dhan);
    }

    [Fact]
    public async Task Without_the_calendar_the_run_refuses_to_start_names_the_range_and_requests_nothing()
    {
        var world = new World();
        world.Calendar.AllKnown = false;

        var result = await world.Handler().HandleAsync(new RunBackfillCommand(), Ct);

        result.Error.Code.ShouldBe(BackfillErrors.CalendarMissingCode);
        result.Error.Message.ShouldContain("2021-10-27");
        result.Error.Message.ShouldContain("2024-03-31");
        world.Source.Plans.ShouldBeEmpty();
        world.Store.Chunks.ShouldBeEmpty();
    }

    [Fact]
    public async Task Too_little_disk_refuses_with_the_numbers_and_unknown_free_space_is_not_enough()
    {
        var world = new World();
        world.Disk = new FakeDisk(1_000);

        var low = await world.Handler().HandleAsync(new RunBackfillCommand(), Ct);
        world.Disk = new FakeDisk(null);
        var unknown = await world.Handler().HandleAsync(new RunBackfillCommand(), Ct);

        low.Error.Code.ShouldBe(BackfillErrors.DiskLowCode);
        low.Error.Message.ShouldContain("1,000");
        unknown.Error.Code.ShouldBe(BackfillErrors.DiskLowCode);
        world.Source.Plans.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_dry_run_reports_the_estimate_and_requests_and_writes_nothing()
    {
        var world = new World();

        var result = await world.Handler().HandleAsync(new RunBackfillCommand(DryRun: true), Ct);

        result.Value.DryRun.ShouldBeTrue();
        result.Value.ChunksPending.ShouldBe(10);
        result.Value.EstimatedRequests.ShouldBe(10);
        result.Value.EstimatedRows.ShouldBeGreaterThan(0);
        world.Source.Plans.ShouldBeEmpty();
        world.Store.Chunks.ShouldBeEmpty();
        world.Store.SeamWrites.ShouldBe(0);
    }

    [Fact]
    public async Task An_instrument_whose_probe_failed_and_one_without_csv_bars_are_not_backfilled()
    {
        var world = new World();
        world.Store.Records[world.Id] = new SeamRecord(world.Id, Seam, ProbeStatus.Unresolved, null, 8000, 0, "no match", false, null);
        var noCsv = new FetchTarget(Guid.NewGuid(), "NOCSV", "share", "1", "NSE_EQ", null);
        world.Targets.Targets.Add(noCsv);

        var result = await world.Handler().HandleAsync(new RunBackfillCommand(), Ct);

        result.Value.InstrumentsUnresolved.ShouldBe(1);
        result.Value.InstrumentsWithoutCsv.ShouldBe(1);
        world.Source.Plans.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(252.10, false)]
    [InlineData(330.00, true)]
    public async Task When_every_chunk_is_finished_the_seam_is_checked_and_a_move_beyond_20_percent_is_a_seam_jump(double csvOpen, bool jump)
    {
        var world = new World();
        world.Bars.Stored[world.Id] = [At(Seam, open: csvOpen, close: csvOpen)];
        var respond = world.Source.Respond;
        world.Source.Respond = t => t.Windows[0].To == new DateTimeOffset(2024, 4, 1, 0, 0, 0, IndiaTime.Offset)
            ? new IntradayResult(t.InstrumentId, [At(new DateTimeOffset(2024, 3, 28, 15, 29, 0, IndiaTime.Offset), open: 250, close: 250)], 1)
            : respond(t);

        var result = await world.Handler().HandleAsync(new RunBackfillCommand(), Ct);

        result.Value.SeamJumps.ShouldBe(jump ? 1 : 0);
        world.Store.Records[world.Id].SeamJump.ShouldBe(jump);
        var counts = (await world.Store.CountChunksAsync(Ct)).Single();
        BackfillStatus.Of(counts.Total, counts.Finished, counts.Failed, ProbeStatus.None, world.Store.Records[world.Id].SeamJump).ShouldBe(jump ? BackfillStatus.SeamJump : BackfillStatus.Done);
    }

    [Fact]
    public async Task The_continuity_check_writes_its_basis_on_every_run_even_when_no_jump_is_found_CF_04d()
    {
        var world = new World();
        world.Bars.Stored[world.Id] = [At(Seam, open: 252.10, close: 252.10)];
        var respond = world.Source.Respond;
        world.Source.Respond = t => t.Windows[0].To == new DateTimeOffset(2024, 4, 1, 0, 0, 0, IndiaTime.Offset)
            ? new IntradayResult(t.InstrumentId, [At(new DateTimeOffset(2024, 3, 28, 15, 29, 0, IndiaTime.Offset), open: 250, close: 250)], 1)
            : respond(t);

        await world.Handler().HandleAsync(new RunBackfillCommand(), Ct);

        var record = world.Store.Records[world.Id];
        record.SeamJump.ShouldBeFalse();
        record.ContinuityBasis.ShouldBe(SeamBasis.Of(Seam, [], null));
        record.JumpPct.ShouldNotBeNull();
    }

    [Fact]
    public async Task With_nothing_left_to_fetch_the_check_runs_again_on_the_new_basis_and_needs_no_dhan_connection_CF_04d()
    {
        var world = new World();
        world.Bars.Stored[world.Id] = [At(new DateTimeOffset(2024, 3, 28, 15, 29, 0, IndiaTime.Offset), open: 250, close: 250), At(Seam, open: 252.10, close: 252.10)];
        await world.Handler().HandleAsync(new RunBackfillCommand(), Ct);
        var first = world.Store.Records[world.Id].ContinuityBasis;
        var split = new AppliedAction(new DateOnly(2026, 6, 15), CorporateActionKind.Split, 1, 2, 0.5m);
        world.Adjustments.GetAsync(world.Id, Arg.Any<CancellationToken>()).Returns(AdjustmentFactors.From([split]));
        world.Source.Plans.Clear();

        var result = await world.Handler().HandleAsync(new RunBackfillCommand(), Ct);

        result.IsSuccess.ShouldBeTrue();
        world.Source.Plans.ShouldBeEmpty();
        world.Store.Records[world.Id].ContinuityBasis.ShouldBe(SeamBasis.Of(Seam, [split], null));
        world.Store.Records[world.Id].ContinuityBasis.ShouldNotBe(first);
    }

    [Fact]
    public async Task A_refused_token_stops_the_run_with_dhan_not_connected_and_leaves_the_chunk_planned()
    {
        var world = new World();
        world.Source.Respond = t => new IntradayResult(t.InstrumentId, [], 1, new IntradayFailure(IntradayFailureKind.Unauthorized, "401"));

        var result = await world.Handler().HandleAsync(new RunBackfillCommand(), Ct);

        result.Value.Stopped.ShouldBe("dhan_not_connected");
        world.Store.Chunks.Values.ShouldAllBe(c => c.Status == "planned");
    }

    [Fact]
    public async Task A_first_csv_bar_that_moves_earlier_leaves_the_instrument_unresolved_until_it_is_probed_again()
    {
        var world = new World();
        world.Store.Records[world.Id] = new SeamRecord(world.Id, Seam, ProbeStatus.Passed, null, 1, 100, null, false, null);
        world.Store.CurrentSeams[world.Id] = Seam.AddDays(-30);

        var result = await world.Handler().HandleAsync(new RunBackfillCommand(), Ct);

        result.Value.InstrumentsUnresolved.ShouldBe(1);
        world.Store.Records[world.Id].ProbeStatus.ShouldBe(ProbeStatus.Unresolved);
        world.Source.Plans.ShouldBeEmpty();
    }
}
