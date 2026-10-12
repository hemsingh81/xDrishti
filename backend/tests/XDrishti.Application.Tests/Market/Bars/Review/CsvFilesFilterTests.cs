using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Market.Bars;
using XDrishti.Application.Market.Bars.Pipeline;
using XDrishti.Application.Market.Tracking;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Tests.Market.Bars.Review;

/// <summary><c>csvFiles</c> on a data job: validated against the folder listing when the job starts, and the only files the CSV phase loads.</summary>
public sealed class CsvFilesFilterTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 6, 30, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(Now);
    private readonly FakeDataJobStore _jobs = new();
    private readonly IDataJobChannel _queue = Substitute.For<IDataJobChannel>();
    private readonly IInstrumentCatalog _catalog = Substitute.For<IInstrumentCatalog>();
    private readonly FakeCsvFolder _folder = new();
    private readonly FakeCsvFileLog _log = new();
    private readonly FakeListedInstruments _listed = new();
    private readonly FakeBarStore _bars = new();
    private readonly InMemoryTrackingData _tracking = new();
    private readonly FakeDataSettings _settings = new();

    public CsvFilesFilterTests() => _catalog.CountActiveAsync(Arg.Any<CancellationToken>()).Returns(10);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private StartDataJobHandler Start => new(_jobs, _queue, _catalog, _folder, _clock, NullLogger<StartDataJobHandler>.Instance);

    private Task<XDrishti.Domain.Common.Result<DataJobView>> StartAsync(string[]? csvFiles, string[]? steps = null) =>
        Start.HandleAsync(new StartDataJobCommand(new StartDataJobRequest(steps, null, null, csvFiles), "owner"), Ct).AsTask();

    private void Instrument(string symbol)
    {
        var instrument = _tracking.AddInstrument("NSE:" + symbol);
        _listed.Matches["NSE:" + symbol] = [instrument.Id];
    }

    [Fact]
    public async Task Named_files_of_the_folder_are_kept_on_the_job_without_duplicates_and_shown_in_the_view()
    {
        _folder.Add("ABB_1m.csv.gz", FakeCsvFolder.Csv(1));
        _folder.Add("TCS_1m.csv", FakeCsvFolder.Csv(1));

        var result = await StartAsync(["TCS_1m.csv", "ABB_1m.csv.gz", "TCS_1m.csv"], ["csv"]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.CsvFiles.ShouldBe(["TCS_1m.csv", "ABB_1m.csv.gz"]);
        _jobs.Started.Single().CsvFiles.ShouldBe(["TCS_1m.csv", "ABB_1m.csv.gz"]);
    }

    [Theory]
    [InlineData("GONE_1m.csv")]
    [InlineData("../ABB_1m.csv.gz")]
    [InlineData("sub/ABB_1m.csv.gz")]
    [InlineData("C:\\ABB_1m.csv.gz")]
    [InlineData("abb_1m.csv.gz")]
    [InlineData("ABB_1m.csv.gz\n")]
    [InlineData("README.md")]
    public async Task A_name_the_folder_does_not_list_is_refused_with_the_name_and_no_job_starts(string name)
    {
        _folder.Add("ABB_1m.csv.gz", FakeCsvFolder.Csv(1));
        _folder.Add("README.md", "x");

        var result = await StartAsync([name], ["csv"]);

        result.Error.Code.ShouldBe("data.invalid");
        var field = result.Error.Fields!.Single();
        field.Field.ShouldBe("csvFiles");
        field.Message.ShouldContain("is not a <SYMBOL>_1m.csv[.gz] file of the CSV folder");
        field.Message.Any(char.IsControl).ShouldBeFalse();
        _jobs.Started.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_error_names_the_first_unknown_files_only()
    {
        var result = await StartAsync([.. Enumerable.Range(0, 9).Select(i => $"X{i}_1m.csv")], ["csv"]);

        result.Error.Fields!.Count.ShouldBe(5);
        result.Error.Fields![0].Message.ShouldContain("X0_1m.csv");
    }

    [Fact]
    public async Task An_empty_list_is_refused_rather_than_loading_everything()
    {
        var result = await StartAsync([], ["csv"]);

        result.Error.Code.ShouldBe("data.invalid");
        result.Error.Fields!.Single().Field.ShouldBe("csvFiles");
    }

    [Fact]
    public async Task More_than_214_names_are_refused_and_exactly_214_are_allowed()
    {
        string[] names = [.. Enumerable.Range(0, 215).Select(i => $"S{i}_1m.csv")];
        foreach (var name in names)
        {
            _folder.Add(name, FakeCsvFolder.Csv(1));
        }

        var tooMany = await StartAsync(names, ["csv"]);
        var allowed = await StartAsync(names[..214], ["csv"]);

        tooMany.Error.Fields!.Single().Message.ShouldBe("Name at most 214 files.");
        allowed.IsSuccess.ShouldBeTrue();
        allowed.Value.CsvFiles!.Count.ShouldBe(214);
    }

    [Fact]
    public async Task Without_the_csv_step_the_list_is_ignored_even_when_it_is_wrong()
    {
        var result = await StartAsync(["GONE_1m.csv"], ["aggregate"]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.CsvFiles.ShouldBeNull();
    }

    [Fact]
    public async Task Without_a_list_every_file_is_loaded_and_the_view_has_no_list()
    {
        var result = await StartAsync(null, ["csv"]);

        result.Value.CsvFiles.ShouldBeNull();
    }

    [Fact]
    public async Task A_missing_or_unreadable_folder_has_no_files_to_name()
    {
        _folder.FolderExists = false;
        var missing = await StartAsync(["ABB_1m.csv"], ["csv"]);
        _folder.FolderExists = true;
        _folder.ListFailure = new UnauthorizedAccessException();
        var unreadable = await StartAsync(["ABB_1m.csv"], ["csv"]);

        missing.Error.Code.ShouldBe("data.invalid");
        unreadable.Error.Code.ShouldBe("data.invalid");
    }

    private async Task<CsvPhase> RunPhaseAsync(DataJob job)
    {
        _jobs.Jobs[job.Id] = job;
        var runner = new CsvPhaseRunner(_folder, _log, _listed, _bars, _catalog, new InstrumentTracking(_tracking, _tracking), _tracking, _settings, _clock, NullLogger<CsvPhaseRunner>.Instance);
        return await runner.RunAsync(job, new JobReporter(job, _jobs, _clock), Ct);
    }

    [Fact]
    public async Task The_csv_phase_loads_only_the_named_files_and_counts_only_those()
    {
        foreach (var symbol in new[] { "ABB", "TCS", "INFY" })
        {
            Instrument(symbol);
            _folder.Add($"{symbol}_1m.csv", FakeCsvFolder.Csv(3));
        }

        var phase = await RunPhaseAsync(DataJob.Start([DataSteps.Csv], false, "owner", Now, csvFiles: ["TCS_1m.csv", "INFY_1m.csv"]));

        (phase.Total, phase.Done, phase.FilesLoaded, phase.Rows).ShouldBe((2, 2, 2, 6));
        _folder.Opened.OrderBy(n => n, StringComparer.Ordinal).ShouldBe(["INFY_1m.csv", "TCS_1m.csv"]);
        _tracking.Tracked.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Named_files_that_are_unchanged_are_still_skipped_unless_forced()
    {
        Instrument("ABB");
        Instrument("TCS");
        _log.Add(_folder.Add("ABB_1m.csv", FakeCsvFolder.Csv(3)));
        _folder.Add("TCS_1m.csv", FakeCsvFolder.Csv(3));

        var normal = await RunPhaseAsync(DataJob.Start([DataSteps.Csv], false, "owner", Now, csvFiles: ["ABB_1m.csv", "TCS_1m.csv"]));
        _folder.Opened.Clear();
        var forced = await RunPhaseAsync(DataJob.Start([DataSteps.Csv], true, "owner", Now, csvFiles: ["ABB_1m.csv"]));

        (normal.FilesUnchanged, normal.FilesLoaded, normal.Total).ShouldBe((1, 1, 2));
        (forced.FilesUnchanged, forced.FilesLoaded, forced.Total).ShouldBe((0, 1, 1));
        _folder.Opened.ShouldBe(["ABB_1m.csv"]);
    }

    [Fact]
    public async Task Ambiguity_is_judged_over_the_whole_folder_so_one_of_two_files_of_a_symbol_is_still_skipped()
    {
        Instrument("TWICE");
        _folder.Add("TWICE_1m.csv", FakeCsvFolder.Csv(1));
        _folder.Add("TWICE_1m.csv.gz", FakeCsvFolder.Csv(1));

        var phase = await RunPhaseAsync(DataJob.Start([DataSteps.Csv], false, "owner", Now, csvFiles: ["TWICE_1m.csv"]));

        (phase.FilesLoaded, phase.FilesSkipped).ShouldBe((0, 1));
        phase.AmbiguousSymbols.ShouldBe(["NSE:TWICE"]);
    }

    [Fact]
    public async Task A_named_file_that_left_the_folder_since_the_start_fails_the_phase_with_its_name()
    {
        Instrument("ABB");
        _folder.Add("ABB_1m.csv", FakeCsvFolder.Csv(3));

        var phase = await RunPhaseAsync(DataJob.Start([DataSteps.Csv], false, "owner", Now, csvFiles: ["ABB_1m.csv", "GONE_1m.csv"]));

        (phase.Status, phase.FilesLoaded, phase.FilesFailed, phase.Total, phase.Done).ShouldBe((PhaseStatus.Failed, 1, 1, 2, 2));
        phase.FailedFiles.ShouldBe(["GONE_1m.csv: the file is no longer in the folder"]);
    }

    [Fact]
    public async Task Naming_only_files_that_left_the_folder_loads_nothing_and_fails()
    {
        Instrument("ABB");
        _folder.Add("ABB_1m.csv", FakeCsvFolder.Csv(3));

        var phase = await RunPhaseAsync(DataJob.Start([DataSteps.Csv], false, "owner", Now, csvFiles: ["GONE_1m.csv"]));

        (phase.Status, phase.FilesLoaded, phase.FilesFailed).ShouldBe((PhaseStatus.Failed, 0, 1));
        _folder.Opened.ShouldBeEmpty();
    }
}
