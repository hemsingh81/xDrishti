using System.Security.Cryptography;
using System.Text.Json;
using XDrishti.Api.Simulate;
using XDrishti.Domain.Common;
using XDrishti.Domain.Research;
using XDrishti.SimExport.Manifest;
using XDrishti.Tests.Shared;

namespace XDrishti.Api.IntegrationTests.Simulate;

/// <summary>
/// P3-04c at the verb, with fake handlers (no database): the flag, the pipeline order, the folder with its cards and index, the extra manifest lines, the format string, the overwrite policy, a stale second read, the log lines and the scans
/// (criteria 1, 7, 8, 10 and 6 of the spec). The database half is in <see cref="SimulateDatabaseTests"/>.
/// </summary>
public sealed class SimulateCardsTests : IDisposable
{
    private const string Common = "--strategy ORB --symbols AAA --from 2024-04-01 --to 2026-04-30 --gross";

    private readonly FakeVerb _verb = new();

    public SimulateCardsTests() => _verb.AddInstrument("AAA");

    public void Dispose() => _verb.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string RunFolder() => Directory.EnumerateDirectories(Path.Combine(_verb.ExportRoot, "sim")).Single(d => !Path.GetFileName(d).StartsWith('.'));

    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    /// <summary>Every file of a tree, relative, with its hash.</summary>
    private static Dictionary<string, string> Snapshot(string folder) =>
        Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).ToDictionary(f => Path.GetRelativePath(folder, f).Replace('\\', '/'), Hash)
            : [];

    // criterion 1: the flag

    [Theory]
    [InlineData("0", 0)]
    [InlineData("5", 5)]
    [InlineData("200", 200)]
    [InlineData("500", 500)]
    public void The_flag_takes_a_whole_number_from_0_to_500(string value, int expected)
    {
        var parsed = SimulateOptions.Parse(Split(Common + " --cards " + value));

        parsed.Options!.Cards.ShouldBe(expected);
        SimulateOptions.Parse(Split(Common + " --cards=" + value)).Options!.Cards.ShouldBe(expected);
    }

    [Theory]
    [InlineData("501")]
    [InlineData("007")]
    [InlineData("00")]
    [InlineData("-1")]
    [InlineData("abc")]
    [InlineData("1.5")]
    [InlineData("")]
    [InlineData("0005000")]
    [InlineData("+5")]
    [InlineData("1e2")]
    public void Anything_else_is_a_usage_error(string value)
    {
        var parsed = SimulateOptions.Parse(Split(Common + " --cards " + value));

        parsed.IsValid.ShouldBeFalse();
        parsed.Errors.ShouldNotBeEmpty();
    }

    [Fact]
    public void The_flag_given_twice_is_a_usage_error_and_the_default_is_200_and_the_usage_text_names_it()
    {
        SimulateOptions.Parse(Split(Common + " --cards 5 --cards 6")).Errors.ShouldContain("'--cards' is given more than once");
        SimulateOptions.Parse(Split(Common)).Options!.Cards.ShouldBe(200);
        SimulateOptions.Usage.ShouldContain("--cards <0..500>");
    }

    private static string[] Split(string args) => args.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    // criterion 7: the folder

    [Fact]
    public async Task Cards_0_writes_no_cards_and_no_index_and_the_manifest_has_the_cap_and_the_number_of_trades_only()
    {
        var exit = await _verb.RunAsync(Common + " --cards 0", Ct);

        exit.ShouldBe(0, _verb.Error.ToString());
        var folder = RunFolder();
        Directory.Exists(Path.Combine(folder, "cards")).ShouldBeFalse();
        File.Exists(Path.Combine(folder, "index.html")).ShouldBeFalse();
        var manifest = ExportManifest.ParseBytes(File.ReadAllBytes(Path.Combine(folder, "manifest.json"))).Value;
        manifest.Format.ShouldBe("xd-sim-export-3");
        manifest.ExtraLines.ShouldBe(["cards.cap=0", "cards.trades=2"]);
        _verb.CardsHandler.Received.ShouldBeEmpty("no card is wanted, so nothing is read a second time");
        _verb.Out.ToString().ShouldContain("trade cards: none (--cards 0)");
    }

    [Fact]
    public async Task Cards_5_writes_the_chosen_cards_named_by_the_rule_an_index_that_links_to_each_and_the_extra_lines_in_the_manifest()
    {
        var exit = await _verb.RunAsync(Common + " --cards 5", Ct);

        exit.ShouldBe(0, _verb.Error.ToString());
        var run = FabricatedRun.Standard();
        var folder = RunFolder();
        var names = Directory.EnumerateFiles(Path.Combine(folder, "cards")).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList();
        names.ShouldBe(["0001-AAA-buy-2026-03-02-target.html", "0002-M_M-sell-2026-03-03-stop.html"]);
        var index = File.ReadAllText(Path.Combine(folder, "index.html"));
        foreach (var name in names)
        {
            index.ShouldContain("href=\"cards/" + name + "\"");
            File.Exists(Path.Combine(folder, "cards", name!)).ShouldBeTrue();
        }

        var manifest = ExportManifest.ParseBytes(File.ReadAllBytes(Path.Combine(folder, "manifest.json"))).Value;
        manifest.Format.ShouldBe("xd-sim-export-3");
        manifest.Entries.Select(e => e.Name).Order(StringComparer.Ordinal).ShouldBe(["README.txt", "cards/0001-AAA-buy-2026-03-02-target.html", "cards/0002-M_M-sell-2026-03-03-stop.html", "index.html", "open_at_end.csv", "run.json", "trades.csv"]);
        foreach (var entry in manifest.Entries)
        {
            entry.Sha256.ShouldBe(Hash(Path.Combine(folder, entry.Name)), entry.Name);
            entry.Bytes.ShouldBe(new FileInfo(Path.Combine(folder, entry.Name)).Length, entry.Name);
        }

        manifest.ExtraLines.ShouldBe(
        [
            "cards.cap=5",
            "cards.trades=2",
            "cards.selected=" + run.Trades[0].Trade.Key + "|target-buy",
            "cards.selected=" + run.Trades[1].Trade.Key + "|stop-sell",
        ]);
        manifest.ExportKey.ShouldBe(ExportKey.Of("xd-sim-export-3", run.RunKey, null, manifest.Entries, manifest.ExtraLines));
        using var json = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder, "run.json")));
        json.RootElement.GetProperty("export").GetProperty("format").GetString().ShouldBe("xd-sim-export-3");
        _verb.CardsHandler.Received.Single().TradeKeys.ShouldBe([run.Trades[0].Trade.Key, run.Trades[1].Trade.Key]);
        _verb.Out.ToString().ShouldContain("trade cards: 2 written (up to 5 asked for), with index.html");
    }

    [Fact]
    public async Task The_files_and_folders_have_the_modes_of_the_export_library()
    {
        await _verb.RunAsync(Common + " --cards 5", Ct);

        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var folder = RunFolder();
        foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            File.GetUnixFileMode(file).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite, file);
        }

        File.GetUnixFileMode(Path.Combine(folder, "cards")).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    [Fact]
    public async Task Two_runs_into_two_roots_are_identical_byte_for_byte_and_a_second_run_in_the_same_root_is_unchanged_after_hashing_every_card()
    {
        using var other = new FakeVerb();
        other.AddInstrument("AAA");

        await _verb.RunAsync(Common + " --cards 5", Ct);
        await other.RunAsync(Common + " --cards 5", Ct);
        var again = await _verb.RunAsync(Common + " --cards 5", Ct);

        Snapshot(RunFolder()).ShouldBe(Snapshot(Directory.EnumerateDirectories(Path.Combine(other.ExportRoot, "sim")).Single()));
        Snapshot(RunFolder()).Keys.Count.ShouldBe(8);
        again.ShouldBe(0);
        _verb.Out.ToString().ShouldContain("export: unchanged");
    }

    // criterion 8: overwrite and stale

    [Fact]
    public async Task Another_cards_value_is_exit_1_and_nothing_changes_and_force_renames_the_old_folder_to_previous()
    {
        await _verb.RunAsync(Common + " --cards 5", Ct);
        var before = Snapshot(_verb.ExportRoot);

        var refused = await _verb.RunAsync(Common + " --cards 1", Ct);

        refused.ShouldBe(1);
        Snapshot(_verb.ExportRoot).ShouldBe(before);
        var forced = await _verb.RunAsync(Common + " --cards 1 --force", Ct);
        forced.ShouldBe(0, _verb.Error.ToString());
        var folders = Directory.EnumerateDirectories(Path.Combine(_verb.ExportRoot, "sim")).Select(Path.GetFileName).ToList();
        folders.Count.ShouldBe(2);
        var previous = folders.Single(f => f!.EndsWith(".previous", StringComparison.Ordinal));
        Directory.EnumerateFiles(Path.Combine(_verb.ExportRoot, "sim", previous!, "cards")).Count().ShouldBe(2, "the old export is kept whole");
        Directory.EnumerateFiles(Path.Combine(_verb.ExportRoot, "sim", folders.Single(f => !f!.EndsWith(".previous", StringComparison.Ordinal))!, "cards")).Count().ShouldBe(1);
    }

    [Fact]
    public async Task A_hand_edited_card_is_never_unchanged()
    {
        await _verb.RunAsync(Common + " --cards 5", Ct);
        var card = Directory.EnumerateFiles(Path.Combine(RunFolder(), "cards")).First();
        File.AppendAllText(card, "<!-- edited -->");

        var exit = await _verb.RunAsync(Common + " --cards 5", Ct);

        exit.ShouldBe(1, "a modified export is refused unless forced");
        _verb.Out.ToString().ShouldNotContain("export: unchanged");
    }

    [Fact]
    public async Task A_stale_second_read_is_exit_1_names_the_instrument_and_the_difference_writes_nothing_and_leaves_no_staging_folder()
    {
        _verb.Cards = _ => Result.Failure<IReadOnlyList<TradeCardData>>(SimulationErrors.CardsStale("NSE:AAA", "bars_changed", "the stored bars differ from the ones the run read"));

        var exit = await _verb.RunAsync(Common + " --cards 5", Ct);

        exit.ShouldBe(1);
        _verb.Error.ToString().ShouldContain("NSE:AAA: bars_changed: the stored bars differ");
        Directory.Exists(_verb.ExportRoot).ShouldBeFalse("nothing was written, not even the root");
        _verb.Out.ToString().ShouldBeEmpty();
        var stale = _verb.Logs.Lines.Single(l => l.EventId == 4411);
        stale.Arguments.Where(a => a.Key is "Symbol" or "Difference").Select(a => a.Value).ShouldBe(["NSE:AAA", "bars_changed"]);
        _verb.Logs.Lines.ShouldNotContain(l => l.EventId == 4410);
    }

    [Fact]
    public async Task The_second_read_comes_after_the_run_and_before_any_file_and_the_chosen_keys_are_the_selection()
    {
        var order = new List<string>();
        var original = _verb.Run;
        _verb.Run = q =>
        {
            order.Add("run");
            return original(q);
        };
        _verb.Cards = q =>
        {
            order.Add("cards:" + Directory.Exists(_verb.ExportRoot));
            return FakeVerb.FlatDays(q);
        };

        await _verb.RunAsync(Common + " --cards 1", Ct);

        order.ShouldBe(["run", "cards:False"], "the root does not exist yet when the cards are read");
        _verb.CardsHandler.Received.Single().TradeKeys.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_token_cancelled_while_the_cards_are_read_is_exit_130_and_leaves_no_run_folder_and_no_staging_folder()
    {
        using var cancel = new CancellationTokenSource();
        _verb.Cards = q =>
        {
            cancel.Cancel();
            return FakeVerb.FlatDays(q);
        };

        var exit = await _verb.RunAsync(Common + " --cards 5", cancel.Token);

        exit.ShouldBe(130);
        (Directory.Exists(_verb.ExportRoot) ? Directory.EnumerateFileSystemEntries(_verb.ExportRoot, "*", SearchOption.AllDirectories).ToList() : []).ShouldBeEmpty();
    }

    // criterion 10: logs and secrets

    [Fact]
    public async Task The_cards_line_has_the_id_4410_with_a_count_and_a_byte_count_and_no_price_bar_or_path()
    {
        await _verb.RunAsync(Common + " --cards 5", Ct);

        var line = _verb.Logs.Lines.Single(l => l.EventId == 4410);
        line.Arguments.Where(a => a.Key != "{OriginalFormat}").Select(a => (a.Key, a.Value!.GetType())).ShouldBe([("Cards", typeof(int)), ("Bytes", typeof(long))]);
        line.Text.ShouldNotContain(_verb.Root.Path);
        line.Text.ShouldNotContain(_verb.Password);
    }

    [Fact]
    public async Task The_database_password_is_in_no_card_and_not_in_the_index()
    {
        await _verb.RunAsync(Common + " --cards 5", Ct);

        foreach (var file in Directory.EnumerateFiles(RunFolder(), "*", SearchOption.AllDirectories))
        {
            File.ReadAllText(file).ShouldNotContain(_verb.Password, Case.Sensitive, file);
            File.ReadAllText(file).ShouldNotContain(_verb.Root.Path, Case.Sensitive, file);
        }
    }

    // criterion 6: no statistics

    [Fact]
    public async Task No_card_and_not_the_index_holds_a_word_of_a_statistic_or_a_figure_in_r_with_a_digit_before_it()
    {
        await _verb.RunAsync(Common + " --cards 5", Ct);

        foreach (var file in Directory.EnumerateFiles(RunFolder(), "*.html", SearchOption.AllDirectories))
        {
            ForbiddenWords.In(File.ReadAllText(file)).ShouldBeEmpty(file);
        }

        ForbiddenWords.In(_verb.Out.ToString()).ShouldBeEmpty("the console text too");
    }
}
