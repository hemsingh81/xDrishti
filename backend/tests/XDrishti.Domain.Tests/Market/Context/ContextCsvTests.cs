using XDrishti.Domain.Market.Context;

namespace XDrishti.Domain.Tests.Market.Context;

/// <summary>P3-01d fallback D: the tolerant reader of owner-dropped NSE daily CSVs; a bad row is refused with a code, never guessed.</summary>
public sealed class ContextCsvTests
{
    private const string Nifty =
        "﻿\"Index Name\",\"Date\",\"Open\",\"High\",\"Low\",\"Close\"\r\n"
        + "\"NIFTY 50\",\"07 Nov 2025\",\"25,100.20\",\"25,300.10\",\"25,050.00\",\"25,250.75\"\r\n"
        + "\"NIFTY 50\",\"06 Nov 2025\",\"25,000.00\",\"25,200.00\",\"24,900.00\",\"25,100.50 \"\r\n";

    private const string Vix =
        "Date ,Open ,High ,Low ,Close ,Prev. Close ,Change ,% Change \r\n"
        + "07-Nov-2025,12.10,12.80,11.90,12.34,12.00,0.34,2.83\r\n"
        + "06-Nov-2025 ,\"12.00\",12.50,11.80,\"12.00\",11.90,0.10,0.84\r\n";

    [Fact]
    public void A_niftyindices_export_with_bom_quotes_commas_and_spaced_dates_reads_cleanly()
    {
        var parsed = ContextCsv.Parse(Nifty, "NIFTY 50_Historical_PR.csv");

        parsed.Kind.ShouldBe(ContextCsvKind.Index);
        parsed.FileReject.ShouldBeNull();
        parsed.Rejects.ShouldBeEmpty();
        parsed.Rows.Select(r => (r.Date, r.High, r.Low, r.Close)).ShouldBe(
        [
            (new DateOnly(2025, 11, 7), (double?)25_300.10, (double?)25_050.00, 25_250.75),
            (new DateOnly(2025, 11, 6), (double?)25_200.00, (double?)24_900.00, 25_100.50),
        ]);
    }

    [Fact]
    public void An_india_vix_export_with_trailing_spaces_in_headers_reads_as_vix_by_its_header()
    {
        var parsed = ContextCsv.Parse(Vix, "hist.csv");

        parsed.Kind.ShouldBe(ContextCsvKind.Vix);
        parsed.Rejects.ShouldBeEmpty();
        parsed.Rows.Select(r => (r.Date, r.Close)).ShouldBe([(new DateOnly(2025, 11, 7), 12.34), (new DateOnly(2025, 11, 6), 12.00)]);
    }

    [Theory]
    [InlineData("07-Nov-2025")]
    [InlineData("07 Nov 2025")]
    [InlineData("7-Nov-2025")]
    [InlineData("07-11-2025")]
    [InlineData("07/11/2025")]
    [InlineData("2025-11-07")]
    public void The_known_date_forms_read_the_same_day(string date)
    {
        var parsed = ContextCsv.Parse("Date,Open,High,Low,Close\n" + date + ",1,110,90,100\n", "nifty50.csv");

        parsed.Rows.Single().Date.ShouldBe(new DateOnly(2025, 11, 7));
    }

    [Fact]
    public void A_file_name_with_vix_marks_a_header_without_vix_columns_as_vix()
    {
        ContextCsv.Parse("Date,Close\n07-Nov-2025,12.3\n", "india_vix.csv").Kind.ShouldBe(ContextCsvKind.Vix);
    }

    [Fact]
    public void Bad_rows_are_refused_with_their_reason_and_the_good_rows_stay()
    {
        const string text =
            "Date,Open,High,Low,Close\n"
            + "07-Nov-2025,1,110,90,100\n"     // good
            + "not-a-date,1,110,90,100\n"      // bad_date
            + "06-Nov-2025,1,110,90,-\n"       // bad_number
            + "05-Nov-2025,1,90,110,100\n"     // bad_range: high below low
            + "04-Nov-2025,1,110,90,200\n"     // bad_range: close above high
            + "03-Nov-2025,1,110\n"            // short_row
            + "07-Nov-2025,1,110,90,101\n";    // duplicate_date

        var parsed = ContextCsv.Parse(text, "nifty50.csv");

        parsed.Rows.Select(r => r.Date).ShouldBe([new DateOnly(2025, 11, 7)]);
        parsed.Rejects.Select(r => r.Code).ShouldBe(
        [
            ContextCsvCodes.BadDate, ContextCsvCodes.BadNumber, ContextCsvCodes.BadRange, ContextCsvCodes.BadRange, ContextCsvCodes.ShortRow, ContextCsvCodes.DuplicateDate,
        ]);
        parsed.Rejects.Select(r => r.Line).ShouldBe([3, 4, 5, 6, 7, 8]);
    }

    [Fact]
    public void A_row_of_another_index_in_a_nifty_file_is_refused()
    {
        var parsed = ContextCsv.Parse("Index Name,Date,Open,High,Low,Close\nNIFTY BANK,07-Nov-2025,1,110,90,100\nNIFTY 50,07-Nov-2025,1,110,90,100\n", null);

        parsed.Rows.Count.ShouldBe(1);
        parsed.Rejects.Single().Code.ShouldBe(ContextCsvCodes.WrongIndex);
    }

    [Fact]
    public void An_implausible_vix_is_refused()
    {
        var parsed = ContextCsv.Parse("Date,Open,High,Low,Close,Prev. Close,Change,% Change\n07-Nov-2025,1,2,1,500,1,1,1\n", null);

        parsed.Rows.ShouldBeEmpty();
        parsed.Rejects.Single().Code.ShouldBe(ContextCsvCodes.BadRange);
    }

    [Theory]
    [InlineData("", ContextCsvCodes.Empty)]
    [InlineData("Name,Value\na,1\n", ContextCsvCodes.NoHeader)]
    [InlineData("Date,High,Low,Close\n", ContextCsvCodes.Empty)]
    [InlineData("Date,Close\n07-Nov-2025,100\n", ContextCsvCodes.MissingColumn)]
    public void A_file_that_cannot_be_read_is_refused_whole(string text, string code)
    {
        ContextCsv.Parse(text, "nifty50.csv").FileReject.ShouldBe(code);
    }

    [Theory]
    [InlineData("NIFTY BANK_Historical.csv")]
    [InlineData(null)]
    public void An_index_file_without_an_index_name_column_must_say_nifty_50_in_its_file_name(string? fileName)
    {
        var parsed = ContextCsv.Parse("Date,Open,High,Low,Close\n07-Nov-2025,1,110,90,100\n", fileName);

        parsed.FileReject.ShouldBe(ContextCsvCodes.UnknownIndex);
        parsed.Rows.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("NIFTY 50_Historical_PR.csv")]
    [InlineData("nifty50.csv")]
    [InlineData("Nifty_50-history.csv")]
    public void The_nifty_50_file_name_forms_are_accepted(string fileName)
    {
        ContextCsv.Parse("Date,Open,High,Low,Close\n07-Nov-2025,1,110,90,100\n", fileName).FileReject.ShouldBeNull();
    }

    [Fact]
    public void A_file_that_mixes_good_rows_with_unreadable_numeric_dates_is_refused_whole()
    {
        // A month/day swap: 07-11-2025 reads as 7 November, 11-13-2025 cannot be a day-first date.
        var parsed = ContextCsv.Parse("Date,Open,High,Low,Close\n07-11-2025,1,110,90,100\n11-13-2025,1,110,90,100\n", "nifty50.csv");

        parsed.FileReject.ShouldBe(ContextCsvCodes.MixedDates);
        parsed.Rows.ShouldBeEmpty();
    }
}
