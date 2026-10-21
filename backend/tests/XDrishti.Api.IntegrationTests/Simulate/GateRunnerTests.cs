using System.Globalization;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using XDrishti.Api.Simulate.Assist;
using XDrishti.Api.Simulate.Gate;
using XDrishti.SimExport.Verification;
using XDrishti.SimReader;
using static XDrishti.Api.IntegrationTests.Simulate.AssistSupport;

namespace XDrishti.Api.IntegrationTests.Simulate;

/// <summary>P3-09d acceptance 5 to 8: the gate through the runner on a pack fixture (in memory, and on disk with a folder second source): the statuses, the spot lines, the determinism, the untouched owner's log and the files it writes.</summary>
[UnsupportedOSPlatform("windows")]
public sealed class GateRunnerTests
{
    private static readonly string[] One = [Symbol];

    private sealed class DatedStored(Func<DateOnly, IReadOnlyList<ReaderBar>> bars) : IStoredDays
    {
        public ValueTask<StoredDay> GetAsync(string symbol, DateOnly date, CancellationToken cancellationToken) => ValueTask.FromResult(new StoredDay(0.05m, bars(date)));
    }

    private static AssistSupport.MemoryFolder WithSpots(AssistSupport.MemoryFolder folder, params (string Key, string Date)[] spots)
    {
        var text = Encoding.UTF8.GetString(folder.Files[PackFiles.PackJsonName]);
        var list = string.Join(',', spots.Select((s, i) => $"{{\"seq\":{i + 2},\"key\":\"{s.Key}\",\"symbol\":\"{Symbol}\",\"date\":\"{s.Date}\"}}"));
        folder.Files[PackFiles.PackJsonName] = Encoding.UTF8.GetBytes(text.Replace("\"spots\":[]", "\"spots\":[" + list + "]", StringComparison.Ordinal));
        return Seal(folder);
    }

    private static AssistSupport.MemoryFolder Fixture(byte[]? engine = null) => WithSpots(Folder(One, engine));

    private static Task<GateOutcome> RunAsync(AssistSupport.MemoryFolder folder, Func<string, SecondDay>? second = null, IStoredDays? stored = null) =>
        GateRunner.RunAsync(folder, new FakeSecond(second ?? (_ => new SecondDay(BuyDay(), null))), stored ?? new FakeStored(BuyDay()), TestContext.Current.CancellationToken);

    private static JsonElement Json(AssistSupport.MemoryFolder folder) => JsonDocument.Parse(folder.Files[GateRunner.JsonName]).RootElement;

    [Fact]
    public async Task A_clean_pack_is_PASS_and_writes_the_two_gate_files_with_the_hashes_of_the_inputs()
    {
        var folder = Fixture();

        var outcome = await RunAsync(folder);

        outcome.Exit.ShouldBe(0, string.Join('\n', outcome.Lines));
        outcome.Status.ShouldBe(GateStatus.Pass);
        var json = Json(folder);
        json.GetProperty("status").GetString().ShouldBe("PASS");
        json.GetProperty("exit_code").GetInt32().ShouldBe(0);
        json.GetProperty("limit").GetString()!.ShouldContain("not a chart-by-eye check");
        var inputs = json.GetProperty("inputs");
        inputs.GetProperty(PackFiles.PackJsonName).GetString().ShouldBe(AssistRunner.Hash(folder.Files[PackFiles.PackJsonName]));
        inputs.GetProperty(PackFiles.EngineValuesName).GetString().ShouldBe(AssistRunner.Hash(folder.Files[PackFiles.EngineValuesName]));
        inputs.GetProperty(DiskPackFolder.LogName).GetString().ShouldBe(AssistRunner.Hash(folder.Files[DiskPackFolder.LogName]));
        inputs.GetProperty(AssistBarsCsv.CacheName).GetString().ShouldBe(AssistRunner.Hash(folder.Files[AssistBarsCsv.CacheName]));
        Encoding.UTF8.GetString(folder.Files[GateRunner.MarkdownName]).ShouldStartWith("# Gate decision: PASS");
    }

    [Fact]
    public async Task The_owners_log_is_never_read_or_written_and_no_verdict_is_written()
    {
        var folder = Fixture();
        var log = folder.Files[PackFiles.CheckLogName].ToArray();

        await RunAsync(folder);

        folder.Ops.ShouldNotContain(o => o.EndsWith(PackFiles.CheckLogName, StringComparison.Ordinal));
        folder.Files[PackFiles.CheckLogName].ShouldBe(log);
        Encoding.UTF8.GetString(folder.Files[DiskPackFolder.LogName]).Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1).ShouldAllBe(l => l.Split(',')[16] == string.Empty);
    }

    [Fact]
    public async Task The_same_inputs_give_the_same_bytes()
    {
        var first = Fixture();
        var second = Fixture();

        await RunAsync(first);
        await RunAsync(second);

        first.Files[GateRunner.JsonName].ShouldBe(second.Files[GateRunner.JsonName]);
        first.Files[GateRunner.MarkdownName].ShouldBe(second.Files[GateRunner.MarkdownName]);
        var again = first.Files[GateRunner.JsonName].ToArray();
        await RunAsync(first);
        first.Files[GateRunner.JsonName].ShouldBe(again);
    }

    [Fact]
    public async Task A_stored_reading_that_differs_from_the_engine_is_BLOCKED_DEFECT_exit_12()
    {
        var folder = Fixture(EngineValues(Read(BuyDay()), One, c => c["exit_reason"] = "stop"));

        var outcome = await RunAsync(folder);

        outcome.Exit.ShouldBe(12);
        outcome.Status.ShouldBe(GateStatus.BlockedDefect);
        Json(folder).GetProperty("defects")[0].GetProperty("detail").GetString()!.ShouldContain("exit reason");
    }

    [Fact]
    public async Task A_planted_gross_gap_is_DATA_REPAIR_NEEDED_exit_11_and_names_the_day()
    {
        var stored = BuyDay();
        stored[31] = stored[31] with { High = 105m };

        var outcome = await RunAsync(Fixture(), stored: new FakeStored(stored));

        outcome.Exit.ShouldBe(11);
        outcome.Lines.ShouldContain(l => l.Contains("NSE:AAA 2026-03-02", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_missing_second_source_is_RERUN_NEEDED_exit_10()
    {
        var outcome = await RunAsync(Fixture(), _ => SecondDay.Missing("HTTP 401"));

        outcome.Exit.ShouldBe(10);
        outcome.Status.ShouldBe(GateStatus.RerunNeeded);
    }

    [Fact]
    public async Task A_data_only_difference_is_PASS_WITH_DATA_FINDINGS_exit_0()
    {
        var second = BuyDay();
        second[10] = second[10] with { Low = 98.5m };

        var folder = Fixture();
        var outcome = await RunAsync(folder, _ => new SecondDay(second, null));

        outcome.Exit.ShouldBe(0);
        outcome.Status.ShouldBe(GateStatus.PassWithDataFindings);
        Json(folder).GetProperty("counts").GetProperty("data_findings").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task The_spot_lines_are_checked_from_the_stored_bars_and_a_contradiction_blocks()
    {
        const string Short = "spot|excluded_session:short_session|NSE:AAA|2026-03-03";
        const string Flagged = "spot|flagged_day|NSE:AAA|2026-03-05";
        const string Open = "spot|open_at_end|NSE:AAA|2026-03-06";
        var folder = WithSpots(Folder(One), (Short, "2026-03-03"), (Flagged, "2026-03-05"), (Open, "2026-03-06"));
        IReadOnlyList<ReaderBar> Day(DateOnly d)
        {
            var shift = d.DayNumber - Date.DayNumber;
            return d == Date ? BuyDay() : [.. BuyDay().Take(d == new DateOnly(2026, 3, 3) ? 100 : 375).Select(b => b with { EpochSeconds = b.EpochSeconds + (shift * 86400L), Open = d == new DateOnly(2026, 3, 5) ? 60m : 102.5m, High = 102.5m, Low = 60m, Close = 102.5m })];
        }

        var outcome = await RunAsync(folder, stored: new DatedStored(Day));

        outcome.Status.ShouldBe(GateStatus.Pass, string.Join('\n', outcome.Lines));
        var spots = Json(folder).GetProperty("spots").EnumerateArray().ToDictionary(s => s.GetProperty("key").GetString()!, s => s.GetProperty("outcome").GetString());
        spots[Short].ShouldBe("confirmed");
        spots[Flagged].ShouldBe("confirmed");
        spots[Open].ShouldBe("not_machine_checkable");
        Json(folder).GetProperty("counts").GetProperty("spots_not_machine_checkable").GetInt32().ShouldBe(1);

        var complete = await RunAsync(WithSpots(Folder(One), (Short, "2026-03-03")), stored: new FakeStored(BuyDay()));
        complete.Exit.ShouldBe(12);
    }

    [Fact]
    public async Task A_pack_json_without_spots_is_refused_and_nothing_is_decided()
    {
        var folder = Fixture();
        folder.Files[PackFiles.PackJsonName] = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(PackJson(Symbol)).Replace(",\"spots\":[]", string.Empty, StringComparison.Ordinal));

        var outcome = await RunAsync(folder);

        outcome.Exit.ShouldBe(2);
        outcome.Status.ShouldBeNull();
        folder.Files.ShouldNotContainKey(GateRunner.JsonName);
    }

    [Fact]
    public async Task On_disk_with_a_folder_source_the_gate_writes_only_its_files_and_leaves_the_owners_log_byte_identical()
    {
        using var temp = new TempFolder();
        var pack = Path.Combine(temp.Path, "pack");
        var second = Path.Combine(temp.Path, "second");
        Directory.CreateDirectory(pack);
        Directory.CreateDirectory(second);
        var memory = WithSpots(Folder(One));
        foreach (var (name, bytes) in memory.Files)
        {
            File.WriteAllBytes(Path.Combine(pack, name), bytes);
        }

        var csv = new StringBuilder("time,open,high,low,close,volume\n");
        foreach (var b in BuyDay())
        {
            csv.Append(DateTimeOffset.FromUnixTimeSeconds(b.EpochSeconds).ToOffset(TimeSpan.FromMinutes(330)).ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)).Append(',').Append(string.Join(',', new[] { b.Open, b.High, b.Low, b.Close, b.Volume }.Select(v => v.ToString(CultureInfo.InvariantCulture)))).Append('\n');
        }

        File.WriteAllText(Path.Combine(second, "AAA_2026-03-02.csv"), csv.ToString());
        var log = File.ReadAllBytes(Path.Combine(pack, PackFiles.CheckLogName));

        var outcome = await GateRunner.RunAsync(new DiskPackFolder(pack), new FolderSecondSource(second), new FakeStored(BuyDay()), TestContext.Current.CancellationToken);

        outcome.Exit.ShouldBe(0, string.Join('\n', outcome.Lines));
        File.ReadAllBytes(Path.Combine(pack, PackFiles.CheckLogName)).ShouldBe(log);
        File.Exists(Path.Combine(pack, GateRunner.JsonName)).ShouldBeTrue();
        File.Exists(Path.Combine(pack, GateRunner.MarkdownName)).ShouldBeTrue();
        Directory.GetFiles(pack).Select(Path.GetFileName).Order().ShouldBe(
            new[] { PackFiles.CheckLogName, PackFiles.EngineValuesName, "manifest.json", PackFiles.PackJsonName, AssistBarsCsv.CacheName, DiskPackFolder.LogName, DiskPackFolder.ManifestName, DiskPackFolder.ReportName, GateRunner.JsonName, GateRunner.MarkdownName }.Order());
    }

    private static async Task<(GateOutcome Outcome, AssistSupport.MemoryFolder Folder)> IntegrityAsync(AssistSupport.MemoryFolder folder)
    {
        var outcome = await RunAsync(folder);
        return (outcome, folder);
    }

    [Fact]
    public async Task A_truncated_pack_json_does_not_pass()
    {
        string[] two = ["NSE:AAA", "NSE:BBB"];
        var folder = Fixture();
        var full = Folder(two);
        folder.Files[PackFiles.EngineValuesName] = full.Files[PackFiles.EngineValuesName];
        folder.Files["manifest.json"] = full.Files["manifest.json"];

        var (outcome, run) = await IntegrityAsync(folder);

        outcome.Status.ShouldBe(GateStatus.BlockedDefect);
        Json(run).GetProperty("defects").EnumerateArray().Select(d => d.GetProperty("detail").GetString()).ShouldContain(d => d!.Contains("pack.json differs from manifest.json", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_edited_pack_json_does_not_pass()
    {
        var folder = Fixture();
        folder.Files[PackFiles.PackJsonName] = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(folder.Files[PackFiles.PackJsonName]).Replace(PackKey, new string('b', 64), StringComparison.Ordinal));

        var (outcome, run) = await IntegrityAsync(folder);

        outcome.Status.ShouldBe(GateStatus.BlockedDefect);
        Json(run).GetProperty("defects").EnumerateArray().Select(d => d.GetProperty("detail").GetString()).ShouldContain(d => d!.Contains("pack_key", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Engine_trades_that_the_pack_does_not_list_do_not_pass()
    {
        string[] two = ["NSE:AAA", "NSE:BBB"];
        var folder = Fixture();
        folder.Files[PackFiles.EngineValuesName] = EngineValues(Read(BuyDay()), two);
        Seal(folder);

        var (outcome, run) = await IntegrityAsync(folder);

        outcome.Status.ShouldBe(GateStatus.BlockedDefect);
        Json(run).GetProperty("defects").EnumerateArray().Select(d => d.GetProperty("detail").GetString()).ShouldContain(d => d!.Contains("engine-values.csv has a trade that pack.json does not list", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_pack_row_that_the_engine_values_do_not_have_does_not_pass()
    {
        string[] two = ["NSE:AAA", "NSE:BBB"];
        var folder = Fixture();
        folder.Files[PackFiles.PackJsonName] = PackJson(two);
        folder.Files[PackFiles.PackJsonName] = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(folder.Files[PackFiles.PackJsonName]));
        Seal(folder);

        var (outcome, run) = await IntegrityAsync(folder);

        outcome.Status.ShouldBe(GateStatus.BlockedDefect);
        Json(run).GetProperty("defects").EnumerateArray().Select(d => d.GetProperty("detail").GetString()).ShouldContain(d => d!.Contains("pack.json lists a trade that engine-values.csv does not have", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_empty_spots_array_is_reported_loudly_as_not_machine_checkable()
    {
        var folder = Fixture();

        var outcome = await RunAsync(folder);

        outcome.Status.ShouldBe(GateStatus.Pass);
        outcome.Lines.ShouldContain(l => l.StartsWith("spot lines in the pack: 0 (NONE", StringComparison.Ordinal));
        Json(folder).GetProperty("counts").GetProperty("spots_not_machine_checkable").GetInt32().ShouldBe(1);
        Json(folder).GetProperty("notes").GetArrayLength().ShouldBe(2);
    }
}
