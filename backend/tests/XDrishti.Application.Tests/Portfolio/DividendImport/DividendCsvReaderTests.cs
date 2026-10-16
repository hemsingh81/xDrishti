using System.Text;
using XDrishti.Application.Portfolio.DividendImport;
using XDrishti.Domain.Portfolio;
using static XDrishti.Application.Tests.Portfolio.DividendImport.DividendFiles;

namespace XDrishti.Application.Tests.Portfolio.DividendImport;

/// <summary>The dividend file reader (ADR 0028, P2-15 criteria 2 and 3): formats, header aliases, bad rows, identical repeats, limits. Today is 2026-10-15.</summary>
public sealed class DividendCsvReaderTests
{
    [Fact]
    public async Task The_made_up_payout_file_in_dhans_shape_gives_its_rows_its_total_and_the_repeats()
    {
        var file = (await ReadSampleAsync("dividend-payout-shape.csv")).Value;

        file.Rows.ShouldBe(21);
        file.Parsed.ShouldBe(21);
        file.Items.Count.ShouldBe(18);
        file.Repeated.ShouldBe(3);
        file.BadTotal.ShouldBe(0);
        file.ParsedAmount.ShouldBe(18671.00m);
        file.Items.Sum(i => i.Amount).ShouldBe(18523.00m);
        file.Repeats.Select(r => (r.Name, r.Occurrences, r.FirstLine)).ShouldBe([("Nimbus Chemicals", 2, 13), ("Quartz Engineering", 3, 15)]);
        file.Repeats[1].Lines.ShouldBe([15, 16, 17]);
    }

    [Fact]
    public async Task Sept_and_thousands_separators_and_an_ampersand_are_read_as_written()
    {
        var file = (await ReadSampleAsync("dividend-payout-shape.csv")).Value;

        file.Items.Single(i => i.Name.StartsWith("Delta", StringComparison.Ordinal)).PayDate.ShouldBe(new DateOnly(2024, 9, 23));
        file.Items.Single(i => i.Name.StartsWith("Vertex", StringComparison.Ordinal)).Amount.ShouldBe(12000m);
        file.Items.Single(i => i.PayDate == new DateOnly(2025, 2, 5)).Name.ShouldBe("Kestrel Foods & Beverages");
    }

    [Fact]
    public async Task Two_payments_of_one_name_on_one_day_with_different_amounts_are_two_rows()
    {
        var file = (await ReadSampleAsync("dividend-payout-shape.csv")).Value;

        var march = file.Items.Where(i => i.PayDate == new DateOnly(2025, 3, 18)).ToList();

        march.Select(m => (m.PerShare, m.Amount)).ShouldBe([(5m, 500m), (4.5m, 450m)]);
    }

    [Theory]
    [InlineData("13 Jun 2024")]
    [InlineData("13 jun 2024")]
    [InlineData("13 June 2024")]
    [InlineData("2024-06-13")]
    [InlineData("13-06-2024")]
    [InlineData("13/06/2024")]
    [InlineData("13-Jun-2024")]
    [InlineData("2024-06-13T00:00:00")]
    [InlineData("2024-6-13")]
    [InlineData("  13 Jun 2024  ")]
    public async Task A_date_in_any_of_the_accepted_formats_is_the_same_day(string date)
    {
        var row = await OneAsync($"{date},Orion Steel,2,10,20.00");

        row.PayDate.ShouldBe(new DateOnly(2024, 6, 13));
    }

    [Theory]
    [InlineData("23 Sept 2024")]
    [InlineData("23 SEPT 2024")]
    [InlineData("23 Sep 2024")]
    [InlineData("23 September 2024")]
    [InlineData("23-Sept-2024")]
    public async Task September_is_read_whether_written_sept_sep_or_in_full(string date)
    {
        var row = await OneAsync($"{date},Orion Steel,2,10,20.00");

        row.PayDate.ShouldBe(new DateOnly(2024, 9, 23));
    }

    [Theory]
    [InlineData("06/13/2024")]
    [InlineData("13/25/2024")]
    [InlineData("31 Feb 2024")]
    [InlineData("Jun 13 2024")]
    [InlineData("13 Juni 2024")]
    [InlineData("tomorrow")]
    [InlineData("")]
    public async Task A_date_that_is_not_day_first_or_not_a_day_is_a_bad_row_never_guessed(string date)
    {
        var reason = await BadReasonAsync($"{date},Orion Steel,2,10,20.00");

        reason.ShouldContain("Date");
    }

    [Fact]
    public async Task A_date_after_today_and_one_before_1990_are_bad_rows()
    {
        var future = await BadReasonAsync("16 Oct 2026,Orion Steel,2,10,20.00");
        var old = await BadReasonAsync("01 Jan 1989,Orion Steel,2,10,20.00");

        future.ShouldContain("future");
        old.ShouldContain("before");
    }

    [Fact]
    public async Task Today_itself_is_a_valid_pay_date()
    {
        var row = await OneAsync("15 Oct 2026,Orion Steel,2,10,20.00");

        row.PayDate.ShouldBe(Today);
    }

    [Theory]
    [InlineData("\"1,134.00\"", 1134.00)]
    [InlineData("1134", 1134)]
    [InlineData("\"12,34,567.50\"", 1234567.50)]
    [InlineData("0.01", 0.01)]
    [InlineData("14", 14)]
    [InlineData("1234.567", 1234.57)]
    public async Task The_amount_paid_has_a_point_for_decimals_and_commas_for_thousands_and_is_kept_to_the_paisa(string cell, double expected)
    {
        var row = await OneAsync($"13 Jun 2024,Orion Steel,2,10,{cell}");

        row.Amount.ShouldBe((decimal)expected);
    }

    [Theory]
    [InlineData("0", "above 0")]
    [InlineData("-5", "above 0")]
    [InlineData("12abc", "not a number")]
    [InlineData("", "empty")]
    [InlineData("\"1,2,3\"", "not a number")]
    [InlineData("₹100", "not a number")]
    [InlineData("1e3", "not a number")]
    [InlineData("0.004", "above 0")]
    [InlineData("2000000000", "At most")]
    public async Task An_amount_that_is_not_a_positive_number_is_a_bad_row(string cell, string reason)
    {
        var problem = await BadReasonAsync($"13 Jun 2024,Orion Steel,2,10,{cell}");

        problem.ShouldContain(reason);
    }

    [Fact]
    public async Task A_cell_longer_than_64_characters_is_a_bad_row_for_a_date_and_for_a_number()
    {
        var long64 = new string('1', 65);

        var date = await BadReasonAsync($"{long64},Orion Steel,2,10,20.00");
        var amount = await BadReasonAsync($"13 Jun 2024,Orion Steel,2,10,{long64}");

        date.ShouldContain("too long");
        amount.ShouldContain("too long");
    }

    [Fact]
    public async Task The_per_share_amount_may_be_zero_or_absent_but_not_negative_and_the_quantity_is_whole_and_above_0_or_absent()
    {
        var zero = await OneAsync("13 Jun 2024,Orion Steel,0,10,20.00");
        var absent = await OneAsync("13 Jun 2024,Orion Steel,,,20.00");
        var negative = await BadReasonAsync("13 Jun 2024,Orion Steel,-1,10,20.00");
        var fraction = await BadReasonAsync("13 Jun 2024,Orion Steel,2,10.5,20.00");
        var none = await BadReasonAsync("13 Jun 2024,Orion Steel,2,0,20.00");
        var minus = await BadReasonAsync("13 Jun 2024,Orion Steel,2,-3,20.00");

        (zero.PerShare, zero.Quantity).ShouldBe((0m, 10L));
        (absent.PerShare, absent.Quantity).ShouldBe((null, null));
        negative.ShouldContain("Dividend per share");
        fraction.ShouldContain("whole number");
        none.ShouldContain("Quantity");
        minus.ShouldContain("Quantity");
    }

    [Fact]
    public async Task A_whole_quantity_with_a_decimal_point_and_thousands_commas_is_read()
    {
        var row = await OneAsync("13 Jun 2024,Orion Steel,0.5,\"2,000.00\",\"1,000.00\"");

        row.Quantity.ShouldBe(2000);
    }

    [Fact]
    public async Task An_empty_scrip_name_is_a_bad_row_and_the_symbol_stands_in_when_the_file_has_one()
    {
        var empty = await BadReasonAsync("13 Jun 2024,,2,10,20.00");
        var symbolled = await OneAsync("13 Jun 2024,,ORIONSTEEL,2,10,20.00", "Date,Scrip Name,Symbol,Dividend Per Share,Quantity,Dividend Paid");

        empty.ShouldContain("scrip name");
        (symbolled.Name, symbolled.Symbol).ShouldBe(("ORIONSTEEL", "ORIONSTEEL"));
    }

    [Fact]
    public async Task A_name_with_line_breaks_is_made_plain_text_and_a_name_over_200_characters_is_a_bad_row()
    {
        var plain = await OneAsync("13 Jun 2024,\"Orion\nSteel\t<b>x</b>\",2,10,20.00");
        var tooLong = await BadReasonAsync($"13 Jun 2024,{new string('a', 201)},2,10,20.00");

        plain.Name.ShouldBe("Orion Steel <b>x</b>");
        tooLong.ShouldContain("longer than 200");
    }

    [Fact]
    public async Task The_isin_and_symbol_columns_are_read_and_a_bad_isin_is_a_bad_row()
    {
        const string header = "Date,Company,ISIN,Symbol,Dividend Paid";

        var row = await OneAsync("13 Jun 2024,Orion Steel,ine000a01011,nse:orionsteel-eq,20.00", header);
        var reason = await BadReasonAsync("13 Jun 2024,Orion Steel,12345,ORION,20.00", header);

        (row.Isin, row.Symbol).ShouldBe(("INE000A01011", "ORIONSTEEL"));
        reason.ShouldContain("ISIN");
    }

    [Theory]
    [InlineData("Date,Scrip Name,Dividend Per Share,Quantity,Dividend Paid")]
    [InlineData("date,scrip_name,dividend_per_share,qty,dividend_paid")]
    [InlineData("\"DATE\",\"SCRIP NAME\",\"DIVIDEND PER SHARE\",\"QUANTITY\",\"DIVIDEND PAID\"")]
    [InlineData("Pay Date,Company Name,DPS,Shares,Amount")]
    [InlineData("Payment Date,Security Name,Per Share,No. of shares,Net Amount")]
    [InlineData("Date,Name,Payout")]
    public async Task Header_names_are_matched_without_regard_to_case_or_punctuation(string header)
    {
        var columns = header.Split(',').Length;
        var row = columns == 5 ? "13 Jun 2024,Orion Steel,2,10,20.00" : "13 Jun 2024,Orion Steel,20.00";

        var parsed = await OneAsync(row, header);

        (parsed.PayDate, parsed.Amount).ShouldBe((new DateOnly(2024, 6, 13), 20m));
    }

    [Fact]
    public async Task A_byte_order_mark_and_blank_lines_and_a_missing_final_newline_do_not_matter()
    {
        var bytes = new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(Header + "\n\n13 Jun 2024,Orion Steel,2,10,20.00\n,,,,\n14 Jun 2024,Delta Power,1,10,10.00")).ToArray();
        await using var stream = new MemoryStream(bytes);

        var file = (await DividendCsvReader.ReadAsync(stream, Today, CancellationToken.None)).Value;

        file.Items.Select(i => i.Name).ShouldBe(["Orion Steel", "Delta Power"]);
        file.Rows.ShouldBe(2);
    }

    [Fact]
    public async Task A_file_that_is_not_utf8_is_refused_with_the_field_file()
    {
        await using var stream = new MemoryStream([.. Encoding.UTF8.GetBytes(Header + "\n13 Jun 2024,Orion "), 0xFF, 0xFE, .. Encoding.UTF8.GetBytes("Steel,2,10,20.00\n")]);

        var result = await DividendCsvReader.ReadAsync(stream, Today, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        (result.Error.Code, result.Error.Fields!.Single().Field).ShouldBe((PortfolioErrors.InvalidCode, "file"));
    }

    [Fact]
    public async Task An_empty_file_is_refused_and_a_header_alone_is_an_empty_list()
    {
        var empty = await ReadAsync(string.Empty);
        var headerOnly = await ReadAsync(Header + "\n");

        empty.IsFailure.ShouldBeTrue();
        Text(empty.Error).ShouldContain("empty");
        headerOnly.Value.Rows.ShouldBe(0);
        headerOnly.Value.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Bad_rows_are_listed_with_their_line_up_to_200_and_all_counted_and_never_imported()
    {
        var rows = new List<string> { "13 Jun 2024,Orion Steel,2,10,20.00" };
        rows.AddRange(Enumerable.Range(0, 250).Select(_ => "13 Jun 2024,Orion Steel,2,10,oops"));
        rows.Add("14 Jun 2024,Delta Power,1,10,10.00");

        var file = (await ReadAsync(Csv([.. rows]))).Value;

        file.Items.Count.ShouldBe(2);
        file.BadTotal.ShouldBe(250);
        file.Bad.Count.ShouldBe(200);
        file.Bad[0].Line.ShouldBe(3);
        file.Bad[199].Line.ShouldBe(202);
        file.Rows.ShouldBe(252);
    }

    [Fact]
    public async Task A_row_with_a_quoted_field_spanning_lines_reports_the_line_it_starts_on()
    {
        var file = (await ReadAsync(Csv("13 Jun 2024,\"Orion\nSteel\",2,10,bad", "14 Jun 2024,Delta Power,1,10,10.00"))).Value;

        file.Bad.ShouldHaveSingleItem().Line.ShouldBe(2);
        file.Items.Single().Line.ShouldBe(4);
    }

    [Fact]
    public async Task Rows_identical_in_date_name_per_share_quantity_and_amount_count_once_whatever_the_spelling_of_the_name()
    {
        var file = await OkAsync(Header, "03 Jul 2026,Quartz Engineering Ltd.,14,1,14.00", "03 Jul 2026,QUARTZ ENGINEERING LIMITED,14,1,14.00", "03 Jul 2026,Quartz Engineering,14,1,14.00");

        file.Items.Count.ShouldBe(1);
        file.Repeated.ShouldBe(2);
        file.Parsed.ShouldBe(3);
        file.ParsedAmount.ShouldBe(42m);
        file.Repeats.Single().Lines.ShouldBe([2, 3, 4]);
    }

    [Fact]
    public async Task Rows_that_differ_in_per_share_quantity_or_amount_are_not_repeats()
    {
        var file = await OkAsync(Header, "18 Mar 2025,Lumen Oils,5,100,500.00", "18 Mar 2025,Lumen Oils,4.5,100,450.00", "18 Mar 2025,Lumen Oils,5,50,250.00", "18 Mar 2025,Lumen Oils,,,500.00");

        file.Items.Count.ShouldBe(4);
        file.Repeated.ShouldBe(0);
    }

    [Fact]
    public async Task More_than_20000_data_rows_are_refused_and_exactly_20000_are_read()
    {
        var row = "13 Jun 2024,Orion Steel,2,10,20.00";
        var many = await ReadAsync(Csv([.. Enumerable.Repeat(row, 20_001)]));
        var enough = await ReadAsync(Csv([.. Enumerable.Repeat(row, 20_000)]));

        many.IsFailure.ShouldBeTrue();
        Text(many.Error).ShouldContain("20,000");
        enough.Value.Rows.ShouldBe(20_000);
    }

    [Fact]
    public async Task A_file_over_10_MB_is_refused_before_it_is_read()
    {
        await using var stream = new MemoryStream(new byte[(10 * 1024 * 1024) + 1]);

        var result = await DividendCsvReader.ReadAsync(stream, Today, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        Text(result.Error).ShouldContain("10 MB");
    }

    [Fact]
    public async Task A_dividend_file_without_a_date_names_what_is_missing_and_the_accepted_names()
    {
        var result = await ReadAsync(CsvWithHeader("Company,Dividend Paid", "Orion Steel,20.00"));

        result.IsFailure.ShouldBeTrue();
        (result.Error.Code, result.Error.Fields!.Single().Field).ShouldBe((PortfolioErrors.InvalidCode, "file"));
        result.Error.Fields!.Single().Message.ShouldContain("date");
        result.Error.Fields!.Single().Message.ShouldContain("Dividend Per Share");
    }

    [Fact]
    public async Task A_tradebook_is_refused_as_a_tradebook_and_one_without_a_side_column_is_explained()
    {
        var trades = await ReadAsync(CsvWithHeader("date,symbol,side,quantity,price", "2024-03-05,EXMPL,buy,10,90"));
        var sideless = await ReadAsync(CsvWithHeader("Trade Date,Symbol,Order Type,Segment,Quantity,Price", "2024-03-05,EXMPL,Market,EQ,10,90"));

        (trades.Error.Code, trades.Error.Payload!["detected"]).ShouldBe((PortfolioErrors.WrongFileKindCode, "trades"));
        (sideless.Error.Code, sideless.Error.Payload!["detected"]).ShouldBe((PortfolioErrors.WrongFileKindCode, "tradebook_without_sides"));
        sideless.Error.Message.ShouldContain("no buy/sell column");
    }

    [Theory]
    [InlineData("Date,Symbol,Side,Quantity,Price,Net Amount", "trades")]
    [InlineData("Date,Symbol,Side,Quantity,Price,Amount", "trades")]
    [InlineData("Trade Date,Symbol,Trade Type,Quantity,Rate,Net Amount", "trades")]
    [InlineData("Date,Symbol,Quantity,Price,Net Amount", "tradebook_without_sides")]
    [InlineData("Date,Symbol,Qty,Rate,Amount", "tradebook_without_sides")]
    public async Task A_tradebook_with_an_amount_column_is_refused_as_a_tradebook_never_read_as_dividends(string header, string detected)
    {
        var result = await ReadAsync(CsvWithHeader(header, "2024-03-05,EXMPL,buy,10,90,900", "2024-03-06,EXMPL,10,90,900"));

        result.IsFailure.ShouldBeTrue();
        (result.Error.Code, result.Error.Payload!["detected"]).ShouldBe((PortfolioErrors.WrongFileKindCode, detected));
    }

    [Theory]
    [InlineData("Date,Symbol,Side,Quantity,Net Amount")]
    [InlineData("Date,Symbol,Transaction Type,Amount,Dividend Per Share")]
    public async Task A_file_with_a_buy_sell_column_is_never_a_dividend_list(string header)
    {
        var result = await ReadAsync(CsvWithHeader(header, "2024-03-05,EXMPL,buy,10,90"));

        result.IsFailure.ShouldBeTrue();
        (result.Error.Code, result.Error.Fields!.Single().Field).ShouldBe((PortfolioErrors.InvalidCode, "file"));
        result.Error.Fields!.Single().Message.ShouldContain("buy/sell");
    }

    [Theory]
    [InlineData("Pay Date,Company,Amount")]
    [InlineData("Date,Symbol,Net Amount")]
    public async Task A_statement_of_plain_amounts_is_refused_as_a_statement(string header)
    {
        var result = await ReadAsync(CsvWithHeader(header, "2024-03-05,EXMPL,900"));

        result.IsFailure.ShouldBeTrue();
        (result.Error.Code, result.Error.Payload!["detected"]).ShouldBe((PortfolioErrors.WrongFileKindCode, "statement"));
        result.Error.Message.ShouldContain("nothing that says the amounts are dividends");
    }

    [Fact]
    public async Task A_dividend_per_share_column_makes_a_plain_amount_the_dividend_and_a_rate_is_not_read_as_a_price_of_a_trade()
    {
        var parsed = await OneAsync("13 Jun 2024,Orion Steel,2,10,20.00", "Pay Date,Company,Dividend Per Share,Quantity,Net Amount");

        (parsed.PerShare, parsed.Quantity, parsed.Amount).ShouldBe((2m, 10L, 20m));
    }
}
