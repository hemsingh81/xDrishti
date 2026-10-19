using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using XDrishti.Domain.Market.Actions;
using XDrishti.Domain.Research;
using XDrishti.SimExport.Json;
using XDrishti.SimExport.Tests.Support;

namespace XDrishti.SimExport.Tests.Json;

/// <summary>Criterion 4 of P3-04b1: <c>run.json</c>.</summary>
public sealed partial class SimExportJsonTests
{
    private const string Format = "xd-sim-export-1";

    private static string Json(SimulationRun run, string format = Format, FillRules? rules = null) =>
        Sinks.Render(s => SimExportJson.Write(s, run, RunLabels.Of(run, rules ?? FillRules.Default), format));

    private static JsonElement Root(string json) => JsonDocument.Parse(json).RootElement;

    [GeneratedRegex(@"\d{2}:\d{2}:\d{2}|T\d{2}:\d{2}")]
    private static partial Regex ClockTime();

    [Fact]
    public void The_file_equals_the_golden_file_golden()
    {
        Golden.Check("run-json", Json(FabricatedRun.Standard()));
    }

    [Fact]
    public void The_labels_object_equals_the_golden_file_golden()
    {
        var labels = Root(Json(FabricatedRun.Standard())).GetProperty("labels");

        Golden.Check("run-json-labels", labels.GetRawText());
    }

    [Fact]
    public void The_labels_object_has_the_lines_the_limits_and_the_oracle_agreement_flag()
    {
        var run = FabricatedRun.Standard();
        var labels = Root(Json(run)).GetProperty("labels");
        var expected = RunLabels.Of(run, FillRules.Default);

        labels.EnumerateObject().Select(p => p.Name).ShouldBe(["lines", "not_modelled", "engine_verified"]);
        labels.GetProperty("lines").EnumerateArray().Select(e => e.GetString()).ShouldBe(expected.Lines);
        labels.GetProperty("not_modelled").EnumerateArray().Select(e => (e.GetProperty("code").GetString()!, e.GetProperty("text").GetString()!)).ShouldBe(expected.NotModelled.Select(l => (l.Code, l.Text)));
        labels.GetProperty("engine_verified").GetBoolean().ShouldBe(RunLabels.IsVerified(run.EngineVersion));
        labels.GetProperty("engine_verified").GetBoolean().ShouldBeTrue();
        expected.Lines.Contains(RunLabels.EngineVerification).ShouldBeTrue();
    }

    [Fact]
    public void A_run_of_another_engine_version_is_written_with_the_fallback_line_and_engine_verified_false()
    {
        var run = FabricatedRun.Standard() with { EngineVersion = "sim-6" };
        var labels = Root(Json(run)).GetProperty("labels");

        labels.GetProperty("engine_verified").GetBoolean().ShouldBeFalse();
        labels.GetProperty("lines").EnumerateArray().Select(e => e.GetString()).ShouldContain("engine: not yet verified for sim-6");
    }

    [Fact]
    public void The_top_level_is_export_labels_run_in_that_order_and_the_export_header_is_what_the_caller_gave()
    {
        var root = Root(Json(FabricatedRun.Standard(), "xd-sim-export-9"));

        root.EnumerateObject().Select(p => p.Name).ShouldBe(["export", "labels", "run"]);
        root.GetProperty("export").GetProperty("format").GetString().ShouldBe("xd-sim-export-9");
        root.GetProperty("export").GetProperty("schema").GetInt32().ShouldBe(1);
    }

    [Fact]
    public void The_file_is_compact_on_one_line()
    {
        var json = Json(FabricatedRun.Standard());

        json.ShouldNotContain("\n");
        json.ShouldNotContain("\r");
        json.ShouldStartWith("{\"export\":{\"format\":");
        json.ShouldNotContain("{ ");
        json.ShouldNotContain("[ ");
    }

    [Fact]
    public void It_parses_back_with_the_key_the_trades_the_instruments_and_the_totals_of_the_record()
    {
        var run = FabricatedRun.Standard();
        var parsed = Root(Json(run)).GetProperty("run");

        parsed.GetProperty("run_key").GetString().ShouldBe(run.RunKey);
        parsed.GetProperty("schema_version").GetInt32().ShouldBe(run.SchemaVersion);
        parsed.GetProperty("engine_version").GetString().ShouldBe(run.EngineVersion);
        parsed.GetProperty("trades").EnumerateArray().Select(t => t.GetProperty("trade").GetProperty("key").GetString()).ShouldBe(run.Trades.Select(t => t.Trade.Key));
        parsed.GetProperty("open_at_end").EnumerateArray().Select(t => t.GetProperty("trade").GetProperty("key").GetString()).ShouldBe(run.OpenAtEnd.Select(t => t.Trade.Key));
        parsed.GetProperty("trades")[0].GetProperty("trade").GetProperty("r_exact").GetDouble().ShouldBe(run.Trades[0].Trade.RExact);
        parsed.GetProperty("trades")[1].GetProperty("trade").GetProperty("slippage_r").GetDouble().ShouldBe(run.Trades[1].Trade.SlippageR);
        parsed.GetProperty("instruments").EnumerateArray().Select(i => i.GetProperty("symbol").GetString()).ShouldBe(run.Instruments.Select(i => i.Symbol));
        parsed.GetProperty("instruments").EnumerateArray().Select(i => i.GetProperty("skipped").GetString()).ShouldBe([null, null, "no_bars"]);
        parsed.GetProperty("instruments")[0].GetProperty("scans").GetArrayLength().ShouldBe(2);
        parsed.GetProperty("instruments")[0].GetProperty("scans")[0].GetProperty("conditions")[0].GetProperty("path").GetString().ShouldBe("buy.trigger[0]");
        parsed.GetProperty("instruments")[0].GetProperty("adjustments")[0].GetProperty("kind").GetString().ShouldBe("split");
        parsed.GetProperty("instruments")[0].GetProperty("discontinuities").GetArrayLength().ShouldBe(1);
        parsed.GetProperty("instruments")[0].GetProperty("notes")[0].GetProperty("reason").GetString().ShouldBe("special_session");
        parsed.GetProperty("totals").GetProperty("instruments").GetInt32().ShouldBe(run.Totals.Instruments);
        parsed.GetProperty("totals").GetProperty("fates").GetProperty("total").GetInt64().ShouldBe(run.Totals.Fates.Total);
        parsed.GetProperty("totals").GetProperty("skipped")[0].GetProperty("reason").GetString().ShouldBe("no_bars");
        parsed.GetProperty("universe").GetProperty("kind").GetString().ShouldBe("instrument_list");
    }

    [Fact]
    public void The_strategy_record_has_its_text_its_hash_and_the_parameters_with_sorted_keys()
    {
        var strategy = FabricatedRun.Strategy() with { Parameters = new Dictionary<string, double> { ["zz"] = 3, ["rr"] = 2, ["or_min"] = 15, ["Aa"] = 1 } };
        var run = FabricatedRun.Standard();
        var json = Json(run with { Strategy = strategy });

        var parsed = Root(json).GetProperty("run").GetProperty("strategy");

        parsed.GetProperty("parameters").EnumerateObject().Select(p => p.Name).ShouldBe(["Aa", "or_min", "rr", "zz"]);
        parsed.GetProperty("definition_json").GetString().ShouldBe(strategy.DefinitionJson);
        parsed.GetProperty("definition_sha256").GetString().ShouldBe(strategy.DefinitionSha256);
        parsed.GetProperty("evaluation_timeframe").GetString().ShouldBe("15m");
        parsed.GetProperty("sides").EnumerateArray().Select(e => e.GetString()).ShouldBe(["BUY", "SELL"]);
    }

    [Fact]
    public void The_order_a_dictionary_enumerates_in_does_not_change_the_file()
    {
        var run = FabricatedRun.Standard();
        var forward = new Dictionary<string, double> { ["a"] = 1, ["b"] = 2, ["c"] = 3 };
        var backward = new Dictionary<string, double> { ["c"] = 3, ["b"] = 2, ["a"] = 1 };

        Json(run with { Strategy = run.Strategy with { Parameters = forward } }).ShouldBe(Json(run with { Strategy = run.Strategy with { Parameters = backward } }));
    }

    [Fact]
    public void The_only_dictionary_of_the_record_is_the_parameters_so_a_new_one_cannot_be_written_unsorted()
    {
        RecordGraph.DictionaryProperties(typeof(SimulationRun)).ShouldBe(["StrategyRecord.Parameters"], "a new dictionary-typed property needs a converter that sorts its keys, and this test to name it");
    }

    [Fact]
    public void The_dictionary_scan_does_find_a_dictionary_property()
    {
        RecordGraph.DictionaryProperties(typeof(DictionarySentinel)).ShouldBe(["DictionarySentinel.Counts"]);
    }

    [Fact]
    public void Decimals_are_written_in_the_fewest_digits()
    {
        var run = FabricatedRun.Standard();
        var a = run.Instruments[0] with
        {
            TickSize = 0.0500m,
            Adjustments = [new AppliedAdjustment(new DateOnly(2025, 6, 2), CorporateActionKind.Bonus, 1, 3, 0.7500m, true)],
        };
        var json = Json(run with { Instruments = [a, .. run.Instruments.Skip(1)] });

        json.ShouldContain("\"tick_size\":0.05,");
        json.ShouldContain("\"factor\":0.75,");
        json.ShouldNotContain("\"tick_size\":0.0500");
        json.ShouldNotContain("\"factor\":0.7500");
        json.ShouldContain("\"kind\":\"bonus\"");
    }

    [Fact]
    public void Doubles_are_written_in_the_round_trip_form_and_negative_zero_as_zero()
    {
        var run = FabricatedRun.Standard();
        var trade = run.Trades[0].Trade with { MaeR = -0.0, RExact = 1.0 / 3, EntryPrice = 1e-7, ExitPrice = 1e21 };
        var json = Json(run with { Trades = [new RunTrade(trade, []), .. run.Trades.Skip(1)] });

        json.ShouldContain("\"mae_r\":0,");
        json.ShouldContain("\"r_exact\":0.3333333333333333,");
        json.ShouldContain("\"entry_price\":1E-07,");
        json.ShouldContain("\"exit_price\":1E+21,");
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void A_value_that_is_not_a_number_makes_the_writer_throw(double bad)
    {
        var run = FabricatedRun.Standard();
        var broken = new[]
        {
            run with { Trades = [new RunTrade(run.Trades[0].Trade with { RFilled = bad }, [])] },
            run with { Instruments = [run.Instruments[0] with { Discontinuities = [new DiscontinuityNote(new DateOnly(2025, 1, 1), bad)] }, .. run.Instruments.Skip(1)] },
            run with { Options = new ReplayOptions(bad, 0.2) },
            run with { Strategy = run.Strategy with { Parameters = new Dictionary<string, double> { ["rr"] = bad } } },
            run with { Trades = [new RunTrade(run.Trades[0].Trade with { TargetPrice = bad }, [])] },
        };

        foreach (var run2 in broken)
        {
            Should.Throw<ArgumentException>(() => Json(run2));
        }
    }

    [Fact]
    public void The_file_holds_no_clock_time_no_machine_path_and_no_culture_dependent_text()
    {
        var json = Json(FabricatedRun.Standard());

        ClockTime().IsMatch(json).ShouldBeFalse();
        json.ShouldNotContain("/Users/");
        json.ShouldNotContain("/home/");
        json.ShouldNotContain("\\\\");
    }

    [Fact]
    public void The_file_is_identical_under_another_culture()
    {
        var run = FabricatedRun.Standard();
        var expected = Json(run);
        var saved = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        try
        {
            var other = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            other.NumberFormat.NumberDecimalSeparator = ",";
            other.NumberFormat.NegativeSign = "−";
            other.DateTimeFormat.DateSeparator = ".";
            CultureInfo.CurrentCulture = other;
            CultureInfo.CurrentUICulture = other;

            Json(run).ShouldBe(expected);
        }
        finally
        {
            (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = saved;
        }
    }

    [Fact]
    public void A_no_slippage_run_says_so_in_the_trades_and_the_labels()
    {
        var rules = FillRules.Create(0.1, SlippageConfig.None).Value;
        var trade = FabricatedRun.Trade(FabricatedRun.IdA, "NSE:AAA", Side.Buy, FabricatedRun.Monday, fills: FillBasis.Exact);
        var run = FabricatedRun.Assemble(FabricatedRun.List((FabricatedRun.IdA, "NSE:AAA")), [FabricatedRun.Ran(FabricatedRun.IdA, "NSE:AAA", FabricatedRun.Monday, FabricatedRun.Monday)], [trade], fills: rules.ToCanonicalJson());

        var root = Root(Json(run, rules: rules));

        root.GetProperty("run").GetProperty("trades")[0].GetProperty("trade").GetProperty("fills").GetString().ShouldBe("no_slippage");
        root.GetProperty("labels").GetProperty("lines")[1].GetString().ShouldBe("fills: modelled, no slippage, no tick rounding of fills");
    }

    [Fact]
    public void A_run_with_no_trades_and_nothing_that_ran_is_written()
    {
        var run = FabricatedRun.Assemble(FabricatedRun.List((FabricatedRun.IdA, "NSE:AAA")), [FabricatedRun.Skipped(FabricatedRun.IdA, "NSE:AAA", SkipReason.NoBars)]);

        var root = Root(Json(run));

        root.GetProperty("run").GetProperty("trades").GetArrayLength().ShouldBe(0);
        root.GetProperty("labels").GetProperty("lines")[7].GetString().ShouldBe("data: none");
    }

    [Fact]
    public void A_basket_run_writes_the_basket_and_the_basket_id_of_every_trade()
    {
        var trade = FabricatedRun.Trade(FabricatedRun.IdA, "NSE:AAA", Side.Buy, FabricatedRun.Monday);
        var run = FabricatedRun.Assemble(FabricatedRun.BasketOf((FabricatedRun.IdA, "NSE:AAA")), [FabricatedRun.Ran(FabricatedRun.IdA, "NSE:AAA", FabricatedRun.Monday, FabricatedRun.Monday)], [trade]);

        var parsed = Root(Json(run)).GetProperty("run");

        parsed.GetProperty("universe").GetProperty("basket_id").GetGuid().ShouldBe(FabricatedRun.Basket);
        parsed.GetProperty("universe").GetProperty("basket_name").GetString().ShouldBe("Banks");
        parsed.GetProperty("trades")[0].GetProperty("basket_ids")[0].GetGuid().ShouldBe(FabricatedRun.Basket);
    }

    [Fact]
    public void A_symbol_with_an_ampersand_is_escaped_for_safety_and_parses_back_to_the_symbol()
    {
        var json = Json(FabricatedRun.Standard());

        json.ShouldContain("\"symbol\":\"NSE:M\\u0026M\"");
        json.ShouldNotContain("&");
        Root(json).GetProperty("run").GetProperty("universe").GetProperty("members")[1].GetProperty("symbol").GetString().ShouldBe("NSE:M&M");
    }

    [Fact]
    public void The_arguments_are_checked()
    {
        var run = FabricatedRun.Standard();
        var labels = RunLabels.Of(run, FillRules.Default);
        var sink = new System.Buffers.ArrayBufferWriter<byte>();

        Should.Throw<ArgumentNullException>(() => SimExportJson.Write(null!, run, labels, Format));
        Should.Throw<ArgumentNullException>(() => SimExportJson.Write(sink, null!, labels, Format));
        Should.Throw<ArgumentNullException>(() => SimExportJson.Write(sink, run, null!, Format));
        Should.Throw<ArgumentException>(() => SimExportJson.Write(sink, run, labels, " "));
    }
}

/// <summary>A type with a dictionary property, to prove the scan that guards the record sees one (it is never exported).</summary>
internal sealed record DictionarySentinel(IReadOnlyDictionary<string, long> Counts);
