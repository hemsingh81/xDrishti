using System.Text.Json;
using System.Text.RegularExpressions;
using XDrishti.Domain.Research;
using XDrishti.SimExport.Cards;
using XDrishti.SimExport.Tests.Support;
using XDrishti.SimExport.Verification;

namespace XDrishti.SimExport.Tests.Verification;

/// <summary>Criteria 8 and 12 of P3-09 for the library: the contents of <c>pack.json</c>, the log, the engine values and the index; what they leave out; the escaping; the card names; the pack key.</summary>
public sealed partial class PackFilesTests
{
    private const string Seed = "p3-check-1";

    private static readonly string[] ExitSpellings = ["stop", "target", "square_off", "max_hold", "end_of_data", "ema_exit"];

    private static (SimulationRun Run, PackRun Pack, PackPlan Plan) Standard()
    {
        var run = FabricatedRun.Standard();
        var pack = new PackRun(run, RunLabels.Of(run, FillRules.Default));
        return (run, pack, PackPlanner.Plan([run], Seed, 5));
    }

    private static string Json(PackPlan plan, PackRun pack, Guid? basket = null) =>
        Sinks.Render(s => PackFiles.WritePackJson(s, Seed, 5, "p3-check-1-0123456789ab", new string('a', 64), basket, [pack], plan));

    [GeneratedRegex(@"\d\s?%", RegexOptions.CultureInvariant)]
    private static partial Regex Percent();

    private static void NoStatistic(string text, string name)
    {
        ForbiddenWords.In(text).ShouldBeEmpty(name);
        Percent().IsMatch(text).ShouldBeFalse(name);
    }

    [Fact]
    public void The_pack_json_parses_and_holds_the_seed_the_sampler_the_run_the_hashes_the_rows_and_the_category_table_with_occurs()
    {
        var (run, pack, plan) = Standard();

        using var json = JsonDocument.Parse(Json(plan, pack));
        var root = json.RootElement;

        root.GetProperty("format").GetString().ShouldBe("xd-verify-pack-1");
        root.GetProperty("seed").GetString().ShouldBe(Seed);
        root.GetProperty("sampler").GetString().ShouldBe("verify-sampler-1");
        root.TryGetProperty("row_count", out _).ShouldBeFalse("the number of rows would reveal how many edge picks were added");
        root.GetProperty("session_notes_cut").GetBoolean().ShouldBeFalse();
        var runElement = root.GetProperty("runs")[0];
        runElement.GetProperty("run_key").GetString().ShouldBe(run.RunKey);
        runElement.GetProperty("engine_version").GetString().ShouldBe(run.EngineVersion);
        runElement.GetProperty("parameters").GetProperty("or_min").GetString().ShouldBe("15");
        runElement.GetProperty("costs").GetString().ShouldBe(CostDescription.None);
        runElement.GetProperty("first_bar").GetString().ShouldBe("2024-04-01");
        root.GetProperty("instruments").EnumerateArray().ShouldAllBe(i => i.GetProperty("bars_sha256").GetString()!.Length == 64 && i.GetProperty("bars_sha256_12").GetString()!.Length == 12);
        root.GetProperty("rows").GetArrayLength().ShouldBe(plan.Rows.Count);
        root.GetProperty("categories").EnumerateArray().Select(c => c.GetProperty("code").GetString()).ShouldBe(EdgeCategories.Codes);
        root.GetProperty("categories").EnumerateArray().ShouldAllBe(c => c.GetProperty("occurs").ValueKind == JsonValueKind.True || c.GetProperty("occurs").ValueKind == JsonValueKind.False);
    }

    [Fact]
    public void The_pack_json_says_when_the_session_notes_of_an_instrument_were_cut_so_a_kind_of_spot_check_may_be_unseen()
    {
        var (run, _, plan) = Standard();
        var cut = run with { Instruments = [.. run.Instruments.Select(i => i with { NotesTruncated = true })] };

        using var json = JsonDocument.Parse(Json(plan, new PackRun(cut, RunLabels.Of(cut, FillRules.Default))));

        json.RootElement.GetProperty("session_notes_cut").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public void The_pack_json_the_log_the_index_and_the_manifest_lines_name_no_source_category_or_exit_reason_of_a_row()
    {
        var (run, pack, plan) = Standard();
        using var json = JsonDocument.Parse(Json(plan, pack));

        foreach (var row in json.RootElement.GetProperty("rows").EnumerateArray())
        {
            row.EnumerateObject().Select(p => p.Name).ShouldBe(["seq", "trade_key", "symbol", "side", "date", "run_key"]);
        }

        var log = Sinks.Render(s => PackFiles.WriteCheckLog(s, plan));
        var index = Sinks.Render(s => PackIndexHtml.Write(s, "p3-check-1-0123456789ab", Seed, [pack], plan));
        var extra = string.Join('\n', PackFiles.ExtraLines(Seed, 5, [run], plan));
        var names = string.Join('\n', plan.Rows.Select(r => PackFiles.CardName(r.Seq, plan.Rows.Count, r.Trade.Trade)));
        var spotted = new[] { string.Join('\n', log.Split('\n').Skip(1)), extra, names, Rows(index) };
        foreach (var (text, label) in spotted.Zip(["log", "manifest lines", "card names", "index rows"]))
        {
            foreach (var code in EdgeCategories.Codes.Where(c => c != "sell").Concat(ExitSpellings).Concat([PackPlanner.RandomSource, PackPlanner.EdgeSource]))
            {
                text.ShouldNotContain(code, Case.Insensitive, label + " names '" + code + "'");
            }
        }
    }

    private static string Rows(string index) => string.Join('\n', index.Split('\n').Where(l => l.StartsWith("<tr>", StringComparison.Ordinal)));

    [Fact]
    public void The_texts_a_reader_sees_hold_no_count_of_a_group_no_sum_or_mean_of_r_and_no_percentage_apart_from_the_run_banner_which_states_a_parameter()
    {
        var (run, _, plan) = Standard();
        var bare = new PackRun(run, new RunLabelSet(["costs: none", RunLabels.EngineVerification], []));

        NoStatistic(Json(plan, bare), "pack.json");
        NoStatistic(PackFiles.ReadmeText("p3-check-1-0123456789ab", Seed, [bare]), "README.txt");
        NoStatistic(HtmlReader.VisibleText(HtmlReader.Read(Sinks.Render(s => PackIndexHtml.Write(s, "p3-check-1-0123456789ab", Seed, [bare], plan)))), "index.html");
        ForbiddenWords.In(Json(plan, new PackRun(run, RunLabels.Of(run, FillRules.Default)))).ShouldBeEmpty("pack.json with the real labels");
    }

    [Fact]
    public void The_readme_begins_with_the_banner_of_every_run_names_the_seed_and_says_that_nothing_in_it_is_evidence_of_an_edge()
    {
        var (_, pack, _) = Standard();

        var readme = PackFiles.ReadmeText("p3-check-1-0123456789ab", Seed, [pack, pack]);

        readme.ShouldStartWith(RunBanner(pack) + RunBanner(pack));
        readme.ShouldContain("seed: " + Seed);
        readme.ShouldContain("Nothing in it is evidence of an edge");
        readme.ShouldContain("costs: none");
    }

    private static string RunBanner(PackRun pack) => XDrishti.SimExport.Text.RunBanner.Text(pack.Labels) + "\n";

    [Fact]
    public void The_blank_log_has_the_header_of_the_protocol_one_row_per_trade_and_per_spot_line_and_nothing_of_the_engine()
    {
        var (_, _, plan) = Standard();

        var rows = Rfc4180Reader.Read(Sinks.Render(s => PackFiles.WriteCheckLog(s, plan)));

        rows[0].ShouldBe(PackFiles.CheckLogColumns);
        rows[0].ShouldBe(
        [
            "seq", "trade_key", "kind", "chart", "seen_entry_time", "seen_entry_price", "seen_stop", "seen_target", "seen_exit_time", "seen_exit_price", "seen_exit_reason", "seen_r",
            "ext_open", "ext_high", "ext_low", "ext_close", "verdict", "finding", "notes", "signed_on",
        ]);
        rows.Count.ShouldBe(1 + plan.Rows.Count + plan.Spots.Count);
        rows.Skip(1).ShouldAllBe(r => r.Skip(3).All(string.IsNullOrEmpty));
        rows.Skip(1).Select(r => r[2]).Distinct().ShouldBe(["trade", "spot"], ignoreOrder: true);
        PackFiles.CheckLogColumns.ShouldNotContain(c => c.Contains("source", StringComparison.Ordinal) || c.Contains("categor", StringComparison.Ordinal) || c.StartsWith("engine", StringComparison.Ordinal));
    }

    [Fact]
    public void The_engine_values_have_the_source_the_categories_and_the_values_of_the_record_to_the_printed_precision_and_spot_lines_with_their_kind()
    {
        var (_, _, plan) = Standard();

        var rows = Rfc4180Reader.Read(Sinks.Render(s => PackFiles.WriteEngineValues(s, plan)));

        rows[0].ShouldBe(PackFiles.EngineValueColumns);
        rows.Count.ShouldBe(1 + plan.Rows.Count + plan.Spots.Count);
        foreach (var row in plan.Rows)
        {
            var cells = rows.Single(r => r[0] == row.Seq.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var map = PackFiles.EngineValueColumns.Zip(cells).ToDictionary(p => p.First, p => p.Second);
            var t = row.Trade.Trade;
            map["trade_key"].ShouldBe(t.Key);
            map["source"].ShouldBe(row.Source);
            map["categories"].ShouldBe(string.Join(';', row.Categories));
            double.Parse(map["entry_price"], System.Globalization.CultureInfo.InvariantCulture).ShouldBe(t.EntryPrice);
            double.Parse(map["stop_price"], System.Globalization.CultureInfo.InvariantCulture).ShouldBe(t.StopPrice);
            double.Parse(map["exit_price"], System.Globalization.CultureInfo.InvariantCulture).ShouldBe(t.ExitPrice);
            double.Parse(map["r_exact"], System.Globalization.CultureInfo.InvariantCulture).ShouldBe(t.RExact);
            map["exit_reason"].ShouldBe(ExportSpellings.Of(t.ExitReason));
            map["tick"].ShouldBe(t.Symbol == "NSE:AAA" ? "0.05" : "0.1");
        }

        rows.Where(r => r[2] == "spot").ShouldAllBe(r => r.Skip(4).All(string.IsNullOrEmpty) && r[3].Length > 0);
    }

    [Fact]
    public void The_costed_engine_values_carry_the_net_r_and_the_charge_total()
    {
        var run = FabricatedRun.CostedStandard();
        var plan = PackPlanner.Plan([run], Seed, 5);

        var rows = Rfc4180Reader.Read(Sinks.Render(s => PackFiles.WriteEngineValues(s, plan)));

        var trade = rows.First(r => r[2] != "spot").Zip(PackFiles.EngineValueColumns, (v, c) => (c, v)).ToDictionary(p => p.c, p => p.v);
        trade["r_net"].ShouldNotBeEmpty();
        trade["cost_total"].ShouldNotBeEmpty();
    }

    [Fact]
    public void A_symbol_with_an_ampersand_or_a_comma_is_quoted_and_a_cell_that_starts_with_a_formula_character_is_defused_in_both_csv_files()
    {
        var hostile = PackFixtures.Neutral("-ORB", "NSE:A,B&C", new DateOnly(2025, 4, 8));
        var run = PackFixtures.Run("-ORB", [hostile]);
        var plan = PackPlanner.Plan([run], Seed, 1);

        var log = Sinks.Render(s => PackFiles.WriteCheckLog(s, plan));
        var engine = Sinks.Render(s => PackFiles.WriteEngineValues(s, plan));

        foreach (var text in new[] { log, engine })
        {
            text.ShouldContain("\"'-ORB|NSE:A,B&C|Buy|2025-04-08\"");
            Rfc4180Reader.Read(text)[1][1].ShouldBe("'-ORB|NSE:A,B&C|Buy|2025-04-08");
        }
    }

    [Fact]
    public void The_index_has_a_row_and_a_relative_link_per_trade_the_spot_questions_and_passes_the_scan_for_a_hostile_symbol_too()
    {
        var hostile = PackFixtures.Neutral("ORB", "NSE:<script>x</script>‮&\"'", new DateOnly(2025, 4, 8));
        var run = PackFixtures.Run("ORB", [hostile], [hostile with { }]);
        var pack = new PackRun(run, RunLabels.Of(run, FillRules.Default));
        var plan = PackPlanner.Plan([run], Seed, 1);

        var html = Sinks.Render(s => PackIndexHtml.Write(s, "p3-check-1-0123456789ab", Seed, [pack], plan));

        CardScan.Problems(html, isIndex: true).ShouldBeEmpty();
        html.ShouldNotContain("<script>x");
        html.ShouldNotContain("‮");
        var links = HtmlReader.Read(html).OfType<HtmlToken.Start>().Where(t => t.Name == "a").Select(t => t.Attr("href")!).ToList();
        links.ShouldBe([PackFiles.CardName(1, 1, hostile)]);
        html.ShouldContain("spot checks");
        html.ShouldContain("do the stored bars of this day end where the data does?");
    }

    [Fact]
    public void The_card_name_has_no_exit_reason_is_numbered_with_three_digits_and_no_longer_than_80_characters()
    {
        var trade = PackFixtures.Neutral("ORB", "NSE:M&M-VERY.LONG_SYMBOL_THAT_IS_CUT_TO_24", new DateOnly(2025, 4, 8), Side.Sell);

        var name = PackFiles.CardName(7, 21, trade);

        name.ShouldBe("cards/007-M_M-VERY.LONG_SYMBOL_THA-sell-2025-04-08.html");
        name.Length.ShouldBeLessThanOrEqualTo(CardNames.MaxName);
        PackFiles.CardName(7, 1500, trade).ShouldStartWith("cards/0007-");
        Should.Throw<ArgumentOutOfRangeException>(() => PackFiles.CardName(0, 5, trade));
    }

    [Fact]
    public void The_pack_key_covers_the_seed_the_count_the_basket_and_the_runs_and_the_pack_id_is_a_safe_folder_name_for_every_valid_seed()
    {
        var run = FabricatedRun.Standard();
        var basketA = new Guid("11111111-2222-3333-4444-555555555555");
        var basketB = new Guid("11111111-2222-3333-4444-555555555556");

        var key = PackFiles.PackKey(Seed, 5, null, [run]);

        key.ShouldMatch("^[0-9a-f]{64}$");
        PackFiles.PackKey(Seed, 5, null, [run]).ShouldBe(key);
        new[]
        {
            PackFiles.PackKey("p3-check-2", 5, null, [run]), PackFiles.PackKey(Seed, 6, null, [run]), PackFiles.PackKey(Seed, 5, basketA, [run]), PackFiles.PackKey(Seed, 5, basketB, [run]),
            PackFiles.PackKey("P3-CHECK-1", 5, null, [run]),
        }.Append(key).Distinct().Count().ShouldBe(6, "each of the five differs from the key and from each other");
        PackFiles.PackId("P3-Check.1_x", key).ShouldBe("p3-check-1-x-" + key[..12]);
        PackFiles.PackId("---", key).ShouldBe("pack-" + key[..12]);
        PackFiles.PackId("P3-CHECK-1", PackFiles.PackKey("P3-CHECK-1", 5, null, [run])).ShouldNotBe(PackFiles.PackId("p3-check-1", key), "the hex part keeps two seeds that differ only in case apart");
        Regex.IsMatch(PackFiles.PackId(new string('A', 40), key), "^[a-z0-9][a-z0-9-]{0,79}$").ShouldBeTrue();
    }

    [Fact]
    public void The_extra_lines_carry_the_seed_the_sampler_the_count_each_run_and_each_row_without_a_source()
    {
        var (run, _, plan) = Standard();

        var lines = PackFiles.ExtraLines(Seed, 5, [run], plan);

        lines.Take(4).ShouldBe(["pack.seed=" + Seed, "pack.sampler=verify-sampler-1", "pack.count=5", "pack.run=ORB|" + run.RunKey]);
        lines.Where(l => l.StartsWith("pack.row=", StringComparison.Ordinal)).Count().ShouldBe(plan.Rows.Count);
        lines.ShouldAllBe(l => !l.Contains('\n') && !l.Contains('\r'));
    }

    [Fact]
    public void Two_renderings_of_every_file_are_identical_bytes_and_the_files_equal_the_golden_files()
    {
        var (run, pack, plan) = Standard();
        Action<System.Buffers.IBufferWriter<byte>>[] writers =
        [
            s => PackFiles.WritePackJson(s, Seed, 5, "p3-check-1-0123456789ab", new string('a', 64), null, [pack], plan),
            s => PackFiles.WriteCheckLog(s, plan),
            s => PackFiles.WriteEngineValues(s, plan),
            s => PackIndexHtml.Write(s, "p3-check-1-0123456789ab", Seed, [pack], plan),
        ];
        foreach (var write in writers)
        {
            Sinks.Bytes(write).ShouldBe(Sinks.Bytes(write));
        }

        Golden.CheckPack("pack-json", Sinks.Render(writers[0]));
        Golden.CheckPack("check-log", Sinks.Render(writers[1]));
        Golden.CheckPack("engine-values", Sinks.Render(writers[2]));
        Golden.CheckPack("pack-index", Sinks.Render(writers[3]));
        Golden.CheckPack("pack-readme", PackFiles.ReadmeText("p3-check-1-0123456789ab", Seed, [pack]));
        run.RunKey.Length.ShouldBe(64);
    }
}
