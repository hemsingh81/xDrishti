using System.Text;
using XDrishti.Application.Portfolio.Trades;
using XDrishti.Domain.Portfolio;
using static XDrishti.Application.Tests.Portfolio.Trades.TradeFiles;

namespace XDrishti.Application.Tests.Portfolio.Trades;

/// <summary>The tradebook reader (ADR 0023, P2-03 criteria 1 to 4): header aliases, formats, skips, bad rows, limits. Today is 2026-10-13.</summary>
public sealed class TradeCsvReaderTests
{
    [Fact]
    public async Task A_zerodha_style_file_gives_its_equity_trades_and_counts_what_it_leaves_out()
    {
        var result = await ReadSampleAsync("zerodha-style.csv");

        var file = result.Value;
        file.Rows.ShouldBe(9);
        file.Trades.Select(t => (t.Symbol, t.Side, t.Qty, t.Price, t.TradeId)).ShouldBe(
        [
            ("EXMPL", TradeSide.Buy, 10L, 1250.50m, "T0001"),
            ("EXMPL", TradeSide.Buy, 5L, 1300.00m, "T0002"),
            ("EXMPL", TradeSide.Sell, 8L, 1410.25m, "T0003"),
            ("ACME", TradeSide.Buy, 100L, 95.40m, "T0004"),
        ]);
        file.DuplicatesInFile.ShouldBe(1);
        file.Skipped.ShouldBe(3);
        file.SkippedByReason.ShouldBe(new Dictionary<string, int> { ["bse"] = 1, ["derivatives"] = 2 });
        file.BadTotal.ShouldBe(1);
        file.Bad.ShouldHaveSingleItem().Line.ShouldBe(10);
        file.Bad[0].Reason.ShouldContain("whole number");
        file.Parsed.ShouldBe(5);
    }

    [Fact]
    public async Task The_execution_timestamp_gives_the_time_and_the_trade_date_the_day()
    {
        var file = (await ReadSampleAsync("zerodha-style.csv")).Value;

        var first = file.Trades[0];

        (first.Date, first.Time, first.Exchange, first.Isin).ShouldBe((new DateOnly(2024, 3, 5), new TimeOnly(9, 21, 5), "NSE", "INE000A01011"));
    }

    [Fact]
    public async Task Other_aliases_formats_thousands_and_a_byte_order_mark_are_understood_and_an_impossible_date_is_a_bad_row()
    {
        var file = (await ReadSampleAsync("tradebook-formats.csv")).Value;

        file.Trades.Select(t => (t.Date, t.Symbol, t.Side, t.Qty, t.Price, t.Charges)).ShouldBe(
        [
            (new DateOnly(2024, 3, 5), "EXMPL", TradeSide.Buy, 1000L, 1250.50m, 35.5m),
            (new DateOnly(2024, 3, 6), "ACME", TradeSide.Sell, 1234567L, 95.4m, 0m),
            (new DateOnly(2024, 3, 7), "ACME", TradeSide.Buy, 10L, 96m, 3m),
        ]);
        file.Rows.ShouldBe(4);
        file.BadTotal.ShouldBe(1);
        file.Bad[0].Reason.ShouldContain("13/25/2024");
    }

    [Fact]
    public async Task A_total_charges_column_is_used_as_it_is_and_the_components_are_not_added_to_it()
    {
        var file = (await ReadSampleAsync("with-total-charges.csv")).Value;

        file.Trades.Select(t => (t.Date, t.Charges, t.TradeId)).ShouldBe([(new DateOnly(2024, 4, 10), 7.50m, "N-1"), (new DateOnly(2024, 4, 11), 0m, "N-2")]);
    }

    [Fact]
    public async Task Without_a_total_column_the_charge_components_are_summed()
    {
        var trade = await OneAsync("2024-03-05,EXMPL,,buy,1,100,10,2.5,1", "date,symbol,isin,side,quantity,price,brokerage,stt,stamp duty");

        trade.Charges.ShouldBe(13.5m);
    }

    [Theory]
    [InlineData("Trade Date,Trading Symbol,Trade Type,Traded Quantity,Trade Price")]
    [InlineData("TRADE_DATE,TSYM,BUY/SELL,QTY,RATE")]
    [InlineData("date,scrip,side,quantity,avg price")]
    [InlineData("Transaction Date,Stock Name,Transaction Type,Shares,Average Price")]
    public async Task Header_names_are_matched_without_regard_to_case_or_punctuation(string header)
    {
        var trade = await OneAsync("2024-03-05,exmpl,Buy,12,99.5", header);

        (trade.Symbol, trade.Side, trade.Qty, trade.Price).ShouldBe(("EXMPL", TradeSide.Buy, 12L, 99.5m));
    }

    [Fact]
    public async Task A_file_with_only_an_isin_is_enough()
    {
        var trade = await OneAsync("2024-03-05,INE000A01011,buy,1,10", "date,isin,side,quantity,price");

        (trade.Symbol, trade.Isin).ShouldBe((string.Empty, "INE000A01011"));
    }

    [Theory]
    [InlineData("symbol,side,quantity,price", "date")]
    [InlineData("date,side,quantity,price", "symbol or ISIN")]
    [InlineData("date,symbol,side,price", "quantity")]
    [InlineData("date,symbol,side,quantity", "price")]
    public async Task A_missing_required_column_refuses_the_file_and_names_it(string header, string missing)
    {
        var result = await ReadRowsAsync(header, "1,2,3,4");

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("portfolio.invalid");
        result.Error.Fields!.Single().Field.ShouldBe("file");
        result.Error.Fields!.Single().Message.ShouldContain(missing);
        result.Error.Fields!.Single().Message.ShouldContain("Accepted header names");
    }

    [Fact]
    public async Task A_file_with_a_date_symbol_quantity_and_price_but_no_side_is_explained_as_a_tradebook_without_sides_not_as_a_missing_column()
    {
        var result = await ReadRowsAsync("date,symbol,quantity,price", "2024-03-05,EXMPL,10,90");

        result.Error.Code.ShouldBe("portfolio.wrong_file_kind");
        result.Error.Payload!["detected"].ShouldBe("tradebook_without_sides");
    }

    [Fact]
    public async Task An_empty_file_is_refused()
    {
        var result = await ReadAsync("\n\n");

        result.IsFailure.ShouldBeTrue();
        result.Error.Fields!.Single().Message.ShouldContain("empty");
    }

    [Fact]
    public async Task A_file_that_is_not_utf8_is_refused()
    {
        await using var stream = new MemoryStream([.. Encoding.UTF8.GetBytes("date,symbol,side,quantity,price\n2024-03-05,EX"), 0xE9, 0xFF, .. Encoding.UTF8.GetBytes("MPL,buy,1,2\n")]);

        var result = await TradeCsvReader.ReadAsync(stream, Today, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Fields!.Single().Message.ShouldContain("UTF-8");
    }

    [Fact]
    public async Task More_than_twenty_thousand_data_rows_are_refused()
    {
        var text = new StringBuilder("date,symbol,side,quantity,price\n");
        for (var i = 0; i <= TradeCsvLimits.MaxRows; i++)
        {
            text.Append("2024-03-05,EXMPL,buy,1,10\n");
        }

        var result = await ReadAsync(text.ToString());

        result.IsFailure.ShouldBeTrue();
        result.Error.Fields!.Single().Message.ShouldContain("20,000");
    }

    [Fact]
    public async Task Exactly_twenty_thousand_rows_are_read()
    {
        var text = new StringBuilder("date,symbol,side,quantity,price\n");
        for (var i = 0; i < TradeCsvLimits.MaxRows; i++)
        {
            text.Append("2024-03-05,EXMPL,buy,1,10\n");
        }

        var result = await ReadAsync(text.ToString());

        result.Value.Trades.Count.ShouldBe(TradeCsvLimits.MaxRows);
    }

    [Fact]
    public async Task A_file_over_ten_megabytes_is_refused()
    {
        await using var stream = new MemoryStream(new byte[TradeCsvLimits.MaxBytes + 1]);

        var result = await TradeCsvReader.ReadAsync(stream, Today, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Fields!.Single().Message.ShouldContain("10 MB");
    }

    [Fact]
    public async Task At_most_two_hundred_bad_rows_are_listed_and_all_are_counted()
    {
        var rows = Enumerable.Range(0, 250).Select(i => $"2024-03-05,EXMPL,,buy,x{i},10").ToArray();

        var file = await OkAsync(Header, rows);

        (file.Bad.Count, file.BadTotal, file.Trades.Count).ShouldBe((TradeCsvLimits.MaxListedBad, 250, 0));
        file.Bad[0].Line.ShouldBe(2);
        file.Bad[^1].Line.ShouldBe(201);
    }

    [Fact]
    public async Task Cancelling_stops_the_read()
    {
        using var cancel = new CancellationTokenSource();
        await cancel.CancelAsync();
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("date,symbol,side,quantity,price\n2024-03-05,EXMPL,buy,1,10\n"));

        await Should.ThrowAsync<OperationCanceledException>(async () => await TradeCsvReader.ReadAsync(stream, Today, cancel.Token));
    }

    [Fact]
    public async Task Empty_rows_are_ignored_and_not_counted()
    {
        var file = await OkAsync(Header, "", ",,,,,", "2024-03-05,EXMPL,,buy,1,10", "   ");

        (file.Rows, file.Trades.Count).ShouldBe((1, 1));
    }

    [Fact]
    public async Task A_quoted_field_may_contain_a_comma_a_quote_and_a_line_break_and_line_numbers_stay_physical()
    {
        var text = "date,symbol,side,quantity,price,note\n2024-03-05,EXMPL,buy,1,10,\"a, \"\"b\"\"\nc\"\n2024-03-06,EXMPL,buy,x,10,\n";

        var file = (await ReadAsync(text)).Value;

        file.Trades.ShouldHaveSingleItem().Line.ShouldBe(2);
        file.Bad.ShouldHaveSingleItem().Line.ShouldBe(4);
    }

    [Fact]
    public async Task Unknown_columns_are_ignored()
    {
        var trade = await OneAsync("2024-03-05,EXMPL,buy,1,10,whatever,42", "date,symbol,side,quantity,price,client name,auction");

        trade.Symbol.ShouldBe("EXMPL");
    }

    [Theory]
    [InlineData("2024-03-05,NSE:INFY,buy,1,10", "INFY")]
    [InlineData("2024-03-05,infy-eq,buy,1,10", "INFY")]
    [InlineData("2024-03-05,  nse:tcs-EQ ,buy,1,10", "TCS")]
    public async Task The_symbol_is_upper_cased_trimmed_and_loses_its_exchange_prefix_and_series_suffix(string row, string expected)
    {
        var trade = await OneAsync(row, "date,symbol,side,quantity,price");

        trade.Symbol.ShouldBe(expected);
    }

    [Theory]
    [InlineData("buy")]
    [InlineData("BUY")]
    [InlineData("b")]
    [InlineData("Purchase")]
    public async Task Buy_side_aliases(string side)
    {
        var trade = await OneAsync($"2024-03-05,EXMPL,{side},1,10", "date,symbol,side,quantity,price");

        trade.Side.ShouldBe(TradeSide.Buy);
    }

    [Theory]
    [InlineData("sell")]
    [InlineData("S")]
    [InlineData("Sale")]
    public async Task Sell_side_aliases(string side)
    {
        var trade = await OneAsync($"2024-03-05,EXMPL,{side},1,10", "date,symbol,side,quantity,price");

        trade.Side.ShouldBe(TradeSide.Sell);
    }

    [Theory]
    [InlineData("2024-03-05,EXMPL,hold,1,10", "Side")]
    [InlineData("2024-03-05,EXMPL,buy,0,10", "Quantity")]
    [InlineData("2024-03-05,EXMPL,buy,-3,10", "Quantity")]
    [InlineData("2024-03-05,EXMPL,buy,1.5,10", "whole number")]
    [InlineData("2024-03-05,EXMPL,buy,1,0", "Price")]
    [InlineData("2024-03-05,EXMPL,buy,1,-1", "Price")]
    [InlineData("2024-03-05,EXMPL,buy,1,abc", "Price")]
    [InlineData("2026-10-14,EXMPL,buy,1,10", "future")]
    [InlineData("1989-12-31,EXMPL,buy,1,10", "Cannot be before")]
    [InlineData("03/25/2024,EXMPL,buy,1,10", "day first")]
    [InlineData("2024-02-30,EXMPL,buy,1,10", "not a date")]
    [InlineData("05-03-24,EXMPL,buy,1,10", "not a date")]
    [InlineData(",EXMPL,buy,1,10", "not a date")]
    [InlineData("2024-03-05,,buy,1,10", "symbol and the ISIN")]
    [InlineData("2024-03-05T10:00:00Z,EXMPL,buy,1,10", "time zone")]
    [InlineData("2024-03-05 25:00,EXMPL,buy,1,10", "not a time")]
    public async Task A_bad_row_is_reported_with_its_reason_and_never_imported(string row, string reason)
    {
        var message = await BadReasonAsync(row, "date,symbol,side,quantity,price");

        message.ShouldContain(reason);
    }

    [Fact]
    public async Task A_bad_isin_and_a_negative_charge_are_bad_rows()
    {
        (await BadReasonAsync("2024-03-05,EXMPL,XYZ,buy,1,10,0", "date,symbol,isin,side,quantity,price,charges")).ShouldContain("ISIN");
        (await BadReasonAsync("2024-03-05,EXMPL,,buy,1,10,-1", "date,symbol,isin,side,quantity,price,charges")).ShouldContain("negative");
    }

    [Theory]
    [InlineData("05-03-2024")]
    [InlineData("5/3/2024")]
    [InlineData("05/03/2024")]
    [InlineData("05-Mar-2024")]
    [InlineData("05-MAR-2024")]
    [InlineData("2024-03-05")]
    [InlineData("2024-3-5")]
    public async Task Every_documented_date_format_gives_the_fifth_of_march(string date)
    {
        var trade = await OneAsync($"{date},EXMPL,buy,1,10", "date,symbol,side,quantity,price");

        trade.Date.ShouldBe(new DateOnly(2024, 3, 5));
    }

    [Fact]
    public async Task The_day_is_first_and_a_date_like_04_05_is_the_fourth_of_may_never_april_the_fifth()
    {
        var trade = await OneAsync("04/05/2024,EXMPL,buy,1,10", "date,symbol,side,quantity,price");

        trade.Date.ShouldBe(new DateOnly(2024, 5, 4));
    }

    [Theory]
    [InlineData("1,234.50", 1234.50)]
    [InlineData("12,34,567.25", 1234567.25)]
    [InlineData("0.5", 0.5)]
    [InlineData("100", 100)]
    [InlineData("99.123456", 99.1235)]
    public async Task Prices_take_a_point_for_decimals_commas_for_thousands_and_are_stored_to_four_decimals(string price, double expected)
    {
        var trade = await OneAsync($"2024-03-05,EXMPL,buy,1,\"{price}\"", "date,symbol,side,quantity,price");

        trade.Price.ShouldBe((decimal)expected);
    }

    [Theory]
    [InlineData("1,5")]
    [InlineData("1,23")]
    [InlineData("12,3456")]
    [InlineData("1e3")]
    [InlineData("₹100")]
    public async Task A_comma_that_does_not_group_thousands_an_exponent_or_a_currency_sign_is_not_a_number(string price)
    {
        var message = await BadReasonAsync($"2024-03-05,EXMPL,buy,1,\"{price}\"", "date,symbol,side,quantity,price");

        message.ShouldContain("not a number");
    }

    [Fact]
    public async Task Times_are_read_from_their_own_column_and_are_india_time()
    {
        var trade = await OneAsync("2024-03-05,09:15:30,EXMPL,buy,1,10", "date,time,symbol,side,quantity,price");

        trade.Time.ShouldBe(new TimeOnly(9, 15, 30));
    }

    [Theory]
    [InlineData("EXMPL", "NSE", "EQ", null)]
    [InlineData("EXMPL", "BSE", "EQ", "bse")]
    [InlineData("EXMPL", "NSE", "FO", "derivatives")]
    [InlineData("EXMPL", "NFO", "EQ", "derivatives")]
    [InlineData("EXMPL", "NSE", "OPTSTK", "derivatives")]
    [InlineData("GOLDM", "MCX", "COMMODITY", "commodity")]
    [InlineData("USDINR", "CDS", "CURRENCY", "currency")]
    [InlineData("EXMPL", "NSE", "Equity", null)]
    [InlineData("EXMPL", "NSE", "CM", null)]
    [InlineData("EXMPL", "NSE", "SOMETHING", "non_equity")]
    [InlineData("BSE:EXMPL", "", "", "bse")]
    [InlineData("NIFTY24MARFUT", "NSE", "EQ", "derivatives")]
    [InlineData("RELIANCE24MAR2500CE", "", "", "derivatives")]
    [InlineData("NIFTY2431422000PE", "", "", "derivatives")]
    [InlineData("NIFTYBEES", "NSE", "EQ", null)]
    [InlineData("TATAPOWER", "NSE", "EQ", null)]
    public async Task Rows_that_are_not_nse_equity_are_skipped_with_a_reason(string symbol, string exchange, string segment, string? skipped)
    {
        var file = await OkAsync("date,symbol,exchange,segment,side,quantity,price", $"2024-03-05,{symbol},{exchange},{segment},buy,1,10");

        if (skipped is null)
        {
            file.Trades.Count.ShouldBe(1);
            file.Skipped.ShouldBe(0);
        }
        else
        {
            file.Trades.ShouldBeEmpty();
            file.SkippedByReason.ShouldBe(new Dictionary<string, int> { [skipped] = 1 });
        }
    }

    [Fact]
    public async Task A_skipped_row_is_not_judged_by_its_other_fields()
    {
        var file = await OkAsync("date,symbol,exchange,side,quantity,price", "garbage,NIFTY24MARFUT,NFO,buy,x,y");

        (file.Skipped, file.BadTotal).ShouldBe((1, 0));
    }

    [Fact]
    public async Task The_same_trade_id_twice_with_identical_details_is_a_repeat_with_different_details_a_bad_row()
    {
        var file = await OkAsync(
            "date,symbol,side,quantity,price,trade id",
            "2024-03-05,EXMPL,buy,1,10,A1",
            "2024-03-05,EXMPL,buy,1,10,A1",
            "2024-03-05,EXMPL,buy,2,10,A1",
            "2024-03-05,EXMPL,buy,2,10,A2");

        (file.Trades.Count, file.DuplicatesInFile, file.BadTotal).ShouldBe((2, 1, 1));
        file.Bad[0].Line.ShouldBe(4);
        file.Bad[0].Reason.ShouldContain("A1");
    }

    [Fact]
    public async Task The_same_trade_id_on_two_days_is_two_trades_because_exchanges_number_trades_per_day()
    {
        var file = await OkAsync(
            "date,symbol,side,quantity,price,trade id",
            "2024-03-05,EXMPL,buy,1,10,1001",
            "2024-03-06,EXMPL,buy,1,10,1001",
            "2024-03-06,EXMPL,buy,1,10,1001");

        (file.Trades.Count, file.DuplicatesInFile, file.BadTotal).ShouldBe((2, 1, 0));
        TradeIdentity.Identify(Guid.NewGuid(), file.Trades).Select(t => t.ExternalId).ShouldBe(["t:20240305:1001", "t:20240306:1001"]);
    }

    [Fact]
    public async Task A_cell_of_several_megabytes_makes_a_bad_row_not_a_failure()
    {
        var huge = new string('9', 2 * 1024 * 1024);

        var file = await OkAsync(Header, $"2024-03-05,EXMPL,,buy,1,{huge}", $"2024-03-05,{huge},,buy,1,10", $"{huge},EXMPL,,buy,1,10", "2024-03-05,EXMPL,,buy,1,10");

        (file.Trades.Count, file.BadTotal).ShouldBe((1, 3));
        file.Bad.Select(b => b.Line).ShouldBe([2, 3, 4]);
        file.Bad.ShouldAllBe(b => b.Reason.Length < 200);
    }

    [Fact]
    public async Task Identical_lines_without_a_trade_id_are_all_kept()
    {
        var file = await OkAsync(Header, "2024-03-05,EXMPL,,buy,1,10", "2024-03-05,EXMPL,,buy,1,10");

        (file.Trades.Count, file.DuplicatesInFile).ShouldBe((2, 0));
    }

    [Fact]
    public async Task The_same_trade_written_with_other_decimals_has_the_same_identity()
    {
        var one = await OneAsync("2024-03-05,EXMPL,,buy,10,90", Header);
        var other = await OneAsync("2024-03-05,EXMPL,,buy,10.0,\"90.0000\"", Header);

        TradeIdentity.Identify(Guid.Empty, [one]).Single().ExternalId.ShouldBe(TradeIdentity.Identify(Guid.Empty, [other]).Single().ExternalId);
    }

    [Fact]
    public async Task The_identity_of_a_line_depends_on_the_account_and_on_how_many_identical_lines_came_before()
    {
        var file = await OkAsync(Header, "2024-03-05,EXMPL,,buy,1,10", "2024-03-05,EXMPL,,buy,1,10", "2024-03-05,EXMPL,,buy,1,11");
        var account = Guid.NewGuid();

        var ids = TradeIdentity.Identify(account, file.Trades).Select(t => t.ExternalId).ToList();
        var again = TradeIdentity.Identify(account, file.Trades).Select(t => t.ExternalId).ToList();
        var other = TradeIdentity.Identify(Guid.NewGuid(), file.Trades).Select(t => t.ExternalId).ToList();

        ids.Distinct().Count().ShouldBe(3);
        again.ShouldBe(ids);
        other.Intersect(ids).ShouldBeEmpty();
        ids.ShouldAllBe(i => i.StartsWith("h:", StringComparison.Ordinal) && i.Length == 34);
    }

    [Fact]
    public async Task A_trade_id_is_the_identity_whatever_else_the_line_says()
    {
        var file = await OkAsync("date,symbol,side,quantity,price,trade id", "2024-03-05,EXMPL,buy,1,10,AB-1");

        TradeIdentity.Identify(Guid.NewGuid(), file.Trades).Single().ExternalId.ShouldBe("t:20240305:AB-1");
    }

    [Fact]
    public async Task The_template_is_read_by_the_reader_without_a_problem()
    {
        var file = (await ReadAsync(TradeTemplate.Csv)).Value;

        (file.Trades.Count, file.BadTotal, file.Skipped).ShouldBe((2, 0, 0));
        file.Trades.Select(t => t.Side).ShouldBe([TradeSide.Buy, TradeSide.Sell]);
    }
}
