using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Research;
using XDrishti.Domain.Research;
using XDrishti.SimExport.Cards;
using XDrishti.SimExport.Manifest;
using XDrishti.SimExport.Verification;
using XDrishti.Tests.Shared;

namespace XDrishti.Api.IntegrationTests.Simulate;

/// <summary>
/// P3-09 criteria 7, 8, 9, 11, 13 and 16 against a real TimescaleDB: <c>verify-pack</c> called as <c>VerifyPackCommand.RunAsync</c> over the real host. The F1 day, the F2 day and a split instrument of P3-03 are seeded through the real stores; the
/// expected pack is the plan of the library over the run the handler gives for the same request, so a pack that drew from another run, or read another bar, would differ.
/// </summary>
public sealed partial class SimulateDatabaseTests
{
    private const string PackWindow = "--from 2026-03-02 --to 2026-03-04 --seed t1 --gross";

    private static readonly FillRulesInput Modelled = new(0.1, 1, 0.05, 2);

    private async Task<Outcome> PackAsync(string args, TempFolder temp, Action<IServiceCollection>? configure = null, string? root = null)
    {
        var logs = new CapturedLogs();
        var overrides = new Dictionary<string, string?> { ["Database:ConnectionString"] = Factory.AdminConnectionString, ["BrokerVault:Key"] = string.Empty };
        using var host = SimulateHost.Create(overrides, services =>
        {
            services.AddSingleton<ILogEventSink>(logs);
            configure?.Invoke(services);
        });
        var output = new StringWriter();
        var error = new StringWriter();

        var exit = await VerifyPackCommand.RunAsync(args.Split(' ', StringSplitOptions.RemoveEmptyEntries), host.Services, new SimulateConsole(output, error), root ?? temp.Root, Ct);

        return new Outcome(exit, output.ToString(), error.ToString(), [.. logs.All]);
    }

    private async Task<(Guid Abb, Guid Tcs, Guid Split, Guid Basket)> SeedPackDaysAsync(string basketName = "Lab")
    {
        var abb = await InstrumentAsync("ABB", A());
        var tcs = await InstrumentAsync("TCS", B());
        var split = await InstrumentAsync("SPLITCO", ReplayDays.Scaled(A(), 2.0, 1), tickPaise: null);
        await AddSplitAsync(split, D4);
        return (abb, tcs, split, await CreateBasketAsync(basketName, abb, tcs, split));
    }

    private static string PackFolder(TempFolder temp, string? root = null) =>
        Directory.EnumerateDirectories(Path.Combine(root ?? temp.Root, "verify")).Single(d => !Path.GetFileName(d).StartsWith('.') && !d.EndsWith(".previous", StringComparison.Ordinal));

    private static Dictionary<string, byte[]> FilesOfPack(string folder) =>
        Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).ToDictionary(f => Path.GetRelativePath(folder, f), File.ReadAllBytes, StringComparer.Ordinal);

    private async Task<SimulationRun> ModelledRunAsync(Guid basket) => await HandlerRunAsync(null, basket, Modelled);

    [Fact]
    public async Task The_pack_of_the_end_to_end_table_has_the_files_of_the_format_the_cards_of_the_trades_of_the_record_the_split_chart_halved_once_and_no_table_changes()
    {
        var (_, _, _, basket) = await SeedPackDaysAsync();
        var before = await MarketRowCountsAsync();
        using var temp = new TempFolder();

        var outcome = await PackAsync("--strategies ORB --basket Lab " + PackWindow + " --count 3", temp);

        outcome.Exit.ShouldBe(0, outcome.Error);
        (await MarketRowCountsAsync()).ShouldBe(before, "the pack writes nothing to the database");
        var run = await ModelledRunAsync(basket);
        var plan = PackPlanner.Plan([run], "t1", 3);
        var folder = PackFolder(temp);
        Path.GetFileName(folder).ShouldBe(PackFiles.PackId("t1", PackFiles.PackKey("t1", 3, basket, [run])));
        var files = FilesOfPack(folder);
        files.Keys.Where(k => !k.StartsWith("cards/", StringComparison.Ordinal)).Order(StringComparer.Ordinal).ShouldBe(["README.txt", "check-log.csv", "engine-values.csv", "index.html", "manifest.json", "pack.json"]);
        files.Keys.Where(k => k.StartsWith("cards/", StringComparison.Ordinal)).Order(StringComparer.Ordinal).ShouldBe([.. plan.Rows.Select(r => PackFiles.CardName(r.Seq, plan.Rows.Count, r.Trade.Trade)).Order(StringComparer.Ordinal)]);
        plan.Rows.Count.ShouldBeGreaterThanOrEqualTo(3);

        foreach (var row in plan.Rows)
        {
            var trade = row.Trade.Trade;
            var html = System.Text.Encoding.UTF8.GetString(files[PackFiles.CardName(row.Seq, plan.Rows.Count, trade)]);
            html.ShouldContain("run key: " + run.RunKey);
            html.ShouldContain("entry " + XDrishti.Domain.Research.CanonicalText.Number(trade.EntryPrice));
            html.ShouldContain("stop " + XDrishti.Domain.Research.CanonicalText.Number(trade.StopPrice));
            html.ShouldContain(", " + XDrishti.SimExport.ExportSpellings.Of(trade.ExitReason) + ", exact result R ");
        }

        var split = plan.Rows.Single(r => r.Trade.Trade.Symbol == "NSE:SPLITCO");
        split.Categories.ShouldContain("adjusted_instrument");
        var splitCard = System.Text.Encoding.UTF8.GetString(files[PackFiles.CardName(split.Seq, plan.Rows.Count, split.Trade.Trade)]);
        splitCard.ShouldContain("<td>as stored (raw)</td><td>198</td><td>198</td><td>198</td><td>198</td>", Case.Sensitive);
        splitCard.ShouldContain("<td>as on the chart (adjusted)</td><td>99</td><td>99</td><td>99</td><td>99</td>", Case.Sensitive);

        var checkedLog = await PackAsync("--check " + Path.Combine(folder, "check-log.csv"), temp);
        checkedLog.Exit.ShouldBe(1, checkedLog.Error);
        checkedLog.Out.Split('\n').Where(l => l.StartsWith("row ", StringComparison.Ordinal)).ShouldAllBe(l => l.EndsWith(": unsigned", StringComparison.Ordinal));
        checkedLog.Out.Split('\n').Count(l => l.StartsWith("row ", StringComparison.Ordinal)).ShouldBe(plan.Rows.Count + plan.Spots.Count);
        checkedLog.Out.ShouldContain("pack: incomplete (");
        checkedLog.Logs.ShouldContain(l => l.Contains("check read", StringComparison.Ordinal));

        outcome.Out.ShouldStartWith(XDrishti.SimExport.Text.RunBanner.Text(RunLabels.Of(run, FillRules.Default)));
        outcome.Out.ShouldContain("rows: " + (plan.Rows.Count + plan.Spots.Count) + " (trades and spot checks together)");
        outcome.Out.ShouldNotContain(" trades, ", Case.Sensitive, "no split that reveals how many edge picks were added");
        outcome.Out.ShouldContain("pack: " + Path.GetFileName(folder));
        outcome.Out.ShouldContain("next: open index.html");
        outcome.Logs.ShouldContain(l => l.Contains("wrote pack", StringComparison.Ordinal) && l.Contains(Path.GetFileName(folder), StringComparison.Ordinal));
        outcome.Logs.ShouldNotContain(l => l.Contains("NSE:", StringComparison.Ordinal) || l.Contains("100.5", StringComparison.Ordinal), "no symbol, bar or price in a log line");
    }

    [Fact]
    public async Task The_manifest_lists_every_file_with_its_hash_carries_the_pack_lines_and_the_export_key_of_the_library_and_the_cards_equal_the_cards_of_simulate_for_the_same_trades()
    {
        var (_, _, _, basket) = await SeedPackDaysAsync();
        using var packRoot = new TempFolder();
        using var simRoot = new TempFolder();

        var packed = await PackAsync("--strategies ORB --basket Lab " + PackWindow + " --count 3", packRoot);
        var simulated = await VerbAsync("--strategy ORB --basket Lab " + Window.Replace("--seed t1 ", string.Empty, StringComparison.Ordinal), simRoot);

        packed.Exit.ShouldBe(0, packed.Error);
        simulated.Exit.ShouldBe(0, simulated.Error);
        var run = await ModelledRunAsync(basket);
        var plan = PackPlanner.Plan([run], "t1", 3);
        var folder = PackFolder(packRoot);
        var manifest = ExportManifest.ParseBytes(File.ReadAllBytes(Path.Combine(folder, "manifest.json"))).Value;
        var packKey = PackFiles.PackKey("t1", 3, basket, [run]);
        manifest.Format.ShouldBe("xd-verify-pack-2");
        manifest.RunKey.ShouldBe(packKey);
        manifest.BasketId.ShouldBe(basket);
        manifest.Entries.Select(e => e.Name).Order(StringComparer.Ordinal).ShouldBe([.. FilesOfPack(folder).Keys.Where(k => k != "manifest.json").Order(StringComparer.Ordinal)]);
        manifest.ExtraLines.ShouldBe(PackFiles.ExtraLines("t1", 3, [run], plan));
        manifest.ExportKey.ShouldBe(ExportKey.Of("xd-verify-pack-2", packKey, basket, manifest.Entries, manifest.ExtraLines));
        foreach (var entry in manifest.Entries)
        {
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(folder, entry.Name)))).ShouldBe(entry.Sha256, entry.Name);
        }

        var simFolder = RunFolder(simRoot);
        foreach (var row in plan.Rows)
        {
            var number = run.Trades.ToList().FindIndex(t => t.Trade.Key == row.Trade.Trade.Key) + 1;
            var simCard = File.ReadAllBytes(Path.Combine(simFolder, CardNames.Of(number, run.Trades.Count, row.Trade.Trade)));
            File.ReadAllBytes(Path.Combine(folder, PackFiles.CardName(row.Seq, plan.Rows.Count, row.Trade.Trade))).ShouldBe(simCard, "the card is the card of simulate for the same trade");
        }
    }

    [Fact]
    public async Task Two_roots_with_the_same_seed_give_identical_bytes_a_second_run_is_unchanged_and_another_seed_is_another_pack()
    {
        await SeedPackDaysAsync();
        using var one = new TempFolder();
        using var two = new TempFolder();

        var first = await PackAsync("--strategies ORB --basket Lab " + PackWindow + " --count 3", one);
        var second = await PackAsync("--strategies ORB --basket Lab " + PackWindow + " --count 3", two);
        var again = await PackAsync("--strategies ORB --basket Lab " + PackWindow + " --count 3", one);
        var other = await PackAsync("--strategies ORB --basket Lab " + PackWindow.Replace("t1", "t2", StringComparison.Ordinal) + " --count 3", one);

        new[] { first.Exit, second.Exit, again.Exit, other.Exit }.ShouldAllBe(e => e == 0, first.Error + second.Error + again.Error + other.Error);
        var a = FilesOfPack(Directory.EnumerateDirectories(Path.Combine(one.Root, "verify")).Single(d => Path.GetFileName(d).StartsWith("t1-", StringComparison.Ordinal)));
        var b = FilesOfPack(PackFolder(two));
        a.Keys.Order(StringComparer.Ordinal).ShouldBe(b.Keys.Order(StringComparer.Ordinal));
        foreach (var (name, bytes) in a)
        {
            bytes.ShouldBe(b[name], name);
        }

        first.Out.ShouldContain("export: written");
        again.Out.ShouldContain("export: unchanged");
        again.Logs.ShouldContain(l => l.Contains("already there and is unchanged", StringComparison.Ordinal));
        Directory.EnumerateDirectories(Path.Combine(one.Root, "verify")).Count().ShouldBe(2, "another seed is another folder");
    }

    [Fact]
    public async Task A_hand_edited_log_or_card_is_never_unchanged_and_the_verb_refuses_without_force_naming_the_file()
    {
        await SeedPackDaysAsync();
        using var temp = new TempFolder();
        (await PackAsync("--strategies ORB --basket Lab " + PackWindow + " --count 3", temp)).Exit.ShouldBe(0);
        var folder = PackFolder(temp);
        var log = Path.Combine(folder, "check-log.csv");
        var original = File.ReadAllText(log);

        File.WriteAllText(log, original.Replace(",trade,", ",trade,card", StringComparison.Ordinal));
        var logEdited = await PackAsync("--strategies ORB --basket Lab " + PackWindow + " --count 3", temp);
        File.WriteAllText(log, original);
        var card = Directory.EnumerateFiles(Path.Combine(folder, "cards")).First();
        File.AppendAllText(card, "<!-- edited -->");
        var cardEdited = await PackAsync("--strategies ORB --basket Lab " + PackWindow + " --count 3", temp);

        logEdited.Exit.ShouldBe(1);
        logEdited.Error.ShouldContain("differs from the manifest: check-log.csv");
        logEdited.Out.ShouldBeEmpty();
        cardEdited.Exit.ShouldBe(1);
        cardEdited.Error.ShouldContain("differs from the manifest: cards/" + Path.GetFileName(card));
    }

    [Fact]
    public async Task Force_over_a_log_the_owner_started_warns_loudly_keeps_the_old_folder_as_previous_and_writes_a_blank_log_and_a_second_force_is_refused_while_previous_exists()
    {
        await SeedPackDaysAsync();
        using var temp = new TempFolder();
        (await PackAsync("--strategies ORB --basket Lab " + PackWindow + " --count 3", temp)).Exit.ShouldBe(0);
        var folder = PackFolder(temp);
        var blank = File.ReadAllBytes(Path.Combine(folder, "check-log.csv"));
        File.AppendAllText(Path.Combine(folder, "check-log.csv"), "my work\n");

        var forced = await PackAsync("--strategies ORB --basket Lab " + PackWindow + " --count 3 --force", temp);

        forced.Exit.ShouldBe(0, forced.Error);
        forced.Error.ShouldContain("WARNING: check-log.csv");
        forced.Error.ShouldContain("has been edited");
        forced.Out.ShouldContain("export: replaced (the old pack is kept in " + folder + ".previous)");
        File.ReadAllBytes(Path.Combine(folder, "check-log.csv")).ShouldBe(blank);
        File.ReadAllText(Path.Combine(folder + ".previous", "check-log.csv")).ShouldEndWith("my work\n");

        File.AppendAllText(Path.Combine(folder, "check-log.csv"), "more work\n");
        var second = await PackAsync("--strategies ORB --basket Lab " + PackWindow + " --count 3 --force", temp);

        second.Exit.ShouldBe(1);
        second.Error.ShouldContain("already exists");
        File.ReadAllText(Path.Combine(folder + ".previous", "check-log.csv")).ShouldEndWith("my work\n");
        File.ReadAllText(Path.Combine(folder, "check-log.csv")).ShouldEndWith("more work\n");
    }

    [Fact]
    public async Task Two_baskets_with_the_same_members_and_different_ids_give_different_pack_ids()
    {
        var (abb, tcs, split, _) = await SeedPackDaysAsync("Lab");
        await CreateBasketAsync("Lab2", abb, tcs, split);
        using var temp = new TempFolder();

        (await PackAsync("--strategies ORB --basket Lab " + PackWindow + " --count 3", temp)).Exit.ShouldBe(0);
        (await PackAsync("--strategies ORB --basket Lab2 " + PackWindow + " --count 3", temp)).Exit.ShouldBe(0);

        Directory.EnumerateDirectories(Path.Combine(temp.Root, "verify")).Count().ShouldBe(2);
    }

    [Fact]
    public async Task A_database_that_changes_between_the_runs_and_the_cards_is_exit_1_naming_the_instrument_with_nothing_written()
    {
        await SeedPackDaysAsync();
        using var temp = new TempFolder();

        var outcome = await PackAsync(
            "--strategies ORB --symbols ABB " + PackWindow,
            temp,
            services =>
            {
                var real = services.Last(s => s.ServiceType == typeof(IBarStore));
                services.AddScoped(real.ImplementationType!);
                services.AddScoped<IBarStore>(sp => new DriftingStore((IBarStore)sp.GetRequiredService(real.ImplementationType!)));
            });

        outcome.Exit.ShouldBe(1, outcome.Error);
        outcome.Error.ShouldContain("NSE:ABB: bars_changed: ");
        outcome.Out.ShouldBeEmpty();
        Directory.Exists(temp.Root).ShouldBeFalse("nothing was written, not even the root");
        outcome.Logs.ShouldContain(l => l.Contains("trade cards are stale for instrument", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Two_strategies_are_two_runs_in_one_pack_each_with_its_banner_and_its_run_key_and_the_cards_come_from_the_run_of_their_own_trade()
    {
        var (_, _, _, basket) = await SeedPackDaysAsync();
        using var temp = new TempFolder();

        var outcome = await PackAsync("--strategies ORB,VWAP_PULLBACK --basket Lab " + PackWindow + " --count 5", temp);

        outcome.Exit.ShouldBe(0, outcome.Error);
        var orb = await ModelledRunAsync(basket);
        using var json = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(PackFolder(temp), "pack.json")));
        var runs = json.RootElement.GetProperty("runs").EnumerateArray().ToList();
        runs.Select(r => r.GetProperty("strategy").GetString()).ShouldBe(["ORB", "VWAP_PULLBACK"]);
        runs[0].GetProperty("run_key").GetString().ShouldBe(orb.RunKey);
        runs[1].GetProperty("run_key").GetString().ShouldNotBe(orb.RunKey);
        json.RootElement.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("run_key").GetString()).ShouldAllBe(k => k == orb.RunKey || k == runs[1].GetProperty("run_key").GetString());
        outcome.Out.Split('\n').Count(l => l.StartsWith("exploration: ", StringComparison.Ordinal)).ShouldBe(2, "the banner of every run");
    }

    [Fact]
    public async Task A_pool_of_no_trades_is_a_pack_with_no_rows_a_warning_and_exit_0()
    {
        await InstrumentAsync("FLAT", ReplayDays.Join(ReplayDays.Flat(D1, 99.00), ReplayDays.Flat(D2, 99.00), ReplayDays.Flat(D3, 99.00)));
        using var temp = new TempFolder();

        var outcome = await PackAsync("--strategies ORB --symbols FLAT " + PackWindow, temp);

        outcome.Exit.ShouldBe(0, outcome.Error);
        outcome.Error.ShouldContain("warning: the pooled runs hold no trade, so the pack has no rows");
        outcome.Out.ShouldContain("rows: 0 (trades and spot checks together)");
        var folder = PackFolder(temp);
        File.Exists(Path.Combine(folder, "check-log.csv")).ShouldBeTrue();
        Directory.Exists(Path.Combine(folder, "cards")).ShouldBeFalse();
    }

    [Fact]
    public async Task A_costed_pack_with_a_stored_sheet_is_another_pack_with_the_net_r_and_the_charge_total_and_says_what_the_run_says_about_costs()
    {
        await SeedPackDaysAsync();
        using (var scope = Factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ICostSheetStore>().SaveAsync(FixtureSheet.Sheet, "cli:cost-sheet", DateTimeOffset.UnixEpoch, Ct);
            (await scope.ServiceProvider.GetRequiredService<IPlatformUnitOfWork>().TrySaveAsync(Ct)).ShouldBeTrue();
        }

        using var temp = new TempFolder();
        var gross = await PackAsync("--strategies ORB --basket Lab " + PackWindow + " --count 3", temp);
        var costed = await PackAsync("--strategies ORB --basket Lab " + PackWindow.Replace("--gross", "--costs " + FixtureSheet.Id, StringComparison.Ordinal) + " --count 3", temp);

        gross.Exit.ShouldBe(0, gross.Error);
        costed.Exit.ShouldBe(0, costed.Error);
        gross.Out.ShouldContain("costs: none");
        costed.Out.ShouldNotContain("costs: none");
        var folders = Directory.EnumerateDirectories(Path.Combine(temp.Root, "verify")).ToList();
        folders.Count.ShouldBe(2, "a different run key is a different pack key");
        var costedFolder = folders.Single(f => File.ReadAllText(Path.Combine(f, "README.txt")).Contains(FixtureSheet.Id, StringComparison.Ordinal));
        var engine = File.ReadAllLines(Path.Combine(costedFolder, "engine-values.csv"));
        var header = engine[0].Split(',');
        var net = Array.IndexOf(header, "r_net");
        var total = Array.IndexOf(header, "cost_total");
        engine.Skip(1).Where(l => !l.Contains(",spot,", StringComparison.Ordinal)).ShouldAllBe(l => l.Split(',')[net].Length > 0 && l.Split(',')[total].Length > 0);
    }

    [Fact]
    public async Task A_refusal_of_the_universe_or_the_request_is_exit_1_with_nothing_written_and_no_bar_read()
    {
        await SeedPackDaysAsync();
        using var temp = new TempFolder();
        var reads = new NoReads();

        var unknownSymbol = await PackAsync("--strategies ORB --symbols NOPE " + PackWindow, temp, services =>
        {
            services.AddSingleton<IBarStore>(reads);
            services.AddSingleton<IBarQueries>(reads);
        });
        var badWindow = await PackAsync("--strategies ORB --symbols ABB --from 2026-03-04 --to 2026-03-02 --seed t1 --gross", temp);

        unknownSymbol.Exit.ShouldBe(1);
        unknownSymbol.Error.ShouldContain("symbol 'NOPE': not_found");
        badWindow.Exit.ShouldBe(1);
        badWindow.Error.ShouldContain("from: ");
        reads.Calls.ShouldBeEmpty();
        Directory.Exists(temp.Root).ShouldBeFalse();
    }

    [Fact]
    public async Task The_usage_error_of_a_missing_seed_is_exit_2_and_prints_the_usage_text()
    {
        using var temp = new TempFolder();

        var outcome = await PackAsync("--strategies ORB --symbols ABB --from 2026-03-02 --to 2026-03-04 --gross", temp);

        outcome.Exit.ShouldBe(2);
        outcome.Error.ShouldContain("'--seed' is required");
        outcome.Error.ShouldContain("verify-pack --strategies");
        Directory.Exists(temp.Root).ShouldBeFalse();
    }
}
