using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Market.Bars.Pipeline;
using XDrishti.Application.Market.Tracking;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Tests.Market.Bars;

public sealed class CsvPhaseRunnerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 6, 30, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(Now);
    private readonly FakeCsvFolder _folder = new();
    private readonly FakeCsvFileLog _log = new();
    private readonly FakeListedInstruments _listed = new();
    private readonly FakeBarStore _bars = new();
    private readonly FakeDataJobStore _jobs = new();
    private readonly InMemoryTrackingData _tracking = new();
    private readonly FakeDataSettings _settings = new();
    private readonly IInstrumentCatalog _catalog = Substitute.For<IInstrumentCatalog>();

    public CsvPhaseRunnerTests() => _catalog.CountActiveAsync(Arg.Any<CancellationToken>()).Returns(10);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private CsvPhaseRunner Runner => new(_folder, _log, _listed, _bars, _catalog, new InstrumentTracking(_tracking, _tracking), _tracking, _settings, _clock, NullLogger<CsvPhaseRunner>.Instance);

    private Guid Instrument(string symbol)
    {
        var instrument = _tracking.AddInstrument("NSE:" + symbol);
        _listed.Matches["NSE:" + symbol] = [instrument.Id];
        return instrument.Id;
    }

    private async Task<(CsvPhase Phase, DataJob Job)> RunAsync(bool force = false)
    {
        var job = DataJob.Start([DataSteps.Csv], force, "owner@example.invalid", Now);
        _jobs.Jobs[job.Id] = job;
        var phase = await Runner.RunAsync(job, new JobReporter(job, _jobs, _clock), Ct);
        return (phase, job);
    }

    [Fact]
    public async Task Matching_files_are_loaded_in_name_order_with_their_size_and_time_recorded_and_the_instruments_tracked()
    {
        var abb = Instrument("ABB");
        var tcs = Instrument("TCS");
        var fileB = _folder.Add("TCS_1m.csv", FakeCsvFolder.Csv(3));
        var fileA = _folder.Add("ABB_1m.csv.gz", FakeCsvFolder.Csv(5));
        _folder.Add("readme.txt", "x");

        var (phase, job) = await RunAsync();

        phase.Status.ShouldBe(PhaseStatus.Succeeded);
        (phase.Done, phase.Total, phase.FilesLoaded, phase.Rows, phase.Inserted, phase.InstrumentsTracked).ShouldBe((2, 2, 2, 8, 8, 2));
        _bars.Sessions.Select(s => s.InstrumentId).ShouldBe([abb, tcs]);
        _bars.Sessions.ShouldAllBe(s => s.Source == BarSource.Csv && s.Completed);
        _bars.Sessions[0].Mark.ShouldBe(new CsvFileMark("ABB_1m.csv.gz", fileA.Size, fileA.ModifiedAt, 5, 0, 0, DateTimeOffset.FromUnixTimeSeconds(1_711_943_100), DateTimeOffset.FromUnixTimeSeconds(1_711_943_100 + 4 * 60)));
        _bars.Sessions[1].Mark!.Size.ShouldBe(fileB.Size);
        _tracking.Tracked.Select(t => t.Id).ShouldBe([abb, tcs], ignoreOrder: true);
        _tracking.Events.ShouldHaveSingleItem().Detail!.ShouldContain("via CSV load");
        _tracking.Events[0].Actor.ShouldBe("owner@example.invalid");
        phase.StartedAt.ShouldBe(Now);
        phase.FinishedAt.ShouldBe(Now);
        job.Csv.Status.ShouldBe(PhaseStatus.Running); // the executor reports the returned phase; the runner only reported its progress
    }

    [Fact]
    public async Task A_file_loaded_before_with_the_same_size_and_time_is_skipped_unless_forced()
    {
        Instrument("ABB");
        Instrument("TCS");
        Instrument("INFY");
        var same = _folder.Add("ABB_1m.csv", FakeCsvFolder.Csv(2));
        var otherSize = _folder.Add("TCS_1m.csv", FakeCsvFolder.Csv(2));
        var otherTime = _folder.Add("INFY_1m.csv", FakeCsvFolder.Csv(2));
        _log.Add(same);
        _log.Add(otherSize with { Size = otherSize.Size + 1 });
        _log.Add(otherTime with { ModifiedAt = otherTime.ModifiedAt.AddSeconds(1) });

        var (normal, _) = await RunAsync();
        _bars.Sessions.Clear();
        var (forced, _) = await RunAsync(force: true);

        (normal.FilesUnchanged, normal.FilesLoaded, normal.Done, normal.Total).ShouldBe((1, 2, 3, 3));
        (forced.FilesUnchanged, forced.FilesLoaded).ShouldBe((0, 3));
        _bars.Sessions.Count.ShouldBe(3);
    }

    [Fact]
    public async Task Unknown_ambiguous_and_doubled_symbols_are_listed_and_not_loaded()
    {
        Instrument("ABB");
        _listed.Matches["NSE:AMBIG"] = [Guid.NewGuid(), Guid.NewGuid()];
        _listed.Matches["NSE:TWICE"] = [Guid.NewGuid()];
        _folder.Add("ABB_1m.csv", FakeCsvFolder.Csv(1));
        _folder.Add("GONE_1m.csv", FakeCsvFolder.Csv(1));
        _folder.Add("AMBIG_1m.csv", FakeCsvFolder.Csv(1));
        _folder.Add("TWICE_1m.csv", FakeCsvFolder.Csv(1));
        _folder.Add("TWICE_1m.csv.gz", FakeCsvFolder.Csv(1));

        var (phase, _) = await RunAsync();

        (phase.FilesLoaded, phase.FilesSkipped, phase.Total, phase.Done).ShouldBe((1, 4, 5, 5));
        phase.UnknownSymbols.ShouldBe(["NSE:GONE"]);
        phase.AmbiguousSymbols.ShouldBe(["NSE:AMBIG", "NSE:TWICE", "NSE:TWICE"]);
        _folder.Opened.ShouldBe(["ABB_1m.csv"]);
    }

    [Fact]
    public async Task A_file_that_fails_is_reported_and_the_others_still_load_and_the_phase_fails_at_the_end()
    {
        Instrument("BAD");
        Instrument("CORRUPT");
        var boom = Instrument("BOOM");
        var ok = Instrument("OK");
        _folder.Add("BAD_1m.csv", "time,open\n1,2\n");
        _folder.Add("CORRUPT_1m.csv.gz", string.Empty, corruptGzip: true);
        _folder.Add("BOOM_1m.csv", FakeCsvFolder.Csv(2));
        _folder.Add("OK_1m.csv", FakeCsvFolder.Csv(2));
        _bars.Failures[boom] = new InvalidOperationException("secret detail that must not reach the owner");

        var (phase, _) = await RunAsync();

        (phase.Status, phase.FilesFailed, phase.FilesLoaded, phase.Done).ShouldBe((PhaseStatus.Failed, 3, 1, 4));
        phase.FailedFiles.ShouldBe(
        [
            "BAD_1m.csv: The header has no 'high', 'low', 'close', 'volume' column.",
            "BOOM_1m.csv: the file could not be stored",
            "CORRUPT_1m.csv.gz: the file is not a valid gzip file",
        ]);
        phase.Message.ShouldBe("3 of 4 files could not be loaded: BAD_1m.csv: The header has no 'high', 'low', 'close', 'volume' column.");
        _bars.Sessions.Where(s => s.Completed).Select(s => s.InstrumentId).ShouldBe([ok]);
        _tracking.Tracked.Select(t => t.Id).ShouldBe([ok]);
        phase.FailedFiles.ShouldAllBe(f => !f.Contains("secret", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Rejections_are_summed_over_the_files_and_only_ten_examples_are_kept()
    {
        Instrument("ABB");
        Instrument("TCS");
        var header = "time,open,high,low,close,volume\n";
        string BadRows(int n) => string.Concat(Enumerable.Range(0, n).Select(i => $"{1_711_943_100 + i * 60L},0,1,1,1,5\n"));
        _folder.Add("ABB_1m.csv", header + BadRows(7) + "1711950000,1,1,1,1,5\n");
        _folder.Add("TCS_1m.csv", header + BadRows(7));

        var (phase, _) = await RunAsync();

        (phase.Rows, phase.Inserted, phase.Rejected).ShouldBe((15, 1, 14));
        phase.Rejections["nonPositivePrice"].ShouldBe(14);
        phase.RejectionExamples.Count.ShouldBe(10);
        phase.RejectionExamples[0].ShouldBe("ABB_1m.csv line 2: a price is not above zero");
        phase.RejectionExamples[9].ShouldBe("TCS_1m.csv line 4: a price is not above zero");
    }

    [Fact]
    public async Task A_missing_folder_skips_the_phase_with_the_path_in_the_message()
    {
        _folder.FolderExists = false;

        var (phase, _) = await RunAsync();

        (phase.Status, phase.Code).ShouldBe((PhaseStatus.Skipped, "data.csv_folder_missing"));
        phase.Message.ShouldBe("The CSV folder /data/csv was not found. Mount it into the API (Data:CsvFolder) and run again.");
    }

    [Fact]
    public async Task An_empty_folder_loads_nothing_and_says_so()
    {
        var (phase, _) = await RunAsync();

        (phase.Status, phase.Total, phase.Message).ShouldBe((PhaseStatus.Succeeded, 0, "There are no <SYMBOL>_1m.csv files in /data/csv."));
    }

    [Fact]
    public async Task Without_an_instrument_list_the_phase_fails_with_the_master_missing_code_and_touches_no_file()
    {
        _catalog.CountActiveAsync(Arg.Any<CancellationToken>()).Returns(0);
        _folder.Add("ABB_1m.csv", FakeCsvFolder.Csv(1));

        var (phase, _) = await RunAsync();

        (phase.Status, phase.Code).ShouldBe((PhaseStatus.Failed, "data.master_missing"));
        _folder.Opened.ShouldBeEmpty();
    }

    [Fact]
    public async Task When_tracking_is_refused_the_load_stands_and_the_job_gets_a_warning()
    {
        var old = _tracking.AddInstrument("NSE:OLD", retired: true);
        _listed.Matches["NSE:OLD"] = [old.Id];
        _folder.Add("OLD_1m.csv", FakeCsvFolder.Csv(2));

        var (phase, job) = await RunAsync();

        (phase.Status, phase.FilesLoaded, phase.InstrumentsTracked).ShouldBe((PhaseStatus.Succeeded, 1, 0));
        job.Warnings.ShouldHaveSingleItem().ShouldStartWith("The loaded instruments could not be tracked:");
    }

    [Fact]
    public async Task A_save_refused_because_of_a_concurrent_change_is_tried_again()
    {
        var abb = Instrument("ABB");
        _folder.Add("ABB_1m.csv", FakeCsvFolder.Csv(2));
        _tracking.Collisions.Enqueue(() => { });

        var (phase, _) = await RunAsync();

        phase.InstrumentsTracked.ShouldBe(1);
        _tracking.Tracked.Select(t => t.Id).ShouldBe([abb]);
        _tracking.Saves.ShouldBe(2);
    }

    [Fact]
    public async Task Progress_is_published_while_files_are_loaded_but_not_after_every_file()
    {
        for (var i = 0; i < 5; i++)
        {
            Instrument("S" + i);
            _folder.Add($"S{i}_1m.csv", FakeCsvFolder.Csv(1));
        }

        var (_, job) = await RunAsync();

        // The phase start is published at once, then at most every 500 ms of the (fake) clock: nothing moved the clock, so no more than that.
        _jobs.Published.Count.ShouldBe(1);
        job.Csv.Done.ShouldBe(5);
    }

    [Theory]
    [InlineData("shifted", "after subtracting 5 h 30 min")]
    [InlineData("endOfMinute", "END of each minute")]
    [InlineData("milliseconds", "look like epoch milliseconds")]
    [InlineData("future", "later than one day from now (2026-10-13)")]
    [InlineData("outsideSessions", "outside every trading session")]
    public async Task A_file_with_a_wrong_clock_fails_alone_and_nothing_of_it_is_stored_or_tracked(string kind, string reason)
    {
        var bad = Instrument("BAD");
        var ok = Instrument("OK");
        var future = Now.AddDays(30).ToUnixTimeSeconds();
        var times = kind switch
        {
            "shifted" => CsvFixtures.Times(days: 3, shiftSeconds: 19_800),
            "endOfMinute" => CsvFixtures.Times(days: 3, firstMinute: 1),
            "milliseconds" => CsvFixtures.Times(days: 3).Select(t => t * 1000),
            "future" => Enumerable.Range(0, 400).Select(i => future + (i * 60L)),
            _ => CsvFixtures.Times(days: 3, count: 120, firstMinute: 765),
        };
        _folder.Add("BAD_1m.csv", CsvFixtures.Csv(times));
        _folder.Add("OK_1m.csv", FakeCsvFolder.Csv(3));

        var (phase, _) = await RunAsync();

        (phase.Status, phase.FilesFailed, phase.FilesLoaded, phase.Inserted).ShouldBe((PhaseStatus.Failed, 1, 1, 3));
        var failure = phase.FailedFiles.ShouldHaveSingleItem();
        failure.ShouldStartWith("BAD_1m.csv: ");
        failure.ShouldContain(reason);
        _bars.Sessions.Single(s => s.InstrumentId == bad).Completed.ShouldBeFalse();
        _bars.Stored.ContainsKey(bad).ShouldBeFalse();
        _tracking.Tracked.Select(t => t.Id).ShouldBe([ok]);
    }

    [Fact]
    public async Task A_file_over_the_row_limit_fails_with_the_setting_named()
    {
        Instrument("ABB");
        _settings.CsvMaxRowsPerFile = 3;
        _folder.Add("ABB_1m.csv", FakeCsvFolder.Csv(4));

        var (phase, _) = await RunAsync();

        phase.FailedFiles.ShouldBe(["ABB_1m.csv: The file has more than 3 rows (Data:Csv:MaxRowsPerFile): this is not a normal file of bars."]);
        _bars.Sessions.ShouldAllBe(s => !s.Completed);
    }

    [Fact]
    public async Task A_compressed_file_that_grows_past_the_byte_limit_when_unpacked_fails()
    {
        Instrument("ABB");
        _settings.CsvMaxBytesPerFile = 2_000;
        _folder.Add("ABB_1m.csv.gz", FakeCsvFolder.Csv(500));

        var (phase, _) = await RunAsync();

        phase.FailedFiles.ShouldBe(["ABB_1m.csv.gz: The file is larger than 2,000 bytes once decompressed (Data:Csv:MaxBytesPerFile): this is not a normal file of bars."]);
        _bars.Sessions.ShouldAllBe(s => !s.Completed);
    }

    [Fact]
    public async Task A_file_that_spans_too_much_time_fails_with_the_setting_named()
    {
        Instrument("ABB");
        _settings.CsvMaxSpanDays = 31;
        _folder.Add("ABB_1m.csv", CsvFixtures.Csv([CsvFixtures.Day0, CsvFixtures.Day0 + (40 * CsvFixtures.Day)]));

        var (phase, _) = await RunAsync();

        phase.FailedFiles.ShouldBe(["ABB_1m.csv: The file spans more than 31 days (from 2024-04-01 on, Data:Csv:MaxSpanDays): this is not a normal file of bars."]);
    }

    [Fact]
    public async Task The_muhurat_evening_session_and_a_saturday_session_in_a_file_are_legitimate()
    {
        var abb = Instrument("ABB");
        var times = CsvFixtures.Times(days: 6).Concat(CsvFixtures.Times(days: 1, count: 60, firstMinute: 540, startDay: 7));
        _folder.Add("ABB_1m.csv.gz", CsvFixtures.Csv(times));

        var (phase, _) = await RunAsync();

        (phase.Status, phase.FilesLoaded, phase.Inserted).ShouldBe((PhaseStatus.Succeeded, 1, (6 * 375) + 60));
        _bars.Stored[abb].Count.ShouldBe((6 * 375) + 60);
    }

    [Fact]
    public async Task An_instrument_the_owner_untracked_is_not_tracked_again_by_a_changed_file()
    {
        var abb = Instrument("ABB");
        var row = TrackedInstrument.Track(abb, "owner@example.invalid", Now.AddDays(-5));
        row.Deactivate(Now.AddDays(-1));
        _tracking.Tracked.Add(row);
        _folder.Add("ABB_1m.csv", FakeCsvFolder.Csv(3));

        var (phase, _) = await RunAsync();

        (phase.FilesLoaded, phase.InstrumentsTracked).ShouldBe((1, 0));
        row.Active.ShouldBeFalse();
        _tracking.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_unchanged_file_whose_instrument_was_never_tracked_gets_it_tracked_so_a_failed_attempt_is_retried()
    {
        var abb = Instrument("ABB");
        _log.Add(_folder.Add("ABB_1m.csv", FakeCsvFolder.Csv(3)), rows: 3);

        var (phase, _) = await RunAsync();

        (phase.FilesUnchanged, phase.FilesLoaded, phase.InstrumentsTracked).ShouldBe((1, 0, 1));
        _folder.Opened.ShouldBeEmpty();
        _tracking.Tracked.Select(t => t.Id).ShouldBe([abb]);
    }

    [Fact]
    public async Task An_unchanged_file_does_not_bring_back_an_untracked_instrument_or_track_one_without_bars()
    {
        var untracked = Instrument("ABB");
        var empty = Instrument("EMPTY");
        var row = TrackedInstrument.Track(untracked, "owner@example.invalid", Now.AddDays(-5));
        row.Deactivate(Now.AddDays(-1));
        _tracking.Tracked.Add(row);
        _log.Add(_folder.Add("ABB_1m.csv", FakeCsvFolder.Csv(3)), rows: 3);
        _log.Add(_folder.Add("EMPTY_1m.csv", FakeCsvFolder.Csv(1)), rows: 0);

        var (phase, _) = await RunAsync();

        (phase.FilesUnchanged, phase.InstrumentsTracked).ShouldBe((2, 0));
        row.Active.ShouldBeFalse();
        _tracking.Tracked.Select(t => t.Id).ShouldNotContain(empty);
    }

    [Fact]
    public async Task A_load_with_no_accepted_rows_does_not_track_the_instrument()
    {
        Instrument("ABB");
        _folder.Add("ABB_1m.csv", "time,open,high,low,close,volume\n1711943100,0,1,1,1,5\n");

        var (phase, _) = await RunAsync();

        (phase.FilesLoaded, phase.Inserted, phase.InstrumentsTracked).ShouldBe((1, 0, 0));
        _tracking.Tracked.ShouldBeEmpty();
    }

    [Fact]
    public async Task When_the_job_was_ended_elsewhere_the_phase_stops_and_touches_no_file()
    {
        Instrument("ABB");
        _folder.Add("ABB_1m.csv", FakeCsvFolder.Csv(3));
        var job = DataJob.Start([DataSteps.Csv], false, "owner", Now);
        _jobs.Jobs[job.Id] = job;
        _jobs.EndedElsewhere.Add(job.Id);
        var reporter = new JobReporter(job, _jobs, _clock);

        var phase = await Runner.RunAsync(job, reporter, Ct);

        reporter.Ended.ShouldBeTrue();
        phase.Status.ShouldBe(PhaseStatus.Failed);
        phase.Message.ShouldBe("The job was ended elsewhere (marked failed as interrupted); the phase stopped.");
        _folder.Opened.ShouldBeEmpty();
        _tracking.Tracked.ShouldBeEmpty();
    }

    [Fact]
    public async Task Cancelling_stops_the_phase_between_files()
    {
        Instrument("ABB");
        _folder.Add("ABB_1m.csv", FakeCsvFolder.Csv(1));
        using var cancel = new CancellationTokenSource();
        await cancel.CancelAsync();
        var job = DataJob.Start([DataSteps.Csv], false, "owner", Now);

        await Should.ThrowAsync<OperationCanceledException>(async () => await Runner.RunAsync(job, new JobReporter(job, _jobs, _clock), cancel.Token));
    }
}
