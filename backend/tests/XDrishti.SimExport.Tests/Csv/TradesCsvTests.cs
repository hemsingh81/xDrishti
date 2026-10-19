using System.Globalization;
using System.Text;
using XDrishti.Domain.Research;
using XDrishti.SimExport.Csv;
using XDrishti.SimExport.Tests.Support;

namespace XDrishti.SimExport.Tests.Csv;

/// <summary>Criterion 2 of P3-04b1: <c>trades.csv</c> and <c>open_at_end.csv</c>.</summary>
public sealed class TradesCsvTests
{
    private static readonly string[] ExpectedColumns =
    [
        "run_key", "schema_version", "strategy_id", "instrument_id", "symbol", "side", "trade_date", "decision_epoch", "decision_ist", "trigger_timeframe", "trigger_bar_epoch", "trigger_bar_open", "trigger_bar_high",
        "trigger_bar_low", "trigger_bar_close", "trigger_bar_volume", "trigger_bar_minutes", "trigger_bar_complete", "entry_epoch", "entry_ist", "entry_price", "stop_price", "target_price", "exit_epoch", "exit_ist",
        "exit_price", "exit_reason", "r_exact", "mae_r", "mfe_r", "fills", "costs", "r_filled", "slippage_r", "entry_filled_price", "exit_filled_price", "entry_fill", "tick_fallback", "sessions_held", "sessions_unseen",
        "costing_segment", "costing_sheet", "costing_quantity", "costing_risk_taken", "costing_entry_turnover", "costing_exit_turnover", "costing_brokerage", "costing_stt", "costing_exchange_txn", "costing_sebi_fee",
        "costing_stamp_duty", "costing_other", "costing_dp_charge", "costing_gst", "costing_total_cost", "cost_r", "r_net", "basket_ids", "key",
    ];

    /// <summary>For each positional property of <see cref="SimTrade"/>, the columns it feeds.</summary>
    private static readonly Dictionary<string, string[]> Feeds = new()
    {
        ["SchemaVersion"] = ["schema_version"],
        ["StrategyId"] = ["strategy_id"],
        ["InstrumentId"] = ["instrument_id"],
        ["Symbol"] = ["symbol"],
        ["Side"] = ["side"],
        ["TradeDate"] = ["trade_date"],
        ["DecisionEpochSeconds"] = ["decision_epoch", "decision_ist"],
        ["TriggerTimeframe"] = ["trigger_timeframe"],
        ["TriggerBar"] = ["trigger_bar_epoch", "trigger_bar_open", "trigger_bar_high", "trigger_bar_low", "trigger_bar_close", "trigger_bar_volume"],
        ["TriggerBarMinutes"] = ["trigger_bar_minutes"],
        ["TriggerBarComplete"] = ["trigger_bar_complete"],
        ["EntryEpochSeconds"] = ["entry_epoch", "entry_ist"],
        ["EntryPrice"] = ["entry_price"],
        ["StopPrice"] = ["stop_price"],
        ["TargetPrice"] = ["target_price"],
        ["ExitEpochSeconds"] = ["exit_epoch", "exit_ist"],
        ["ExitPrice"] = ["exit_price"],
        ["ExitReason"] = ["exit_reason"],
        ["RExact"] = ["r_exact"],
        ["MaeR"] = ["mae_r"],
        ["MfeR"] = ["mfe_r"],
        ["Fills"] = ["fills"],
        ["Costs"] = ["costs"],
        ["RFilled"] = ["r_filled"],
        ["SlippageR"] = ["slippage_r"],
        ["EntryFilledPrice"] = ["entry_filled_price"],
        ["ExitFilledPrice"] = ["exit_filled_price"],
        ["EntryFill"] = ["entry_fill"],
        ["TickFallback"] = ["tick_fallback"],
        ["SessionsHeld"] = ["sessions_held"],
        ["SessionsUnseen"] = ["sessions_unseen"],
        ["Costing"] = ["costing_segment", "costing_sheet", "costing_quantity", "costing_risk_taken", "costing_entry_turnover", "costing_exit_turnover", "costing_brokerage", "costing_stt", "costing_exchange_txn", "costing_sebi_fee", "costing_stamp_duty", "costing_other", "costing_dp_charge", "costing_gst", "costing_total_cost", "cost_r", "r_net"],
    };

    private static string Csv(SimulationRun run, IReadOnlyList<RunTrade> trades) => Sinks.Render(s => TradesCsv.Write(s, run.RunKey, trades));

    private static string Csv(string runKey, params RunTrade[] trades) => Sinks.Render(s => TradesCsv.Write(s, runKey, trades));

    [Fact]
    public void The_trades_file_equals_the_golden_file_byte_for_byte_golden()
    {
        var run = FabricatedRun.Standard();

        Golden.Check("trades-csv", Csv(run, run.Trades));
    }

    [Fact]
    public void The_open_at_end_file_equals_the_golden_file_golden()
    {
        var run = FabricatedRun.Standard();

        Golden.Check("open-at-end-csv", Csv(run, run.OpenAtEnd));
    }

    [Fact]
    public void The_header_is_the_column_list_in_order()
    {
        TradesCsv.Columns.ShouldBe(ExpectedColumns);
        var text = Csv(FabricatedRun.Standard(), []);

        text.ShouldBe(string.Join(',', ExpectedColumns) + "\n");
    }

    [Fact]
    public void The_bytes_are_utf8_without_a_byte_order_mark_with_line_feeds_and_a_final_newline()
    {
        var run = FabricatedRun.Standard();
        var bytes = Sinks.Bytes(s => TradesCsv.Write(s, run.RunKey, run.Trades));

        bytes.Take(3).ShouldNotBe([0xEF, 0xBB, 0xBF]);
        bytes[^1].ShouldBe((byte)'\n');
        bytes.ShouldNotContain((byte)'\r');
        Encoding.UTF8.GetString(bytes).Split('\n').Length.ShouldBe(run.Trades.Count + 2, "header, the rows and the empty piece after the final newline");
    }

    [Fact]
    public void Every_row_has_as_many_cells_as_the_header_and_the_numbers_parse_back_to_the_same_double()
    {
        var run = FabricatedRun.Standard();
        var rows = Rfc4180Reader.Read(Csv(run, run.Trades));
        var header = rows[0];

        rows.Count.ShouldBe(3);
        rows.Skip(1).ShouldAllBe(r => r.Count == header.Count);
        for (var i = 0; i < run.Trades.Count; i++)
        {
            var trade = run.Trades[i].Trade;
            var row = rows[i + 1];
            double Cell(string column) => double.Parse(row[header.IndexOf(column)], NumberStyles.Float, CultureInfo.InvariantCulture);

            Cell("entry_price").ShouldBe(trade.EntryPrice);
            Cell("stop_price").ShouldBe(trade.StopPrice);
            Cell("exit_price").ShouldBe(trade.ExitPrice);
            Cell("r_exact").ShouldBe(trade.RExact);
            Cell("r_filled").ShouldBe(trade.RFilled);
            Cell("slippage_r").ShouldBe(trade.SlippageR);
            Cell("trigger_bar_close").ShouldBe(trade.TriggerBar.Close);
            long.Parse(row[header.IndexOf("entry_epoch")], CultureInfo.InvariantCulture).ShouldBe(trade.EntryEpochSeconds);
            row[header.IndexOf("key")].ShouldBe(trade.Key);
            row[header.IndexOf("schema_version")].ShouldBe("4");
        }
    }

    [Fact]
    public void The_run_key_is_the_full_64_hex_in_every_row()
    {
        var run = FabricatedRun.Standard();
        var rows = Rfc4180Reader.Read(Csv(run, run.Trades));

        rows.Skip(1).Select(r => r[0]).ShouldAllBe(k => k == run.RunKey && k.Length == 64);
    }

    [Fact]
    public void A_run_key_that_is_not_64_lower_case_hex_is_refused()
    {
        var run = FabricatedRun.Standard();

        Should.Throw<ArgumentException>(() => Csv("abc", run.Trades[0]));
        Should.Throw<ArgumentException>(() => Csv(new string('A', 64), run.Trades[0]));
        Should.Throw<ArgumentException>(() => Csv(null!, run.Trades[0]));
    }

    [Fact]
    public void The_times_are_india_time_whatever_the_culture_and_the_zone_of_the_machine()
    {
        // 2026-03-02 10:00 India time is the entry minute of the first trade; the decision is the minute before and the exit ten minutes after.
        var run = FabricatedRun.Standard();
        var saved = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        try
        {
            foreach (var culture in new[] { Culture(",", "."), Culture("٫", "٬"), CultureInfo.InvariantCulture })
            {
                CultureInfo.CurrentCulture = culture;
                CultureInfo.CurrentUICulture = culture;
                var rows = Rfc4180Reader.Read(Csv(run, run.Trades));
                var header = rows[0];

                rows[1][header.IndexOf("decision_ist")].ShouldBe("2026-03-02 09:59");
                rows[1][header.IndexOf("entry_ist")].ShouldBe("2026-03-02 10:00");
                rows[1][header.IndexOf("exit_ist")].ShouldBe("2026-03-02 10:10");
                rows[1][header.IndexOf("trade_date")].ShouldBe("2026-03-02");
            }
        }
        finally
        {
            (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = saved;
        }
    }

    [Fact]
    public void The_file_is_the_same_under_another_culture()
    {
        var run = FabricatedRun.Standard();
        var expected = Csv(run, run.Trades);
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = Culture(",", ".");

            Csv(run, run.Trades).ShouldBe(expected);
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void A_null_target_is_an_empty_cell()
    {
        var run = FabricatedRun.Standard();
        var rows = Rfc4180Reader.Read(Csv(run, run.OpenAtEnd));

        rows[1][rows[0].IndexOf("target_price")].ShouldBe(string.Empty);
        rows[1][rows[0].IndexOf("exit_reason")].ShouldBe("end_of_data");
    }

    [Fact]
    public void The_two_files_hold_exactly_the_trades_and_the_open_at_end_trades_of_the_record()
    {
        var run = FabricatedRun.Standard();
        var trades = Rfc4180Reader.Read(Csv(run, run.Trades));
        var open = Rfc4180Reader.Read(Csv(run, run.OpenAtEnd));

        trades.Skip(1).Select(r => r[^1]).ShouldBe(run.Trades.Select(t => t.Trade.Key));
        open.Skip(1).Select(r => r[^1]).ShouldBe(run.OpenAtEnd.Select(t => t.Trade.Key));
        run.OpenAtEnd.ShouldNotBeEmpty();
        trades.Skip(1).Select(r => r[^1]).Intersect(open.Skip(1).Select(r => r[^1])).ShouldBeEmpty("a position the data cut short is not in the trades file");
    }

    [Fact]
    public void A_run_with_no_trades_gives_a_header_and_no_rows()
    {
        var run = FabricatedRun.Assemble(FabricatedRun.List((FabricatedRun.IdA, "NSE:AAA")), [FabricatedRun.Skipped(FabricatedRun.IdA, "NSE:AAA", SkipReason.NoBars)]);

        Rfc4180Reader.Read(Csv(run, run.Trades)).Count.ShouldBe(1);
        Rfc4180Reader.Read(Csv(run, run.OpenAtEnd)).Count.ShouldBe(1);
    }

    [Fact]
    public void Every_positional_property_of_the_trade_but_the_reserved_snapshot_is_a_column()
    {
        var properties = typeof(SimTrade).GetConstructors().Single().GetParameters().Select(p => p.Name!).Where(n => n != nameof(SimTrade.EntrySnapshotJson)).ToList();

        properties.Except(Feeds.Keys).ShouldBeEmpty("a property of SimTrade that no column carries: revise the CSV on purpose and raise the exporter format");
        Feeds.Keys.Except(properties).ShouldBeEmpty();
        Feeds.Values.SelectMany(c => c).Concat(["run_key", "basket_ids", "key"]).Order().ShouldBe(TradesCsv.Columns.Order(), "every column is fed by a property, the run key, the baskets or the key");
        TradesCsv.Columns.ShouldNotContain("entry_snapshot_json");
    }

    [Fact]
    public void Negative_zero_is_written_zero()
    {
        var trade = FabricatedRun.Trade(FabricatedRun.IdA, "NSE:AAA", Side.Buy, FabricatedRun.Monday) with { MaeR = -0.0, SlippageR = -0.0 };
        var rows = Rfc4180Reader.Read(Csv(new string('a', 64), new RunTrade(trade, [])));

        rows[1][rows[0].IndexOf("mae_r")].ShouldBe("0");
        rows[1][rows[0].IndexOf("slippage_r")].ShouldBe("0");
    }

    [Fact]
    public void A_negative_number_is_not_prefixed_like_a_formula()
    {
        var trade = FabricatedRun.Trade(FabricatedRun.IdA, "NSE:AAA", Side.Sell, FabricatedRun.Monday, rExact: -1.5, rFilled: -1.55) with { SlippageR = -0.05, MaeR = -0.05 };
        var text = Csv(new string('a', 64), new RunTrade(trade, []));
        var rows = Rfc4180Reader.Read(text);

        rows[1][rows[0].IndexOf("r_exact")].ShouldBe("-1.5");
        rows[1][rows[0].IndexOf("slippage_r")].ShouldBe("-0.05");
        text.ShouldNotContain("'-");
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void A_price_that_is_not_a_number_makes_the_writer_throw(double bad)
    {
        var run = FabricatedRun.Standard();
        var trade = run.Trades[0].Trade;

        foreach (var broken in new[] { trade with { EntryPrice = bad }, trade with { TargetPrice = bad }, trade with { RExact = bad }, trade with { TriggerBar = trade.TriggerBar with { High = bad } } })
        {
            Should.Throw<ArgumentException>(() => Csv(run.RunKey, new RunTrade(broken, [])));
        }
    }

    [Fact]
    public void The_basket_ids_are_joined_by_semicolons()
    {
        var other = new Guid("99999999-8888-7777-6666-555555555555");
        var trade = FabricatedRun.Trade(FabricatedRun.IdA, "NSE:AAA", Side.Buy, FabricatedRun.Monday);
        var rows = Rfc4180Reader.Read(Csv(new string('a', 64), new RunTrade(trade, [FabricatedRun.Basket, other])));

        rows[1][rows[0].IndexOf("basket_ids")].ShouldBe(FabricatedRun.Basket.ToString("D") + ";" + other.ToString("D"));
    }

    [Fact]
    public void The_key_cell_is_the_key_of_the_trade_without_making_the_string()
    {
        var trade = FabricatedRun.Trade(FabricatedRun.IdA, "NSE:M&M", Side.Sell, new DateOnly(2025, 12, 31));
        var rows = Rfc4180Reader.Read(Csv(new string('a', 64), new RunTrade(trade, [])));

        rows[1][^1].ShouldBe(trade.Key);
        rows[1][^1].ShouldBe("ORB|NSE:M&M|Sell|2025-12-31");
    }

    [Fact]
    public void The_key_cell_of_a_trade_filled_on_the_day_after_its_decision_ends_with_the_decision_date()
    {
        var monday = FabricatedRun.Trade(FabricatedRun.IdA, "NSE:AAA", Side.Buy, new DateOnly(2026, 3, 2));
        var trade = monday with { TradeDate = new DateOnly(2026, 3, 3) };
        var rows = Rfc4180Reader.Read(Csv(new string('a', 64), new RunTrade(trade, [])));

        trade.Key.ShouldBe("ORB|NSE:AAA|Buy|2026-03-02", "the key follows the day of the decision (ADR 0043 D6)");
        rows[1][^1].ShouldBe(trade.Key);
        rows[1][rows[0].IndexOf("trade_date")].ShouldBe("2026-03-03");
    }

    private static CultureInfo Culture(string decimalSeparator, string groupSeparator)
    {
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.NumberFormat.NumberDecimalSeparator = decimalSeparator;
        culture.NumberFormat.NumberGroupSeparator = groupSeparator;
        culture.NumberFormat.NegativeSign = "−";
        culture.DateTimeFormat.TimeSeparator = ".";
        return culture;
    }
}
