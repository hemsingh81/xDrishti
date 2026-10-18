using System.Text.Json;
using System.Text.RegularExpressions;
using XDrishti.Api.Simulate;
using XDrishti.Application.Market.Tracking;
using XDrishti.Application.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Research;
using XDrishti.SimExport.Manifest;
using XDrishti.SimExport.Text;
using XDrishti.Tests.Shared;

namespace XDrishti.Api.IntegrationTests.Simulate;

/// <summary>
/// The verb with fake handlers (no database): the order of the pipeline, the exit codes, the tick gate before the run, the banner and the summary, the files and the overwrite policy through the verb, the log lines and the scans for secrets and for
/// words of a statistic. The handlers answer scripted values, so what is tested is the verb's own work (P3-04b2 criteria 4, 8, 9, 10 and 11); the database half is in <see cref="SimulateDatabaseTests"/>.
/// </summary>
public sealed partial class SimulateCommandTests : IDisposable
{
    private const string Common = "--strategy ORB --symbols AAA --from 2024-04-01 --to 2026-04-30";

    private readonly FakeVerb _verb = new();

    public void Dispose() => _verb.Dispose();

    [GeneratedRegex(@"\d\s?%", RegexOptions.CultureInvariant)]
    private static partial Regex Percent();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string Folders() => Directory.Exists(_verb.ExportRoot) ? string.Join("|", Directory.EnumerateFileSystemEntries(_verb.ExportRoot, "*", SearchOption.AllDirectories).Select(p => Path.GetRelativePath(_verb.ExportRoot, p))) : "(no root)";

    private string RunFolder() => Directory.EnumerateDirectories(Path.Combine(_verb.ExportRoot, "sim")).Single(d => !Path.GetFileName(d).StartsWith('.'));

    private string Scrub(string text) => text.Replace(_verb.Root.Path, "<root>", StringComparison.Ordinal);

    // the command line and the configuration: exit 2

    [Fact]
    public async Task List_strategies_prints_the_registry_and_exits_0_without_touching_a_handler_or_the_export_root()
    {
        var exit = await _verb.RunAsync("--list-strategies", Ct);

        exit.ShouldBe(0);
        var text = _verb.Out.ToString();
        text.ShouldContain("ORB:");
        text.ShouldContain("timeframes: 5m, 15m (default)");
        text.ShouldContain("or_min=<number>  default 15");
        text.ShouldContain("rr=<number>  default 2");
        _verb.RunHandler.Received.ShouldBeEmpty();
        _verb.InstrumentHandler.Received.ShouldBeEmpty();
        Directory.Exists(_verb.ExportRoot).ShouldBeFalse();
    }

    [Theory]
    [InlineData("--strategy ORB --symbols AAA --from 2024-04-01")]
    [InlineData("--cards --strategy ORB --symbols AAA --from 2024-04-01 --to 2024-04-30")]
    [InlineData("")]
    [InlineData("--strategy orb --symbols AAA --from 2024-04-01 --to 2024-04-30")]
    public async Task A_wrong_command_line_is_exit_2_with_the_usage_text_and_no_stack_and_nothing_is_run(string args)
    {
        var exit = await _verb.RunAsync(args, Ct);

        exit.ShouldBe(2);
        _verb.Out.ToString().ShouldBeEmpty();
        _verb.Error.ToString().ShouldContain("usage:");
        _verb.Error.ToString().ShouldNotContain("   at ");
        _verb.RunHandler.Received.ShouldBeEmpty();
        Directory.Exists(_verb.ExportRoot).ShouldBeFalse();
    }

    [Fact]
    public async Task An_export_root_that_is_unset_relative_or_without_a_parent_is_exit_2_before_anything_is_resolved()
    {
        foreach (var root in new string?[] { null, string.Empty, "exports/relative", Path.Combine(_verb.Root.Path, "missing-parent", "exports") })
        {
            var exit = await _verb.RunInRootAsync(Common, root, Ct);

            exit.ShouldBe(2, "root: " + root);
        }

        _verb.Error.ToString().ShouldContain("The export directory is not usable");
        _verb.Error.ToString().ShouldContain("usage:");
        _verb.InstrumentHandler.Received.ShouldBeEmpty();
        _verb.RunHandler.Received.ShouldBeEmpty();
    }

    // the universe: exit 1

    [Fact]
    public async Task Every_unmatched_symbol_is_listed_with_its_reason_and_the_run_is_refused_before_anything_is_read()
    {
        _verb.Resolve = _ => Result.Success(new ResolveResult([], [new UnmatchedEntry("NOPE", "not_found"), new UnmatchedEntry("INE000000000", "ambiguous"), new UnmatchedEntry("bad$", "invalid")], 3, 0));

        var exit = await _verb.RunAsync("--strategy ORB --symbols NOPE,INE000000000 --from 2024-04-01 --to 2026-04-30", Ct);

        exit.ShouldBe(1);
        var lines = _verb.Error.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.ShouldBe(["symbol 'NOPE': not_found", "symbol 'INE000000000': ambiguous", "symbol 'bad$': invalid"]);
        _verb.RunHandler.Received.ShouldBeEmpty();
        Folders().ShouldBe("(no root)");
    }

    [Fact]
    public async Task A_basket_name_that_matches_nothing_lists_at_most_twenty_manual_basket_names()
    {
        for (var i = 0; i < 25; i++)
        {
            _verb.AddBasket("Basket" + i.ToString("00", System.Globalization.CultureInfo.InvariantCulture));
        }

        _verb.AddBasket("Account bucket", "portfolio");

        var exit = await _verb.RunAsync("--strategy ORB --basket Nothing --from 2024-04-01 --to 2026-04-30", Ct);

        exit.ShouldBe(1);
        var text = _verb.Error.ToString();
        text.ShouldContain("basket 'Nothing' matches no active manual basket");
        text.ShouldContain("Basket00");
        text.ShouldContain("Basket19");
        text.ShouldNotContain("Basket20");
        text.ShouldNotContain("Account bucket");
        text.ShouldContain("and 5 more");
        _verb.RunHandler.Received.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_basket_name_is_matched_ignoring_case_and_its_id_is_handed_to_the_handler()
    {
        var basket = _verb.AddBasket("Banks", "manual", ("AAA", "share", 5m, false));

        var exit = await _verb.RunAsync("--strategy ORB --basket bAnKs --from 2024-04-01 --to 2026-04-30", Ct);

        exit.ShouldBe(0);
        var request = _verb.RunHandler.Received.Single().Request;
        (request.BasketId, request.InstrumentIds).ShouldBe((basket.Id, null));
    }

    [Fact]
    public async Task A_basket_given_by_id_that_is_not_a_manual_one_is_left_to_the_handler_to_refuse_as_not_found()
    {
        var portfolio = _verb.AddBasket("Account bucket", "portfolio", ("AAA", "share", 0.05m, false));
        _verb.Run = _ => Result.Failure<SimulationRun>(SimulationErrors.BasketNotFound);

        var exit = await _verb.RunAsync("--strategy ORB --basket " + portfolio.Id + " --from 2024-04-01 --to 2026-04-30", Ct);

        exit.ShouldBe(1);
        _verb.Error.ToString().ShouldContain("simulation.basket_not_found: ");
        _verb.RunHandler.Received.Single().Request.BasketId.ShouldBe(portfolio.Id);
        Folders().ShouldBe("(no root)");
    }

    // the tick gate: before the run, with the figures

    private (Guid Suspect, FakeVerb Verb) Universe(bool withSuspect)
    {
        _verb.AddInstrument("AAA", "share", 5m);
        _verb.AddInstrument("ETFX", "etf", 1m);
        _verb.AddInstrument("BBB", "share", 10m);
        _verb.AddInstrument("IDX", "index", 0.05m);
        _verb.AddInstrument("FUT", "future", 0.25m);
        _verb.AddInstrument("WEIRD", "swap", 0.05m);
        _verb.AddInstrument("NOTICK", "share", null);
        _verb.AddInstrument("BIGTICK", "share", 150m);
        var suspect = withSuspect ? _verb.AddInstrument("SUSP", "share", 0.05m) : Guid.Empty;
        return (suspect, _verb);
    }

    [Fact]
    public async Task A_share_whose_stored_tick_reads_as_less_than_a_paisa_refuses_the_run_before_the_run_handler_is_asked_and_names_the_figures()
    {
        Universe(withSuspect: true);

        var exit = await _verb.RunAsync("--strategy ORB --symbols AAA,SUSP --from 2024-04-01 --to 2026-04-30", Ct);

        exit.ShouldBe(1);
        _verb.RunHandler.Received.ShouldBeEmpty("no bar can have been read: the handler is where the bars are read");
        var text = _verb.Error.ToString();
        text.ShouldContain("SUSP");
        text.ShouldContain("stored 0.05");
        text.ShouldContain("converted 0.0005 rupee");
        text.ShouldContain("ADR 0038 item 6");
        text.ShouldNotContain("IDX");
        text.ShouldNotContain("FUT");
        Folders().ShouldBe("(no root)");
    }

    [Fact]
    public async Task The_gate_names_at_most_three_instruments()
    {
        for (var i = 0; i < 5; i++)
        {
            _verb.AddInstrument("SUS" + i, "share", 0.05m);
        }

        await _verb.RunAsync(Common, Ct);

        _verb.Error.ToString().Split('\n').Count(l => l.StartsWith("  SUS", StringComparison.Ordinal)).ShouldBe(3);
        _verb.Error.ToString().ShouldContain("5 share or ETF rows");
    }

    [Fact]
    public async Task Shares_and_an_etf_with_ticks_of_one_five_and_ten_paise_run_and_the_index_the_future_the_unknown_type_the_missing_tick_and_the_big_tick_are_not_suspects()
    {
        Universe(withSuspect: false);

        var exit = await _verb.RunAsync(Common, Ct);

        exit.ShouldBe(0, _verb.Error.ToString());
        _verb.RunHandler.Received.Count.ShouldBe(1);
        var summary = _verb.Out.ToString();
        summary.ShouldContain("not tick-checked (not a share or an ETF): future 1, index 1, unknown type 1");
        summary.ShouldContain("ticks used (rupees, converted from the master's paise):");
        summary.ShouldContain("  0.05: 1 instruments, for example NSE:AAA");
        summary.ShouldContain("  0.1: 1 instruments, for example NSE:M&M");
    }

    [Fact]
    public async Task The_gate_reads_the_members_of_a_basket_and_refuses_for_a_suspect_member()
    {
        _verb.AddBasket("Banks", "manual", ("AAA", "share", 5m, false), ("SUSP", "etf", 0.05m, false), ("IDX", "index", 0.05m, false));

        var exit = await _verb.RunAsync("--strategy ORB --basket Banks --from 2024-04-01 --to 2026-04-30", Ct);

        exit.ShouldBe(1);
        _verb.RunHandler.Received.ShouldBeEmpty();
        _verb.Error.ToString().ShouldContain("SUSP: stored 0.05, converted 0.0005 rupee");
        _verb.Error.ToString().ShouldNotContain("IDX");
    }

    [Fact]
    public async Task A_retired_member_runs_and_is_marked_and_duplicates_are_counted_in_the_summary()
    {
        _verb.AddInstrument("AAA", "share", 5m, retired: true);
        var resolved = _verb.Resolve(new ResolveInstrumentsQuery(new ResolveInput(["AAA"]))).Value;
        _verb.Resolve = _ => Result.Success(resolved with { Duplicates = 2 });

        var exit = await _verb.RunAsync(Common, Ct);

        exit.ShouldBe(0);
        _verb.Out.ToString().ShouldContain("retired instruments run: AAA");
        _verb.Out.ToString().ShouldContain("duplicates collapsed: 2");
    }

    // the request the handler gets

    [Fact]
    public async Task The_request_has_the_ids_in_order_the_default_timeframe_of_the_strategy_the_parameters_and_the_modelled_fills()
    {
        var first = _verb.AddInstrument("AAA");
        var second = _verb.AddInstrument("BBB");

        var exit = await _verb.RunAsync("--strategy ORB --symbols AAA,BBB --from 2024-04-01 --to 2026-04-30 --param rr=3 --param or_min=30 --acknowledge-csv-adjusted", Ct);

        exit.ShouldBe(0);
        var request = _verb.RunHandler.Received.Single().Request;
        request.StrategyId.ShouldBe("ORB");
        request.InstrumentIds.ShouldBe([first, second]);
        request.BasketId.ShouldBeNull();
        request.EvaluationTimeframe.ShouldBe("15m");
        (request.From, request.To).ShouldBe((new DateOnly(2024, 4, 1), new DateOnly(2026, 4, 30)));
        request.Parameters.ShouldNotBeNull().OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => (p.Key, p.Value)).ShouldBe([("or_min", 30.0), ("rr", 3.0)]);
        request.AcknowledgeCsvAdjusted.ShouldBeTrue();
        request.Fills.ShouldBe(new FillRulesInput(0.1, 1, 0.05, 2));
        request.Fills.ShouldBe(new FillRulesInput(FillRules.Default.EntryLimitPercent, FillRules.Default.Slippage.Ticks, FillRules.Default.Slippage.BarRangeFrac, FillRules.Default.Slippage.StopExitExtraTicks));
    }

    [Fact]
    public async Task An_explicit_timeframe_is_passed_and_no_slippage_turns_the_slippage_and_the_tick_rounding_off_in_the_request_and_the_label()
    {
        _verb.AddInstrument("AAA");
        _verb.Run = q => Result.Success(FabricatedRun.Assemble(FabricatedRun.List((FabricatedRun.IdA, "NSE:AAA")), [FabricatedRun.Ran(FabricatedRun.IdA, "NSE:AAA", FabricatedRun.Monday, FabricatedRun.Monday.AddDays(4))], fills: FillRules.Create(0.1, SlippageConfig.None).Value.ToCanonicalJson()));

        var exit = await _verb.RunAsync(Common + " --timeframe 5m --fills no-slippage", Ct);

        exit.ShouldBe(0, _verb.Error.ToString());
        var request = _verb.RunHandler.Received.Single().Request;
        request.EvaluationTimeframe.ShouldBe("5m");
        request.Fills.ShouldBe(new FillRulesInput(0.1, 0, 0, 0));
        _verb.Out.ToString().ShouldContain("fills: modelled, no slippage, no tick rounding of fills");
    }

    // a refusal of the handler

    [Fact]
    public async Task A_refusal_of_the_handler_prints_one_path_code_message_line_per_problem_at_most_twenty_and_writes_nothing()
    {
        _verb.AddInstrument("AAA");
        _verb.Run = _ => Result.Failure<SimulationRun>(SimulationErrors.Invalid([.. Enumerable.Range(0, 25).Select(i => new FieldError("parameters.p" + i, "bad value " + i, Code: "simulation.unknown_parameter"))]));

        var exit = await _verb.RunAsync(Common, Ct);

        exit.ShouldBe(1);
        var lines = _verb.Error.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Length.ShouldBe(21);
        lines[20].ShouldBe("and 5 more problems");
        lines[0].ShouldBe("parameters.p0: simulation.unknown_parameter: bad value 0");
        lines[19].ShouldBe("parameters.p19: simulation.unknown_parameter: bad value 19");
        _verb.Out.ToString().ShouldBeEmpty();
        Folders().ShouldBe("(no root)");
    }

    [Fact]
    public async Task A_refusal_without_fields_is_one_code_and_message_line()
    {
        _verb.AddInstrument("AAA");
        _verb.Run = _ => Result.Failure<SimulationRun>(SimulationErrors.BasketNotFound);

        var exit = await _verb.RunAsync(Common, Ct);

        exit.ShouldBe(1);
        _verb.Error.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).ShouldHaveSingleItem().ShouldStartWith("simulation.basket_not_found: ");
    }

    // the files and the console

    [Fact]
    public async Task A_run_writes_the_five_files_prints_the_banner_first_and_the_banner_is_the_labels_of_run_json_and_the_readme()
    {
        _verb.AddInstrument("AAA");

        var exit = await _verb.RunAsync(Common, Ct);

        exit.ShouldBe(0, _verb.Error.ToString());
        var run = FabricatedRun.Standard();
        var folder = RunFolder();
        Path.GetFileName(folder).ShouldBe(RunId.Of("ORB", run.From, run.To, run.RunKey));
        Directory.EnumerateFiles(folder).Select(Path.GetFileName).Order(StringComparer.Ordinal).ShouldBe(["README.txt", "manifest.json", "open_at_end.csv", "run.json", "trades.csv"]);
        if (!OperatingSystem.IsWindows())
        {
            foreach (var file in Directory.EnumerateFiles(folder))
            {
                File.GetUnixFileMode(file).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            File.GetUnixFileMode(folder).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        var banner = RunBanner.Text(RunLabels.Of(run, FillRules.Default));
        _verb.Out.ToString().ShouldStartWith(banner);
        File.ReadAllText(Path.Combine(folder, "README.txt")).ShouldStartWith(banner);
        using var json = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder, "run.json")));
        var lines = json.RootElement.GetProperty("labels").GetProperty("lines").EnumerateArray().Select(e => e.GetString()).ToList();
        _verb.Out.ToString().Split('\n').Take(lines.Count).ShouldBe(lines!);
        File.ReadAllText(Path.Combine(folder, "trades.csv")).ShouldContain(run.RunKey);
    }

    [Fact]
    public async Task The_summary_has_the_lines_of_the_spec_and_no_split_of_the_trades_by_side_or_exit_reason_no_r_value_and_no_percentage()
    {
        _verb.AddInstrument("AAA");

        await _verb.RunAsync(Common, Ct);

        var run = FabricatedRun.Standard();
        var summary = Scrub(_verb.Out.ToString());
        summary.ShouldContain("run key: " + run.RunKey);
        summary.ShouldContain("folder: <root>");
        summary.ShouldContain("export: written");
        summary.ShouldContain("database: db.example.invalid:5433/xdrishti");
        summary.ShouldContain("instruments: 2 run, 1 skipped");
        summary.ShouldContain("skipped by reason: no_bars 1");
        summary.ShouldContain("  skipped: NSE:CCC (no_bars)");
        summary.ShouldContain("candidates: ");
        summary.ShouldContain("trades: 2");
        summary.ShouldContain("open at end (the data cut them short): 1");
        summary.ShouldContain("entries a flagged day suppressed: ");
        summary.ShouldContain("phases: resolve and gate ");
        summary.ShouldContain("instruments per second in the run");
        var afterBanner = summary[RunBanner.Text(RunLabels.Of(run, FillRules.Default)).Length..];
        afterBanner.ShouldNotContain("Buy");
        afterBanner.ShouldNotContain("Sell");
        afterBanner.ShouldNotContain("stop", Case.Insensitive);
        afterBanner.ShouldNotContain("target", Case.Insensitive);
        afterBanner.ShouldNotContain("exit", Case.Insensitive);
        summary.ShouldNotContain("xd_app", Case.Insensitive);
        _verb.Out.ToString().ShouldNotContain(_verb.Password);
        Percent().IsMatch(afterBanner).ShouldBeFalse();
    }

    [Fact]
    public async Task The_console_text_of_a_success_a_refusal_and_a_failure_has_none_of_the_words_of_a_statistic()
    {
        _verb.AddInstrument("AAA");
        var texts = new List<string>();

        await _verb.RunAsync(Common, Ct);
        texts.Add(Scrub(_verb.Out.ToString()));
        texts.Add(Scrub(_verb.Error.ToString()));
        await _verb.RunAsync(Common + " --fills fast", Ct);
        _verb.Run = _ => Result.Failure<SimulationRun>(SimulationErrors.StrategyInvalid([new FieldError("strategy_id", "The strategy `X` is not a built-in one.", Code: "simulation.unknown_strategy")]));
        await _verb.RunAsync(Common, Ct);
        _verb.Run = _ => Result.Success(FabricatedRun.Standard());
        _verb.RunThrows = () => new InvalidOperationException("the database is unreachable");
        await _verb.RunAsync(Common + " --force", Ct);
        await _verb.RunAsync("--list-strategies", Ct);
        texts.Add(Scrub(_verb.Out.ToString()));
        texts.Add(Scrub(_verb.Error.ToString()));

        foreach (var text in texts)
        {
            ForbiddenWords.In(text).ShouldBeEmpty(text);
        }

        _verb.Error.ToString().ShouldContain("failed: InvalidOperationException: the database is unreachable");
    }

    [Fact]
    public void The_scan_can_fail_a_symbol_with_a_forbidden_word_is_found_and_a_symbol_that_only_contains_one_is_not()
    {
        ForbiddenWords.In("instruments: NSE:WINNER (no_bars)").ShouldBe(["winner"]);
        ForbiddenWords.In("instruments: NSE:TATACONSUM (no_bars) window summary consumed").ShouldBeEmpty();
    }

    [Fact]
    public async Task A_hostile_symbol_in_the_console_text_trips_the_scan_so_the_scan_is_live()
    {
        var run = FabricatedRun.Assemble(FabricatedRun.List((FabricatedRun.IdA, "NSE:WINNER")), [FabricatedRun.Skipped(FabricatedRun.IdA, "NSE:WINNER", SkipReason.NoBars)]);
        _verb.AddInstrument("WINNER");
        _verb.Run = _ => Result.Success(run);

        await _verb.RunAsync(Common, Ct);

        ForbiddenWords.In(Scrub(_verb.Out.ToString())).ShouldBe(["winner"]);
    }

    [Fact]
    public async Task A_symbol_that_contains_a_word_such_as_consum_does_not_trip_the_scan()
    {
        var run = FabricatedRun.Assemble(FabricatedRun.List((FabricatedRun.IdA, "NSE:TATACONSUM")), [FabricatedRun.Skipped(FabricatedRun.IdA, "NSE:TATACONSUM", SkipReason.NoBars)]);
        _verb.AddInstrument("TATACONSUM");
        _verb.Run = _ => Result.Success(run);

        await _verb.RunAsync(Common, Ct);

        ForbiddenWords.In(Scrub(_verb.Out.ToString())).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_run_in_which_every_instrument_is_skipped_succeeds_writes_the_files_and_prints_a_warning()
    {
        _verb.AddInstrument("AAA");
        _verb.Run = _ => Result.Success(FabricatedRun.Assemble(FabricatedRun.List((FabricatedRun.IdA, "NSE:AAA"), (FabricatedRun.IdB, "NSE:BBB")), [FabricatedRun.Skipped(FabricatedRun.IdA, "NSE:AAA", SkipReason.NoBars), FabricatedRun.Skipped(FabricatedRun.IdB, "NSE:BBB", SkipReason.TickSizeInvalid)]));

        var exit = await _verb.RunAsync(Common, Ct);

        exit.ShouldBe(0);
        _verb.Error.ToString().ShouldContain("warning: every instrument was skipped");
        _verb.Out.ToString().ShouldContain("skipped by reason: no_bars 1, tick_size_invalid 1");
        File.ReadAllText(Path.Combine(RunFolder(), "trades.csv")).Split('\n', StringSplitOptions.RemoveEmptyEntries).Length.ShouldBe(1, "a header only");
        Directory.EnumerateFiles(RunFolder()).Count().ShouldBe(5);
    }

    [Fact]
    public async Task A_run_with_no_instruments_succeeds_with_header_only_files()
    {
        _verb.Run = _ => Result.Success(FabricatedRun.Assemble(FabricatedRun.BasketOf(), []));
        var basket = _verb.AddBasket("Empty");

        var exit = await _verb.RunAsync("--strategy ORB --basket Empty --from 2024-04-01 --to 2026-04-30", Ct);

        exit.ShouldBe(0, _verb.Error.ToString());
        _verb.RunHandler.Received.Single().Request.BasketId.ShouldBe(basket.Id);
        _verb.Error.ToString().ShouldNotContain("warning");
        File.ReadAllText(Path.Combine(RunFolder(), "open_at_end.csv")).Split('\n', StringSplitOptions.RemoveEmptyEntries).Length.ShouldBe(1);
    }

    // the overwrite policy through the verb

    [Fact]
    public async Task The_same_run_again_is_unchanged_and_exit_0_a_modified_folder_is_refused_and_force_keeps_one_previous_copy_and_never_a_second()
    {
        _verb.AddInstrument("AAA");
        (await _verb.RunAsync(Common, Ct)).ShouldBe(0);
        var folder = RunFolder();
        var before = File.ReadAllBytes(Path.Combine(folder, "trades.csv"));

        var again = await _verb.RunAsync(Common, Ct);

        again.ShouldBe(0);
        _verb.Out.ToString().ShouldContain("export: unchanged");
        File.ReadAllBytes(Path.Combine(folder, "trades.csv")).ShouldBe(before);

        File.AppendAllText(Path.Combine(folder, "trades.csv"), "edited\n");
        var refused = await _verb.RunAsync(Common, Ct);
        refused.ShouldBe(1);
        _verb.Error.ToString().ShouldContain("different or modified export");
        File.ReadAllText(Path.Combine(folder, "trades.csv")).ShouldEndWith("edited\n");

        var forced = await _verb.RunAsync(Common + " --force", Ct);
        forced.ShouldBe(0);
        _verb.Out.ToString().ShouldContain("export: replaced (the old export is kept in ");
        File.ReadAllBytes(Path.Combine(folder, "trades.csv")).ShouldBe(before);
        File.ReadAllText(Path.Combine(folder + ".previous", "trades.csv")).ShouldEndWith("edited\n");

        File.AppendAllText(Path.Combine(folder, "trades.csv"), "edited again\n");
        (await _verb.RunAsync(Common + " --force", Ct)).ShouldBe(1);
        _verb.Error.ToString().ShouldContain("remove it by hand before forcing another replacement");
        File.ReadAllText(Path.Combine(folder + ".previous", "trades.csv")).ShouldEndWith("edited\n");
    }

    [Fact]
    public async Task Two_baskets_with_the_same_members_have_the_same_run_key_and_different_export_keys_so_the_second_is_refused_without_force()
    {
        var members = new[] { (FabricatedRun.IdA, "NSE:AAA") };
        SimulationRun Make(Guid basket) => FabricatedRun.Assemble(
            new UniverseRecord(UniverseKind.Basket, basket, "Banks", UniverseLabels.MembersAsOfToday, [.. members.Select(m => new UniverseMember(m.Item1, m.Item2))]),
            [FabricatedRun.Ran(FabricatedRun.IdA, "NSE:AAA", FabricatedRun.Monday, FabricatedRun.Monday.AddDays(4))],
            [FabricatedRun.Trade(FabricatedRun.IdA, "NSE:AAA", Side.Buy, FabricatedRun.Monday)]);
        var one = Make(new Guid("00000000-0000-0000-0000-0000000000b1"));
        var two = Make(new Guid("00000000-0000-0000-0000-0000000000b2"));
        one.RunKey.ShouldBe(two.RunKey);
        _verb.AddBasket("Banks", "manual", ("AAA", "share", 5m, false));

        _verb.Run = _ => Result.Success(one);
        (await _verb.RunAsync("--strategy ORB --basket Banks --from 2024-04-01 --to 2026-04-30", Ct)).ShouldBe(0);
        _verb.Run = _ => Result.Success(two);
        var second = await _verb.RunAsync("--strategy ORB --basket Banks --from 2024-04-01 --to 2026-04-30", Ct);

        second.ShouldBe(1);
        _verb.Error.ToString().ShouldContain("different or modified export");
        File.ReadAllText(Path.Combine(RunFolder(), "trades.csv")).ShouldContain("00000000-0000-0000-0000-0000000000b1");
    }

    [Fact]
    public async Task A_stale_staging_folder_is_reported_in_the_summary_and_left_alone()
    {
        _verb.AddInstrument("AAA");
        var stale = Path.Combine(_verb.ExportRoot, "sim", ".staging-orb-old-0000aaaa");
        Directory.CreateDirectory(stale);

        (await _verb.RunAsync(Common, Ct)).ShouldBe(0);

        _verb.Out.ToString().ShouldContain("leftover staging folders of a killed run (remove by hand): .staging-orb-old-0000aaaa");
        Directory.Exists(stale).ShouldBeTrue();
    }

    [Fact]
    public async Task A_root_with_spaces_in_its_name_works()
    {
        _verb.AddInstrument("AAA");
        var spaced = Path.Combine(_verb.Root.Path, "my exports");

        var exit = await _verb.RunInRootAsync(Common, spaced, Ct);

        exit.ShouldBe(0, _verb.Error.ToString());
        Directory.Exists(Path.Combine(spaced, "sim")).ShouldBeTrue();
    }

    // failure and cancellation

    [Fact]
    public async Task An_unexpected_failure_is_exit_3_with_the_type_and_a_cut_message_and_no_stack_and_nothing_left_behind()
    {
        _verb.AddInstrument("AAA");
        _verb.RunThrows = () => new InvalidOperationException("the database said no " + new string('x', 500));

        var exit = await _verb.RunAsync(Common, Ct);

        exit.ShouldBe(3);
        var line = _verb.Error.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).ShouldHaveSingleItem();
        line.ShouldStartWith("failed: InvalidOperationException: the database said no x");
        line.Length.ShouldBeLessThan(340);
        line.ShouldEndWith("...");
        _verb.Error.ToString().ShouldNotContain("   at ");
        Folders().ShouldBe("(no root)");
        _verb.Logs.Lines.Single(l => l.EventId == 4409).Text.ShouldNotContain("   at ", Case.Sensitive);
        _verb.Out.ToString().ShouldNotContain("   at ");
    }

    [Fact]
    public async Task A_token_cancelled_mid_run_is_exit_130_with_no_staging_folder_and_no_stack()
    {
        _verb.AddInstrument("AAA");
        using var cancel = new CancellationTokenSource();
        _verb.RunThrows = () =>
        {
            cancel.Cancel();
            return new OperationCanceledException(cancel.Token);
        };

        var exit = await _verb.RunAsync(Common, cancel.Token);

        exit.ShouldBe(130);
        _verb.Error.ToString().ShouldContain("cancelled");
        _verb.Error.ToString().ShouldNotContain("   at ");
        Folders().ShouldNotContain(".staging-");
        _verb.Out.ToString().ShouldBeEmpty();
    }

    [Fact]
    public async Task A_token_cancelled_before_the_first_instrument_is_exit_130()
    {
        _verb.AddInstrument("AAA");
        using var cancel = new CancellationTokenSource();
        await cancel.CancelAsync();

        var exit = await _verb.RunAsync(Common, cancel.Token);

        exit.ShouldBe(130);
        _verb.RunHandler.Received.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_token_cancelled_after_the_final_rename_is_exit_130_and_the_complete_export_is_there_and_the_message_says_so()
    {
        _verb.AddInstrument("AAA");
        using var cancel = new CancellationTokenSource();
        _verb.Writer = new ExportFolderWriter((from, to) =>
        {
            Directory.Move(from, to);
            cancel.Cancel();
        });

        var exit = await _verb.RunAsync(Common, cancel.Token);

        exit.ShouldBe(130);
        _verb.Error.ToString().ShouldContain("cancelled after the export was written; it is complete in ");
        var folder = RunFolder();
        Directory.EnumerateFiles(folder).Count().ShouldBe(5);
        ExportManifest.ParseBytes(File.ReadAllBytes(Path.Combine(folder, "manifest.json"))).IsSuccess.ShouldBeTrue();
        Folders().ShouldNotContain(".staging-");
    }

    [Fact]
    public async Task A_write_that_fails_is_exit_3_and_its_staging_folder_is_removed()
    {
        _verb.AddInstrument("AAA");
        _verb.Writer = new ExportFolderWriter((_, _) => throw new IOException("disk full"));

        var exit = await _verb.RunAsync(Common, Ct);

        exit.ShouldBe(3);
        _verb.Error.ToString().ShouldContain("failed: IOException: disk full");
        Folders().ShouldNotContain(".staging-");
        Directory.EnumerateDirectories(Path.Combine(_verb.ExportRoot, "sim")).ShouldBeEmpty();
    }

    [Fact]
    public void The_first_ctrl_c_cancels_the_token_and_is_swallowed_and_the_second_is_not()
    {
        using var cancel = new CancellationTokenSource();
        var interrupt = new SimulateProgram.InterruptCounter(cancel);

        interrupt.Pressed().ShouldBeTrue();
        cancel.IsCancellationRequested.ShouldBeTrue();
        interrupt.Pressed().ShouldBeFalse("a stuck write can still be killed");
    }

    // the log lines and the secrets

    [Fact]
    public async Task The_verb_logs_its_own_lines_with_ids_4405_to_4409_and_only_ids_codes_counts_and_the_run_id()
    {
        _verb.AddInstrument("AAA");

        await _verb.RunAsync(Common, Ct);
        await _verb.RunAsync(Common, Ct);
        _verb.Run = _ => Result.Failure<SimulationRun>(SimulationErrors.BasketNotFound);
        await _verb.RunAsync(Common, Ct);
        _verb.Run = _ => Result.Success(FabricatedRun.Standard());
        _verb.RunThrows = () => new InvalidOperationException("boom");
        await _verb.RunAsync(Common, Ct);

        var lines = _verb.Logs.Lines.ToList();
        lines.Select(l => l.EventId).Distinct().Order().ShouldBe([4405, 4406, 4407, 4408, 4409]);
        lines.Count(l => l.EventId == 4405).ShouldBe(4);
        lines.Single(l => l.EventId == 4406).Arguments.Where(a => a.Key == "Code").Select(a => a.Value).ShouldBe(["simulation.basket_not_found"]);
        lines.Single(l => l.EventId == 4407).Text.ShouldContain("5 files");
        foreach (var argument in lines.SelectMany(l => l.Arguments).Where(a => a.Key != "{OriginalFormat}"))
        {
            (argument.Value is string or int or long or DateOnly).ShouldBeTrue(argument.Key + " is " + argument.Value?.GetType().Name);
            (argument.Value?.ToString() ?? string.Empty).ShouldNotContain(_verb.Root.Path);
            (argument.Value?.ToString() ?? string.Empty).ShouldNotContain(_verb.Password);
        }
    }

    [Fact]
    public async Task The_database_password_is_in_no_output_log_line_or_file_of_a_success_a_refusal_and_a_failure()
    {
        _verb.AddInstrument("AAA");
        await _verb.RunAsync(Common, Ct);
        _verb.Run = _ => Result.Failure<SimulationRun>(SimulationErrors.BasketNotFound);
        await _verb.RunAsync(Common + " --fills no-slippage", Ct);
        _verb.RunThrows = () => new TimeoutException("could not reach the database");
        _verb.Run = _ => Result.Success(FabricatedRun.Standard());
        await _verb.RunAsync(Common + " --timeframe 5m", Ct);

        var password = System.Text.Encoding.UTF8.GetBytes(_verb.Password);
        _verb.Out.ToString().ShouldNotContain(_verb.Password);
        _verb.Error.ToString().ShouldNotContain(_verb.Password);
        _verb.Logs.Lines.ShouldAllBe(l => !l.Text.Contains(_verb.Password, StringComparison.Ordinal));
        foreach (var file in Directory.EnumerateFiles(_verb.ExportRoot, "*", SearchOption.AllDirectories))
        {
            File.ReadAllBytes(file).AsSpan().IndexOf(password).ShouldBe(-1, file);
        }
    }
}
