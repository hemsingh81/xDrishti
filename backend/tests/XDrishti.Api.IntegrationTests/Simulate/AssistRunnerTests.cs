using System.Runtime.Versioning;
using System.Text;
using XDrishti.Api.Simulate;
using XDrishti.Api.Simulate.Assist;
using XDrishti.SimExport.Verification;
using XDrishti.SimReader;
using static XDrishti.Api.IntegrationTests.Simulate.AssistSupport;

namespace XDrishti.Api.IntegrationTests.Simulate;

/// <summary>P3-09c criteria 4 to 7: the order of reads (blindness), the verdicts, the cache, the untouched owner's log, the empty verdict column and the deterministic bytes.</summary>
[UnsupportedOSPlatform("windows")]
public sealed class AssistRunnerTests
{
    private static readonly string[] One = [Symbol];

    private static string Text(AssistSupport.MemoryFolder folder, string name) => Encoding.UTF8.GetString(folder.Files[name]);

    private static async Task<(AssistOutcome Outcome, AssistSupport.MemoryFolder Folder, FakeSecond Second)> RunAsync(
        string[]? symbols = null, Func<string, SecondDay>? second = null, IReadOnlyList<ReaderBar>? stored = null, byte[]? engine = null)
    {
        symbols ??= One;
        var folder = Folder(symbols, engine);
        var source = new FakeSecond(second ?? (_ => new SecondDay(BuyDay(), null)));
        var outcome = await AssistRunner.RunAsync(folder, source, new FakeStored(stored ?? BuyDay()), TestContext.Current.CancellationToken);
        return (outcome, folder, source);
    }

    private static string Verdict(AssistSupport.MemoryFolder folder, string verdict) =>
        Text(folder, DiskPackFolder.ReportName).Contains("<td>" + verdict + "</td>", StringComparison.Ordinal) ? verdict : string.Empty;

    [Fact]
    public async Task The_engine_values_are_read_only_after_the_assist_log_is_written()
    {
        var (outcome, folder, _) = await RunAsync();

        outcome.Exit.ShouldBe(0, string.Join('\n', outcome.Lines));
        var write = folder.Ops.IndexOf("write assist-log.csv");
        var read = folder.Ops.IndexOf("read engine-values.csv");
        write.ShouldBeGreaterThanOrEqualTo(0);
        read.ShouldBeGreaterThan(write);
        folder.Ops.Count(o => o == "read engine-values.csv").ShouldBe(1);
    }

    [Fact]
    public async Task The_report_prints_both_hashes_says_it_is_not_a_signed_check_and_counts_without_percentages()
    {
        var (outcome, folder, _) = await RunAsync();

        var report = Text(folder, DiskPackFolder.ReportName);
        report.ShouldContain(outcome.LogHash!);
        report.ShouldContain(outcome.EngineHash!);
        report.ShouldContain("not a signed check");
        report.ShouldContain("agree_all: 1");
        report.ShouldNotContain("%)");
        Verdict(folder, Verdicts.AgreeAll).ShouldBe(Verdicts.AgreeAll);
    }

    [Fact]
    public async Task The_owners_check_log_is_never_read_or_written_and_the_verdict_column_is_empty()
    {
        var (_, folder, _) = await RunAsync();

        folder.Ops.ShouldNotContain(o => o.EndsWith(PackFiles.CheckLogName, StringComparison.Ordinal));
        var lines = Text(folder, DiskPackFolder.LogName).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var header = lines[0].Split(',');
        lines.Length.ShouldBe(2);
        header.Take(PackFiles.CheckLogColumns.Count).ShouldBe(PackFiles.CheckLogColumns);
        header.Skip(PackFiles.CheckLogColumns.Count).ShouldBe(AssistLog.StoredColumns);
        var cells = lines[1].Split(',');
        cells[Array.IndexOf(header, "verdict")].ShouldBeEmpty();
        cells[Array.IndexOf(header, "signed_on")].ShouldBeEmpty();
        cells[Array.IndexOf(header, "chart")].ShouldBe("auto:fake");
        cells[Array.IndexOf(header, "seen_entry_time")].ShouldBe("2026-03-02 09:45");
        cells[Array.IndexOf(header, "seen_exit_reason")].ShouldBe("square_off");
        cells[Array.IndexOf(header, "stored_seen_entry_ist")].ShouldBe("2026-03-02 09:45");
        cells[Array.IndexOf(header, "stored_seen_exit_reason")].ShouldBe("square_off");
    }

    [Fact]
    public void The_disk_folder_refuses_to_write_the_owners_log()
    {
        using var temp = new TempFolder();
        Directory.CreateDirectory(temp.Root);
        var disk = new DiskPackFolder(temp.Root);

        Should.Throw<InvalidOperationException>(() => disk.Write(PackFiles.CheckLogName, [1]));
        Should.Throw<InvalidOperationException>(() => disk.Write(PackFiles.EngineValuesName, [1]));
        disk.Write(DiskPackFolder.LogName, [1]);
        File.Exists(Path.Combine(temp.Root, DiskPackFolder.LogName)).ShouldBeTrue();
    }

    [Fact]
    public async Task A_rerun_on_the_same_inputs_gives_the_same_bytes_and_does_no_second_fetch()
    {
        var (_, folder, first) = await RunAsync();
        var before = folder.Files.ToDictionary(f => f.Key, f => f.Value);
        var second = new FakeSecond(_ => throw new InvalidOperationException("the cache should have served this"));

        var outcome = await AssistRunner.RunAsync(folder, second, new FakeStored(BuyDay()), TestContext.Current.CancellationToken);

        first.Calls.Count.ShouldBe(1);
        second.Calls.ShouldBeEmpty();
        outcome.Exit.ShouldBe(0);
        foreach (var name in new[] { AssistBarsCsv.CacheName, DiskPackFolder.LogName, DiskPackFolder.ReportName, DiskPackFolder.ManifestName })
        {
            folder.Files[name].ShouldBe(before[name], name);
        }
    }

    [Fact]
    public async Task One_call_is_made_per_distinct_symbol_and_date()
    {
        string[] symbols = ["NSE:AAA", "NSE:BBB", "NSE:AAA"];
        var folder = Folder(symbols, EngineValues(Read(BuyDay()), symbols));
        var second = new FakeSecond(_ => new SecondDay(BuyDay(), null));

        await AssistRunner.RunAsync(folder, second, new FakeStored(BuyDay()), TestContext.Current.CancellationToken);

        second.Calls.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_planted_bar_difference_is_bars_differ_and_names_the_minute()
    {
        var stored = BuyDay();
        stored[31] = stored[31] with { High = 105m };

        var (_, folder, _) = await RunAsync(stored: stored);

        Verdict(folder, Verdicts.BarsDiffer).ShouldBe(Verdicts.BarsDiffer);
        Text(folder, DiskPackFolder.ReportName).ShouldContain("09:46 differs by 50 ticks");
    }

    [Fact]
    public async Task A_planted_one_tick_difference_is_not_a_verdict_but_is_counted()
    {
        var stored = BuyDay();
        stored[0] = stored[0] with { High = stored[0].High + 0.05m };

        var (_, folder, _) = await RunAsync(stored: stored);

        Verdict(folder, Verdicts.AgreeAll).ShouldBe(Verdicts.AgreeAll);
        Text(folder, DiskPackFolder.ReportName).ShouldContain("1 differ, gap median 0 ticks and max 1 tick");
    }

    [Fact]
    public async Task A_constant_price_level_offset_is_agree_all_with_a_note()
    {
        var stored = BuyDay().Select(b => b with { Open = b.Open * 0.98m, High = b.High * 0.98m, Low = b.Low * 0.98m, Close = b.Close * 0.98m }).ToList();

        var (_, folder, _) = await RunAsync(stored: stored, engine: EngineValues(Read(stored), One));

        Verdict(folder, Verdicts.AgreeAll).ShouldBe(Verdicts.AgreeAll);
        Text(folder, DiskPackFolder.ReportName).ShouldContain("price level offset: stored is 0.98000 x second source");
        Text(folder, DiskPackFolder.ReportName).ShouldContain("1 rows carry a price level offset note");
    }

    [Fact]
    public async Task An_offset_that_changes_inside_the_day_is_bars_differ()
    {
        var stored = BuyDay().Select((b, i) => i >= 20 ? b : b with { Open = b.Open * 0.98m, High = b.High * 0.98m, Low = b.Low * 0.98m, Close = b.Close * 0.98m }).ToList();

        var (_, folder, _) = await RunAsync(stored: stored, engine: EngineValues(Read(stored), One));

        Verdict(folder, Verdicts.BarsDiffer).ShouldBe(Verdicts.BarsDiffer);
        Text(folder, DiskPackFolder.ReportName).ShouldContain("price level not constant");
    }

    [Fact]
    public async Task Small_multi_tick_feed_noise_with_the_reader_equal_to_the_engine_is_agree_all_with_a_note()
    {
        var stored = BuyDay();
        stored[2] = stored[2] with { High = stored[2].High + 0.30m, Low = stored[2].Low - 0.30m };

        var (_, folder, _) = await RunAsync(stored: stored);

        Verdict(folder, Verdicts.AgreeAll).ShouldBe(Verdicts.AgreeAll);
        Text(folder, DiskPackFolder.ReportName).ShouldContain("09:17 differs by 6 ticks");
        Text(folder, DiskPackFolder.ReportName).ShouldContain("1 of 1 rows carry a data note");
    }

    [Fact]
    public async Task A_data_only_difference_between_the_sources_is_data_sensitive_and_names_the_fields()
    {
        var second = BuyDay();
        second[10] = second[10] with { Low = 98.5m };

        var (_, folder, _) = await RunAsync(second: _ => new SecondDay(second, null));

        Verdict(folder, Verdicts.DataSensitive).ShouldBe(Verdicts.DataSensitive);
        var report = Text(folder, DiskPackFolder.ReportName);
        report.ShouldContain("reader on the second source differs from the engine (rows, whatever their verdict): 1");
        report.ShouldContain("reader on the stored bars differs from the engine (rows, whatever their verdict): 0");
        report.ShouldContain("stop");
    }

    [Fact]
    public async Task A_difference_on_the_stored_bars_is_reader_differs_even_with_a_gross_bar_problem_counted_separately()
    {
        var engine = EngineValues(Read(BuyDay()), One, cells => cells["stop_price"] = "98.5");
        var second = BuyDay();
        second[31] = second[31] with { High = 105m };

        var (_, folder, _) = await RunAsync(second: _ => new SecondDay(second, null), engine: engine);

        Verdict(folder, Verdicts.ReaderDiffers).ShouldBe(Verdicts.ReaderDiffers);
        Text(folder, DiskPackFolder.ReportName).ShouldContain("rows with a gross bar problem (rows, whatever their verdict): 1");
    }

    [Fact]
    public async Task A_stored_day_with_no_trade_against_an_engine_trade_is_reader_differs()
    {
        var flat = BuyDay().Select(b => b with { Open = 100m, High = 100m, Low = 100m, Close = 100m }).ToList();

        var (_, folder, _) = await RunAsync(stored: flat);

        Verdict(folder, Verdicts.ReaderDiffers).ShouldBe(Verdicts.ReaderDiffers);
        Text(folder, DiskPackFolder.ReportName).ShouldContain("no trade on the stored bars");
    }

    [Fact]
    public async Task A_second_source_the_reader_cannot_use_while_the_stored_reading_differs_is_reader_differs()
    {
        var engine = EngineValues(Read(BuyDay()), One, cells => cells["stop_price"] = "98.5");
        var broken = BuyDay();
        broken.Add(new ReaderBar(broken[^1].EpochSeconds + 86400, 100m, 100m, 100m, 100m, 1m));

        var (_, folder, _) = await RunAsync(second: _ => new SecondDay(broken, null), engine: engine);

        Verdict(folder, Verdicts.ReaderDiffers).ShouldBe(Verdicts.ReaderDiffers);
    }

    [Fact]
    public async Task A_minute_missing_in_the_second_source_is_bars_differ()
    {
        var second = BuyDay();
        second.RemoveAt(31);

        var (_, folder, _) = await RunAsync(second: _ => new SecondDay(second, null));

        Verdict(folder, Verdicts.BarsDiffer).ShouldBe(Verdicts.BarsDiffer);
        Text(folder, DiskPackFolder.ReportName).ShouldContain("missing in second source");
    }

    [Fact]
    public async Task The_console_prints_the_same_independent_counters_as_the_report()
    {
        var (outcome, folder, _) = await RunAsync();

        foreach (var line in AssistCounters.Lines([]))
        {
            outcome.Lines.ShouldContain(line);
        }

        outcome.Lines.ShouldContain("rows with a gross bar problem (rows, whatever their verdict): 0");
        Text(folder, DiskPackFolder.ReportName).ShouldContain("rows with a gross bar problem (rows, whatever their verdict): 0");
    }

    [Fact]
    public async Task A_missing_stored_minute_is_bars_differ()
    {
        var stored = BuyDay();
        stored.RemoveAt(31);

        var (_, folder, _) = await RunAsync(stored: stored);

        Verdict(folder, Verdicts.BarsDiffer).ShouldBe(Verdicts.BarsDiffer);
        Text(folder, DiskPackFolder.ReportName).ShouldContain("missing in stored");
    }

    [Fact]
    public async Task A_field_of_the_engine_that_the_reader_does_not_reproduce_is_reader_differs_with_the_field()
    {
        var engine = EngineValues(Read(BuyDay()), One, cells => cells["stop_price"] = "98.5");

        var (_, folder, _) = await RunAsync(engine: engine);

        Verdict(folder, Verdicts.ReaderDiffers).ShouldBe(Verdicts.ReaderDiffers);
        Text(folder, DiskPackFolder.ReportName).ShouldContain("stop");
    }

    [Fact]
    public async Task A_day_where_the_reader_finds_no_trade_is_cannot_read()
    {
        var flat = BuyDay().Select(b => b with { Open = 100m, High = 100m, Low = 100m, Close = 100m }).ToList();

        var (_, folder, _) = await RunAsync(second: _ => new SecondDay(flat, null), stored: flat);

        Verdict(folder, Verdicts.CannotRead).ShouldBe(Verdicts.CannotRead);
    }

    [Fact]
    public async Task A_day_the_second_source_cannot_give_is_source_unavailable_and_the_run_goes_on()
    {
        string[] symbols = ["NSE:AAA", "NSE:BBB"];

        var (outcome, folder, _) = await RunAsync(symbols, s => s == "NSE:BBB" ? SecondDay.Missing("Dhan refused the access token or the subscription") : new SecondDay(BuyDay(), null));

        outcome.Exit.ShouldBe(0);
        outcome.Lines.ShouldContain("source_unavailable: 1");
        outcome.Lines.ShouldContain("agree_all: 1");
        var log = Text(folder, DiskPackFolder.LogName);
        log.ShouldContain("source_unavailable: Dhan refused the access token or the subscription");
        log.ShouldNotContain("token=");
    }

    [Fact]
    public async Task An_engine_values_file_that_differs_from_the_manifest_is_refused_after_the_log_is_written()
    {
        var folder = Folder(One);
        folder.Files[PackFiles.EngineValuesName] = [.. folder.Files[PackFiles.EngineValuesName], (byte)'\n'];

        var outcome = await AssistRunner.RunAsync(folder, new FakeSecond(_ => new SecondDay(BuyDay(), null)), new FakeStored(BuyDay()), TestContext.Current.CancellationToken);

        outcome.Exit.ShouldBe(2);
        outcome.Lines[0].ShouldContain("differs from manifest.json");
        folder.Ops.ShouldContain("write assist-log.csv");
        folder.Ops.ShouldNotContain("write assist-report.html");
    }

    [Fact]
    public void The_manifest_check_of_the_assist_mode_never_opens_the_engine_values_file()
    {
        using var temp = new TempFolder();
        var engine = EngineValues(Read(BuyDay()), One);
        var engineFile = Path.Combine(temp.Path, PackFiles.EngineValuesName);
        File.WriteAllBytes(engineFile, engine);
        File.WriteAllBytes(Path.Combine(temp.Path, "manifest.json"), Manifest(engine));
        File.SetUnixFileMode(engineFile, UnixFileMode.None);
        try
        {
            VerifyPackCheck.VerifyManifest(temp.Path, PackFiles.EngineValuesName).ShouldBeNull();
            VerifyPackCheck.VerifyManifest(temp.Path).ShouldNotBeNull();
        }
        finally
        {
            File.SetUnixFileMode(engineFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [Fact]
    public async Task A_pack_json_that_is_not_a_pack_is_refused_with_exit_2_and_nothing_is_written()
    {
        var folder = Folder(One);
        folder.Files[PackFiles.PackJsonName] = Encoding.UTF8.GetBytes("{}");

        var outcome = await AssistRunner.RunAsync(folder, new FakeSecond(_ => new SecondDay(BuyDay(), null)), new FakeStored(BuyDay()), TestContext.Current.CancellationToken);

        outcome.Exit.ShouldBe(2);
        folder.Ops.ShouldNotContain(o => o.StartsWith("write", StringComparison.Ordinal));
    }

    [Fact]
    public void The_chart_sample_has_every_row_that_is_not_agree_all_and_a_sell_when_one_agrees()
    {
        PackedTrade Pack(int seq, ReaderSide side) => new(seq, "ORB|S" + seq + "|" + side + "|2026-03-02", "NSE:S" + seq, side, Date, "ORB", 15, 2m);
        RowVerdict V(int seq, ReaderSide side, string verdict) => new(new AssistRow(Pack(seq, side), null, "auto:x", null, 0.05m, []), verdict, []);
        List<RowVerdict> rows = [V(1, ReaderSide.Buy, Verdicts.BarsDiffer), V(30, ReaderSide.Buy, Verdicts.ReaderDiffers), V(31, ReaderSide.Buy, Verdicts.DataSensitive), V(32, ReaderSide.Buy, Verdicts.CannotRead), V(33, ReaderSide.Buy, Verdicts.SourceUnavailable), .. Enumerable.Range(2, 12).Select(i => V(i, ReaderSide.Buy, Verdicts.AgreeAll)), V(20, ReaderSide.Sell, Verdicts.AgreeAll)];

        var picked = AssistReport.Sample("seed-1", rows);

        picked.ShouldContain(1);
        picked.ShouldContain(30);
        picked.ShouldContain(31);
        picked.ShouldContain(32);
        picked.ShouldContain(33);
        picked.Count.ShouldBe(AssistReport.ChartRows + 1);
        picked.ShouldContain(20);
        AssistReport.Sample("seed-1", rows).ShouldBe(picked);
    }
}
