using XDrishti.SimExport.Manifest;

namespace XDrishti.Api.IntegrationTests.Simulate;

/// <summary>
/// Criteria 2, 3, 5, 7 and 8 of P4-05 through the verb over the real host and a real TimescaleDB: <c>--exit</c> runs a policy that no verb could run before (a single exit is an ordinary run with that exit in place of the built-in's), a list of values
/// is a comparison on the same entries (its folder, its tables, its keys), <c>--store</c> keeps the legs and the comparison, and each refusal of the flags is exit 2.
/// </summary>
public sealed partial class SimulateDatabaseTests
{
    private const string ExitArgs = "--strategy ORB --symbols ABB,TCS " + Window;

    private const string Be = " --exit PARTIAL_BE:book_pct=50,t1_rr=1,t2_rr=2";

    [Fact]
    public async Task A_single_exit_runs_the_policy_end_to_end_and_the_trades_carry_their_legs()
    {
        await SeedStoreInstrumentsAsync();
        using var temp = new TempFolder();

        var outcome = await VerbAsync(ExitArgs + Be, temp);

        outcome.Exit.ShouldBe(0, outcome.Error);
        var folder = RunFolder(temp);
        var trades = File.ReadAllLines(Path.Combine(folder, "trades.csv"));
        trades.Length.ShouldBeGreaterThan(1);
        trades.Skip(1).ShouldContain(l => l.Contains("|entry|", StringComparison.Ordinal) && l.Contains(';', StringComparison.Ordinal), "a trade that booked T1 shows its legs in the legs column");
        File.Exists(Path.Combine(folder, "comparison.csv")).ShouldBeFalse("one candidate is an ordinary run");
        outcome.Out.ShouldNotContain("comparison key");
    }

    [Fact]
    public async Task A_single_exit_of_FIXED_RR_gives_the_trades_of_the_param_rr_it_replaces_and_a_run_key_of_its_own()
    {
        await SeedStoreInstrumentsAsync();
        using var viaExit = new TempFolder();
        using var viaParam = new TempFolder();

        var a = await VerbAsync(ExitArgs + " --exit FIXED_RR:rr=3", viaExit);
        var b = await VerbAsync(ExitArgs + " --param rr=3", viaParam);

        a.Exit.ShouldBe(0, a.Error);
        b.Exit.ShouldBe(0, b.Error);
        static List<string> WithoutRunKey(string folder) => [.. File.ReadAllLines(Path.Combine(folder, "trades.csv")).Select(l => l[(l.IndexOf(',', StringComparison.Ordinal) + 1)..])];
        WithoutRunKey(RunFolder(viaExit)).ShouldBe(WithoutRunKey(RunFolder(viaParam)), "an exit replaces the exit of the built-in and nothing else");
        ExportManifest.ParseBytes(File.ReadAllBytes(Path.Combine(RunFolder(viaExit), "manifest.json"))).Value.RunKey
            .ShouldNotBe(ExportManifest.ParseBytes(File.ReadAllBytes(Path.Combine(RunFolder(viaParam), "manifest.json"))).Value.RunKey);
    }

    [Fact]
    public async Task A_list_of_values_is_a_comparison_with_its_folder_its_tables_and_its_keys_and_the_same_entries()
    {
        await SeedStoreInstrumentsAsync();
        using var temp = new TempFolder();

        var outcome = await VerbAsync(ExitArgs + " --exit FIXED_RR:rr=2|3", temp);

        outcome.Exit.ShouldBe(0, outcome.Error);
        var folder = RunFolder(temp);
        var files = Directory.EnumerateFiles(folder).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList();
        files.ShouldContain("comparison.csv");
        files.ShouldContain("diagnostics.csv");
        files.ShouldContain("manifest.json");
        files.ShouldContain("README.txt");
        files.ShouldContain("trades.csv");
        files.ShouldContain("run.json");
        files.ShouldContain(f => f!.StartsWith("trades-02-FIXED_RR_rr_3_", StringComparison.Ordinal));
        files.ShouldContain(f => f!.StartsWith("run-02-", StringComparison.Ordinal));
        outcome.Out.ShouldContain("candidates compared on the same entries; the best row is in-sample on the data shown and is not evidence of an edge");
        outcome.Out.ShouldContain("comparison key: ");
        outcome.Out.ShouldContain("entries key: ");
        outcome.Out.ShouldContain("FIXED_RR{rr=2}");
        outcome.Out.ShouldContain("FIXED_RR{rr=3}");
        outcome.Out.ShouldContain("MAE/MFE diagnostics");
        outcome.Out.ShouldContain("CENSORED");
        outcome.Out.ShouldNotContain("gate not applied", Case.Sensitive);
        var csv = File.ReadAllLines(Path.Combine(folder, "comparison.csv"));
        csv[0].ShouldStartWith("candidate,sample,unit,trades,wins,win_rate,mean_r");
        csv.Length.ShouldBe(5, "a header and the own and the common rows of two candidates");
        var first = File.ReadAllLines(Path.Combine(folder, "trades.csv")).Skip(1).Select(l => l.Split(',')[^1]).Order(StringComparer.Ordinal).ToList();
        var second = File.ReadAllLines(Path.Combine(folder, files.Single(f => f!.StartsWith("trades-02-", StringComparison.Ordinal))!)).Skip(1).Select(l => l.Split(',')[^1]).Order(StringComparer.Ordinal).ToList();
        first.ShouldNotBeEmpty();
        second.ShouldBe(first, "the same entries: the trade keys are the same set");
        Path.GetFileName(folder).ShouldBe(RunId.Of("ORB", D1, D3, outcome.Out.Split('\n').Single(l => l.StartsWith("comparison key: ", StringComparison.Ordinal))["comparison key: ".Length..]));
    }

    [Fact]
    public async Task The_same_comparison_again_is_unchanged_and_with_store_it_is_stored_then_already_stored_with_its_members_joined_on_the_trade_key()
    {
        await SeedStoreInstrumentsAsync();
        using var temp = new TempFolder();
        var args = ExitArgs + " --exit FIXED_RR:rr=2.5|3.5 --store";

        var first = await VerbAsync(args, temp);
        var second = await VerbAsync(args, temp);

        first.Exit.ShouldBe(0, first.Error);
        second.Exit.ShouldBe(0, second.Error);
        second.Out.ShouldContain("unchanged");
        var key = first.Out.Split('\n').Single(l => l.StartsWith("comparison key: ", StringComparison.Ordinal))["comparison key: ".Length..];
        var entries = first.Out.Split('\n').Single(l => l.StartsWith("entries key: ", StringComparison.Ordinal))["entries key: ".Length..];
        first.Out.ShouldContain("stored: comparison " + key[..12] + ", 2 members");
        second.Out.ShouldContain("already stored: comparison " + key[..12]);
        (await ResearchCountAsync("SELECT count(*) FROM research.sim_comparisons WHERE comparison_key = @k AND entries_key = @e AND cardinality(member_run_keys) = 2", ("k", key), ("e", entries))).ShouldBe(1);
        var joined = await ResearchCountAsync(
            """
            SELECT count(*) FROM research.sim_comparisons c
            JOIN research.sim_runs ra ON ra.run_key = c.member_run_keys[1]
            JOIN research.sim_runs rb ON rb.run_key = c.member_run_keys[2]
            JOIN research.sim_trades ta ON ta.run_id = ra.id AND ta.open_at_end = false
            JOIN research.sim_trades tb ON tb.run_id = rb.id AND tb.open_at_end = false AND tb.trade_key = ta.trade_key
            WHERE c.comparison_key = @k
            """,
            ("k", key));
        joined.ShouldBeGreaterThan(0);
        (await ResearchCountAsync("SELECT count(*) FROM research.sim_runs r JOIN research.sim_comparisons c ON r.run_key = ANY(c.member_run_keys) WHERE c.comparison_key = @k", ("k", key))).ShouldBe(2);
    }

    [Fact]
    public async Task A_run_with_legs_is_stored_with_them_since_P4_05()
    {
        await SeedStoreInstrumentsAsync();
        using var temp = new TempFolder();

        var outcome = await VerbAsync(ExitArgs + Be + " --store", temp);

        outcome.Exit.ShouldBe(0, outcome.Error);
        var runKey = ExportManifest.ParseBytes(File.ReadAllBytes(Path.Combine(RunFolder(temp), "manifest.json"))).Value.RunKey;
        outcome.Out.ShouldContain("stored: run " + runKey[..12]);
        (await ResearchCountAsync("SELECT count(*) FROM research.sim_trade_legs l JOIN research.sim_runs r ON r.id = l.run_id WHERE r.run_key = @k", ("k", runKey))).ShouldBeGreaterThan(2);
    }

    [Fact]
    public async Task A_pack_of_one_exit_is_the_hand_check_of_a_policy_and_a_pack_of_a_list_is_a_usage_error()
    {
        await SeedPackDaysAsync();
        using var temp = new TempFolder();
        using var refused = new TempFolder();

        var outcome = await PackAsync("--strategies ORB --basket Lab " + PackWindow + " --count 3 --exit PARTIAL_BE:book_pct=50,t1_rr=1,t2_rr=2", temp);
        var list = await PackAsync("--strategies ORB --basket Lab " + PackWindow + " --count 3 --exit FIXED_RR:rr=2|3", refused);

        outcome.Exit.ShouldBe(0, outcome.Error);
        File.ReadAllText(Path.Combine(PackFolder(temp), "engine-values.csv")).ShouldNotBeEmpty();
        list.Exit.ShouldBe(2);
        list.Error.ShouldContain("a pack checks one exit set");
        Directory.Exists(Path.Combine(refused.Root, "verify")).ShouldBeFalse();
    }

    [Theory]
    [InlineData(" --exit FIXED_RRR:rr=2", "'--exit FIXED_RRR:rr=2'")]
    [InlineData(" --exit FIXED_RR:risk=2", "'--exit FIXED_RR:risk=2'")]
    [InlineData(" --exit EMA_TRAIL:length=5|9", "takes one value")]
    [InlineData(" --exit FIXED_RR:rr=2 --param rr=3", "'--param rr' and '--exit'")]
    [InlineData(" --exit EVENT_EXIT:days_before_results=1", "not run yet")]
    [InlineData(" --exit FIXED_RR:rr=2 --exit FIXED_RR:rr=2", "FIXED_RR{rr=2} twice")]
    [InlineData(" --time-stop min_progress_rr=0.5,intraday_bars=6", "needs at least one '--exit'")]
    public async Task A_refused_flag_is_a_usage_error_exit_2_that_names_the_flag_and_the_value(string flags, string expected)
    {
        await SeedStoreInstrumentsAsync();
        using var temp = new TempFolder();

        var outcome = await VerbAsync(ExitArgs + flags, temp);

        outcome.Exit.ShouldBe(2);
        outcome.Error.ShouldContain(expected);
        outcome.Error.ShouldContain("usage:");
        Directory.Exists(Path.Combine(temp.Root, "sim")).ShouldBeFalse("nothing was written");
    }

    [Fact]
    public async Task A_candidate_the_simulator_cannot_run_is_refused_by_name_with_its_index_and_nothing_is_written()
    {
        await SeedStoreInstrumentsAsync();
        using var temp = new TempFolder();

        var outcome = await VerbAsync(ExitArgs + " --exit FIXED_RR:rr=2|20", temp);

        outcome.Exit.ShouldBe(1);
        outcome.Error.ShouldContain("candidates[1].");
        Directory.Exists(Path.Combine(temp.Root, "sim")).ShouldBeFalse();
    }

    [Fact]
    public async Task The_list_of_strategies_also_lists_the_policies_and_their_parameters()
    {
        using var temp = new TempFolder();

        var outcome = await VerbAsync("--list-strategies", temp);

        outcome.Exit.ShouldBe(0, outcome.Error);
        outcome.Out.ShouldContain("--exit POLICY:name=value");
        outcome.Out.ShouldContain("FIXED_RR: rr (candidate lists with '|': rr)");
        outcome.Out.ShouldContain("PYRAMID: initial_pct, add_at_rr, add_pct, max_adds, trail_atr_mult, atr_period");
        outcome.Out.ShouldContain("--time-stop");
    }
}
