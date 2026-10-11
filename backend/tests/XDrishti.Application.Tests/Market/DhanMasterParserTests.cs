using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Market.Import;
using XDrishti.Domain.Market;

namespace XDrishti.Application.Tests.Market;

public sealed class DhanMasterParserTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly string Header = MasterFixtures.Text(MasterFixtures.Master).Split('\n')[0];

    private static async Task<XDrishti.Domain.Common.Result<MasterParse>> ParseAsync(string text, Func<long, ValueTask>? progress = null)
    {
        await using var stream = MasterFixtures.AsStream(text);
        return await DhanMasterParser.ParseAsync(stream, progress, Ct);
    }

    private static string Row(string exchange = "NSE", string segment = "E", string id = "1", string isin = "INE000A01010", string instrument = "EQUITY",
        string underlying = "ABC", string name = "ABC LTD", string display = "Abc", string type = "ES", string series = "EQ", string lot = "1.0",
        string expiry = "", string tick = "5.0000", string flag = "N", string category = "NA") =>
        $"{exchange},{segment},{id},{isin},{instrument},,{underlying},{name},{display},{type},{series},{lot},{expiry},,,{tick},NA,N,N,{flag},{category},A,0,0,0,0,0,0,3.3,100.0,50.0,1000,";

    private static IEnumerable<string> Many(int count) =>
        Enumerable.Range(0, count).Select(i => Row(id: (1000 + i).ToString(System.Globalization.CultureInfo.InvariantCulture), underlying: "SYM" + i, isin: "INE" + (100000 + i).ToString(System.Globalization.CultureInfo.InvariantCulture) + "A1"));

    [Fact]
    public async Task The_fixture_counts_by_type()
    {
        var parse = (await ParseAsync(MasterFixtures.Text(MasterFixtures.Master))).Value;

        var counts = parse.Rows.GroupBy(r => r.Type).ToDictionary(g => g.Key, g => g.Count());
        counts[InstrumentType.Share].ShouldBe(30);
        counts[InstrumentType.Etf].ShouldBe(3);
        counts[InstrumentType.Index].ShouldBe(4);
        counts[InstrumentType.Future].ShouldBe(24);
        parse.OutOfScope.ShouldBe(75 - 61);
    }

    [Fact]
    public async Task Debt_bonds_BSE_options_currency_and_exchange_test_symbols_are_skipped()
    {
        var parse = (await ParseAsync(MasterFixtures.Text(MasterFixtures.Master))).Value;
        var names = parse.Rows.Select(r => r.SymbolName).ToHashSet();

        names.ShouldNotContain("SDL MH 6.56% 2032"); // debt
        names.ShouldNotContain("2.50%GOLDBONDS2030SR-I"); // gold bond
        names.ShouldNotContain("RELIANCE-Oct2026-720-CE"); // option
        names.ShouldNotContain("EURINR-Mar2026-104.75-CE"); // currency option
        names.ShouldNotContain("M TEK COPPER LIMITED"); // BSE equity
        names.ShouldNotContain(n => n.Contains("NSETEST", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Every_share_series_is_kept_and_a_flag_of_N_means_not_flagged()
    {
        var parse = (await ParseAsync(MasterFixtures.Text(MasterFixtures.Master))).Value;

        parse.Rows.Where(r => r.Type == InstrumentType.Share).Select(r => r.Series).Distinct().Order()
            .ShouldBe(["BE", "BZ", "EQ", "SM", "ST"]);
        parse.Rows.ShouldAllBe(r => r.AsmGsmFlag == null || r.AsmGsmFlag != "N");
        parse.Rows.Single(r => r.UnderlyingSymbol == "RELIANCE" && r.Type == InstrumentType.Share).Isin.ShouldBe("INE002A01018");
    }

    [Fact]
    public async Task A_row_reads_all_its_fields()
    {
        var parse = (await ParseAsync(MasterFixtures.Text(MasterFixtures.Master))).Value;

        var reliance = parse.Rows.Single(r => r is { UnderlyingSymbol: "RELIANCE", Type: InstrumentType.Share });

        reliance.SecurityId.ShouldBe("2885");
        reliance.SymbolName.ShouldBe("RELIANCE INDUSTRIES LTD");
        reliance.DisplayName.ShouldBe("Reliance Industries");
        reliance.Series.ShouldBe("EQ");
        reliance.LotSize.ShouldBe(1);
        reliance.TickSize.ShouldBe(10m);
        reliance.Expiry.ShouldBeNull();
        reliance.MtfLeverage.ShouldBe(4.545455m);
        reliance.FreezeQuantity.ShouldBe(81195);
        reliance.Isin.ShouldBe("INE002A01018");
    }

    [Fact]
    public async Task Index_rows_have_no_ISIN_series_or_expiry_and_futures_carry_their_expiry_and_lot()
    {
        var parse = (await ParseAsync(MasterFixtures.Text(MasterFixtures.Master))).Value;

        var nifty = parse.Rows.Single(r => r is { Type: InstrumentType.Index, SymbolName: "NIFTY" });
        nifty.Isin.ShouldBeNull();
        nifty.Series.ShouldBeNull();
        nifty.Expiry.ShouldBeNull();

        var future = parse.Rows.Single(r => r.SymbolName == "360ONE-Oct2026-FUT");
        future.Expiry.ShouldBe(new DateOnly(2026, 10, 27));
        future.LotSize.ShouldBe(500);
        future.Instrument.ShouldBe("FUTSTK");
    }

    [Fact]
    public async Task Progress_is_reported_while_reading()
    {
        var text = Header + "\n" + string.Join("\n", Enumerable.Range(0, DhanMasterParser.ProgressEvery * 2 + 5).Select(i => Row(exchange: "BSE", id: i.ToString(System.Globalization.CultureInfo.InvariantCulture)))) + "\n" + Row() + "\n";
        var reported = new List<long>();

        var result = await ParseAsync(text, rows =>
        {
            reported.Add(rows);
            return ValueTask.CompletedTask;
        });

        result.IsSuccess.ShouldBeTrue();
        reported.ShouldBe([DhanMasterParser.ProgressEvery, DhanMasterParser.ProgressEvery * 2, DhanMasterParser.ProgressEvery * 2 + 6]);
    }

    [Fact]
    public async Task A_missing_column_the_importer_reads_fails_closed_and_names_it()
    {
        var text = MasterFixtures.Text(MasterFixtures.Master).Replace("TICK_SIZE", "TICK_SZ", StringComparison.Ordinal);

        var result = await ParseAsync(text);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(DhanMasterParser.FailureCode);
        result.Error.Message.ShouldContain("missing TICK_SIZE");
        result.Error.Message.ShouldContain("Nothing was imported");
    }

    [Fact]
    public async Task A_repeated_required_column_fails_closed()
    {
        var result = await ParseAsync(Header.Replace("SERIES,", "SERIES,SERIES,", StringComparison.Ordinal) + "\n");

        result.IsFailure.ShouldBeTrue();
        result.Error.Message.ShouldContain("repeated SERIES");
    }

    [Fact]
    public async Task An_extra_column_only_warns_and_the_columns_are_found_by_name()
    {
        // The extra column sits first and EXCH_ID is no longer column 0.
        var text = "NEW_COLUMN," + Header + "\n" + string.Join("\n", Many(3).Select(r => "x," + r)) + "\n";

        var result = await ParseAsync(text);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Rows.Count.ShouldBe(3);
        result.Value.Warnings.ShouldHaveSingleItem().ShouldContain("NEW_COLUMN");
    }

    [Fact]
    public async Task A_vanished_column_that_is_not_used_only_warns()
    {
        var text = MasterFixtures.Text(MasterFixtures.Master).Replace("STRIKE_PRICE", "XSTRIKE", StringComparison.Ordinal);
        // Rows keep their 33 fields; the header names are what is checked.

        var result = await ParseAsync(text);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Warnings.ShouldContain(w => w.Contains("STRIKE_PRICE", StringComparison.Ordinal));
        result.Value.Warnings.ShouldContain(w => w.Contains("XSTRIKE", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EXCH_ID_need_not_be_the_first_column()
    {
        var reordered = Header.Split(',').ToList();
        reordered.Remove("EXCH_ID");
        reordered.Insert(reordered.Count - 1, "EXCH_ID"); // before the trailing empty column
        var rows = Many(3).Select(r =>
        {
            var f = r.Split(',').ToList();
            var exch = f[0];
            f.RemoveAt(0);
            f.Insert(f.Count - 1, exch);
            return string.Join(",", f);
        });

        var result = await ParseAsync(string.Join(",", reordered) + "\n" + string.Join("\n", rows) + "\n");

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : null);
        result.Value.Rows.Count.ShouldBe(3);
    }

    [Fact]
    public async Task An_empty_file_and_a_file_without_in_scope_rows_fail()
    {
        (await ParseAsync(string.Empty)).IsFailure.ShouldBeTrue();
        (await ParseAsync(Header + "\n" + Row(exchange: "BSE") + "\n")).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task A_few_malformed_NSE_rows_are_skipped_with_a_warning_and_protected_but_other_exchanges_are_ignored()
    {
        var text = Header + "\n" + string.Join("\n", Many(150)) + "\nNSE,E,777,short\nBSE,E,778,short\n";

        var result = await ParseAsync(text);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Rows.Count.ShouldBe(150);
        result.Value.Warnings.ShouldHaveSingleItem().ShouldContain("777");
        result.Value.Protected.ShouldHaveSingleItem().ShouldBe(new BrokerIdentity("NSE_EQ", "777"));
        result.Value.OutOfScope.ShouldBe(1);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("abc")]
    [InlineData("1.5")]
    [InlineData("")]
    public async Task An_in_scope_row_with_an_invalid_lot_size_is_skipped_and_its_instrument_protected(string lot)
    {
        var text = Header + "\n" + string.Join("\n", Many(150)) + "\n" + Row(id: "555", underlying: "BADLOT", lot: lot) + "\n";

        var result = await ParseAsync(text);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Rows.Count.ShouldBe(150);
        result.Value.Warnings.ShouldHaveSingleItem().ShouldBe("Skipped Dhan id 555: lot size is not a whole number from 1.");
        result.Value.Protected.ShouldHaveSingleItem().ShouldBe(new BrokerIdentity("NSE_EQ", "555"));
    }

    [Fact]
    public async Task A_future_with_an_invalid_expiry_is_skipped_not_fatal()
    {
        var future = Row(id: "556", segment: "D", instrument: "FUTSTK", type: "FUT", series: "NA", isin: "NA", expiry: "not-a-date");

        var result = await ParseAsync(Header + "\n" + string.Join("\n", Many(150)) + "\n" + future + "\n");

        result.IsSuccess.ShouldBeTrue();
        result.Value.Warnings.ShouldHaveSingleItem().ShouldContain("expiry");
        result.Value.Protected.ShouldHaveSingleItem().ShouldBe(new BrokerIdentity("NSE_FNO", "556"));
    }

    [Theory]
    [InlineData("series", "EQUITYXYZ")]
    [InlineData("isin", "INE0000000000000X")]
    [InlineData("category", "CATEGORYTOOLONGXX")]
    [InlineData("underlying", "U8888888888888888888888888888888888888888888888888888888888888888888888888888888888")]
    [InlineData("name", "N")]
    [InlineData("id", "123456789012345678901234567890123")]
    public async Task A_row_that_does_not_fit_the_stored_limits_is_skipped_with_a_warning_so_saving_cannot_fail(string field, string value)
    {
        var bad = field switch
        {
            "series" => Row(id: "600", underlying: "LIM", series: value),
            "isin" => Row(id: "600", underlying: "LIM", isin: value),
            "category" => Row(id: "600", underlying: "LIM", flag: "Y", category: value),
            "underlying" => Row(id: "600", underlying: value),
            "name" => Row(id: "600", underlying: "LIM", name: new string('N', 201)),
            _ => Row(id: value, underlying: "LIM"),
        };

        var result = await ParseAsync(Header + "\n" + string.Join("\n", Many(150)) + "\n" + bad + "\n");

        result.IsSuccess.ShouldBeTrue();
        result.Value.Rows.Count.ShouldBe(150);
        result.Value.Warnings.ShouldHaveSingleItem().ShouldContain("longer than");
    }

    [Fact]
    public async Task Values_at_the_limits_are_accepted()
    {
        var row = Row(id: new string('9', 32), underlying: new string('U', 80), name: new string('N', 200), series: "ABCDEFGH", isin: "INE000000000000A");

        var result = await ParseAsync(Header + "\n" + row + "\n");

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Row_warnings_list_the_first_ten_and_count_the_rest()
    {
        var bad = Enumerable.Range(0, 15).Select(i => Row(id: (7000 + i).ToString(System.Globalization.CultureInfo.InvariantCulture), underlying: "BAD" + i, lot: "0"));

        var result = await ParseAsync(Header + "\n" + string.Join("\n", Many(2000)) + "\n" + string.Join("\n", bad) + "\n");

        result.IsSuccess.ShouldBeTrue();
        result.Value.Warnings.Count.ShouldBe(11);
        result.Value.Warnings[^1].ShouldBe("…and 5 more rows skipped.");
        result.Value.Protected.Count.ShouldBe(15);
    }

    [Fact]
    public async Task More_than_one_percent_unreadable_in_scope_rows_fails_closed()
    {
        var bad = Enumerable.Range(0, 2).Select(i => Row(id: (7000 + i).ToString(System.Globalization.CultureInfo.InvariantCulture), underlying: "BAD" + i, lot: "0"));

        var result = await ParseAsync(Header + "\n" + string.Join("\n", Many(98)) + "\n" + string.Join("\n", bad) + "\n");

        result.IsFailure.ShouldBeTrue();
        result.Error.Message.ShouldContain("too many unreadable rows (2 of 100)");
    }

    [Fact]
    public async Task Exactly_one_percent_is_still_tolerated()
    {
        var result = await ParseAsync(Header + "\n" + string.Join("\n", Many(99)) + "\n" + Row(id: "7000", underlying: "BAD", lot: "0") + "\n");

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task More_than_fifty_unreadable_rows_fail_closed_even_below_one_percent()
    {
        var bad = Enumerable.Range(0, 51).Select(i => Row(id: (70000 + i).ToString(System.Globalization.CultureInfo.InvariantCulture), underlying: "BAD" + i, lot: "0"));

        var result = await ParseAsync(Header + "\n" + string.Join("\n", Many(6000)) + "\n" + string.Join("\n", bad) + "\n");

        result.IsFailure.ShouldBeTrue();
        result.Error.Message.ShouldContain("too many unreadable rows");
    }

    [Fact]
    public async Task Fifty_unreadable_rows_below_one_percent_are_tolerated()
    {
        var bad = Enumerable.Range(0, 50).Select(i => Row(id: (70000 + i).ToString(System.Globalization.CultureInfo.InvariantCulture), underlying: "BAD" + i, lot: "0"));

        var result = await ParseAsync(Header + "\n" + string.Join("\n", Many(6000)) + "\n" + string.Join("\n", bad) + "\n");

        result.IsSuccess.ShouldBeTrue();
        result.Value.Protected.Count.ShouldBe(50);
    }

    [Fact]
    public async Task A_line_that_is_not_a_table_row_fails_closed()
    {
        var result = await ParseAsync(Header + "\n" + Row() + "\n" + new string('x', CsvRecordReader.MaxRecordLength + 10) + "\n");

        result.IsFailure.ShouldBeTrue();
        result.Error.Message.ShouldContain("not a table");
    }

    [Fact]
    public async Task Options_and_other_segments_are_skipped_without_being_read()
    {
        var option = "NSE,D,5001,NA,OPTSTK,13061,RELIANCE,RELIANCE-Oct2026-720-CE,RELIANCE 720 CE,OP,NA,500.0,2026-10-27,720.0,CE,5.0,M,N,N,N,NA,A,0,0,0,0,0,0,0,10.0,5.0,100,";
        var text = Header + "\n" + Row() + "\n" + option + "\n" + option + "\n";

        var result = await ParseAsync(text);

        result.Value.OutOfScope.ShouldBe(2);
        result.Value.Rows.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Quoted_fields_with_commas_are_read_whole()
    {
        var row = Row(name: "\"ACME, THE LTD\"");

        var result = await ParseAsync(Header + "\n" + row + "\n");

        result.Value.Rows.Single().SymbolName.ShouldBe("ACME, THE LTD");
    }

    [Fact]
    public async Task Windows_line_endings_and_a_byte_order_mark_are_accepted()
    {
        var text = "﻿" + (Header + "\n" + Row() + "\n").Replace("\n", "\r\n", StringComparison.Ordinal);

        var result = await ParseAsync(text);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Rows.Count.ShouldBe(1);
    }

    [Theory]
    [InlineData("N", null)]
    [InlineData("Y", "Y")]
    [InlineData("R", "R")]
    public async Task The_ASM_GSM_flag_is_kept_only_when_set(string flag, string? expected)
    {
        var result = await ParseAsync(Header + "\n" + Row(flag: flag) + "\n");

        result.Value.Rows.Single().AsmGsmFlag.ShouldBe(expected);
    }
}
