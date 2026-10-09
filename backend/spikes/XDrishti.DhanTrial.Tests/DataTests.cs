
namespace XDrishti.DhanTrial.Tests;

public class CandleParserTests
{
    [Fact]
    public void Parses_dhan_parallel_arrays_into_bars()
    {
        var bars = CandleParser.Parse("{\"open\":[1.5,2.5],\"high\":[2,3],\"low\":[1,2],\"close\":[1.75,2.75],\"volume\":[10,20],\"timestamp\":[1760000000,1760000060],\"open_interest\":[0,0]}");

        bars.Count.ShouldBe(2);
        bars[1].ShouldBe(new Bar(1760000060, 2.5, 3, 2, 2.75, 20));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"errorCode\":\"DH-907\"}")]
    [InlineData("{\"open\":[],\"high\":[],\"low\":[],\"close\":[],\"volume\":[],\"timestamp\":[]}")]
    public void Error_or_empty_bodies_yield_no_bars(string json)
    {
        CandleParser.Parse(json).ShouldBeEmpty();
    }

    [Fact]
    public void Arrays_of_different_length_are_rejected()
    {
        Should.Throw<FormatException>(() => CandleParser.Parse("{\"open\":[1],\"high\":[1,2],\"low\":[1],\"close\":[1],\"volume\":[1],\"timestamp\":[1]}"));
    }
}

public class DateChunkerTests
{
    [Fact]
    public void A_year_is_split_into_ranges_of_at_most_90_days_without_gaps_or_overlap()
    {
        var ranges = DateChunker.Split(new DateOnly(2025, 10, 9), new DateOnly(2026, 10, 9), 90);

        ranges.Count.ShouldBe(5);
        ranges.ShouldAllBe(r => r.To.DayNumber - r.From.DayNumber + 1 <= 90);
        for (var i = 1; i < ranges.Count; i++)
        {
            ranges[i].From.ShouldBe(ranges[i - 1].To.AddDays(1));
        }

        ranges[0].From.ShouldBe(new DateOnly(2025, 10, 9));
        ranges[^1].To.ShouldBe(new DateOnly(2026, 10, 9));
    }

    [Fact]
    public void A_short_window_is_a_single_chunk()
    {
        DateChunker.Split(new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 9), 90).Count.ShouldBe(1);
    }
}

public class DhanErrorsTests
{
    [Theory]
    [InlineData(400, "{\"errorType\":\"Input_Exception\",\"errorCode\":\"DH-905\",\"errorMessage\":\"Missing fields\"}", "DH-905")]
    [InlineData(200, "{\"status\":\"failed\",\"data\":{\"806\":\"Data APIs not subscribed\"}}", "806")]
    [InlineData(500, "not json", "HTTP 500")]
    public void Error_codes_are_extracted_from_both_response_shapes(int status, string body, string expected)
    {
        DhanErrors.Parse(status, body).Code.ShouldBe(expected);
    }

    [Fact]
    public void A_good_candle_response_is_not_an_error()
    {
        DhanErrors.Parse(200, "{\"open\":[1]}").Code.ShouldBeNull();
    }

    [Theory]
    [InlineData(429, null, true)]
    [InlineData(200, "DH-904", true)]
    [InlineData(200, "805", true)]
    [InlineData(400, "DH-905", false)]
    public void Throttling_is_recognised(int status, string? code, bool expected)
    {
        DhanErrors.IsThrottle(status, code).ShouldBe(expected);
    }
}

public class IstTimeTests
{
    [Theory]
    [InlineData("2026-10-10T20:30:00")]
    [InlineData("10/10/2026 20:30")]
    [InlineData("2026-10-10 20:30:00")]
    public void Zone_less_dhan_timestamps_are_read_as_indian_time(string text)
    {
        IstTime.TryParse(text, out var value).ShouldBeTrue();

        value.ShouldBe(new DateTimeOffset(2026, 10, 10, 20, 30, 0, TimeSpan.FromMinutes(330)));
    }

    [Fact]
    public void Garbage_is_not_parsed()
    {
        IstTime.TryParse("tomorrow-ish", out _).ShouldBeFalse();
    }
}

public class InstrumentMasterTests
{
    private const string Header = "SEM_EXM_EXCH_ID,SEM_SEGMENT,SEM_SMST_SECURITY_ID,SEM_INSTRUMENT_NAME,SEM_EXPIRY_CODE,SEM_TRADING_SYMBOL,SEM_LOT_UNITS,SEM_CUSTOM_SYMBOL,SEM_EXPIRY_DATE,SEM_STRIKE_PRICE,SEM_OPTION_TYPE,SEM_TICK_SIZE,SEM_EXPIRY_FLAG,SEM_EXCH_INSTRUMENT_TYPE,SEM_SERIES,SM_SYMBOL_NAME";

    private static InstrumentMaster Sample() => InstrumentMaster.Parse(new StringReader(string.Join(
        "\n",
        Header,
        "NSE,E,2885,EQUITY,0,RELIANCE,1,\"Reliance Industries, Ltd\",,,,0.05,,ES,EQ,RELIANCE INDUSTRIES",
        "NSE,E,9999,EQUITY,0,RELIANCE-BE,1,Reliance BE,,,,0.05,,ES,BE,RELIANCE INDUSTRIES",
        "NSE,I,13,INDEX,0,NIFTY,1,Nifty 50,,,,0,,INDEX,,NIFTY 50",
        "NSE,I,25,INDEX,0,BANKNIFTY,1,Nifty Bank,,,,0,,INDEX,,NIFTY BANK",
        "BSE,E,500325,EQUITY,0,RELIANCE,1,Reliance,,,,0.05,,A,A,RELIANCE",
        "NSE,D,5555,FUTSTK,1,RELIANCE-Oct2026-FUT,500,Reliance Fut,,,,0.05,,FUT,,RELIANCE")));

    [Fact]
    public void Only_nse_equity_and_index_rows_are_kept_and_quoted_commas_survive()
    {
        var master = Sample();

        master.TotalRows.ShouldBe(6);
        master.Rows.Count.ShouldBe(4);
        master.Rows.First(r => r.SecurityId == "2885").DisplayName.ShouldBe("Reliance Industries, Ltd");
    }

    [Fact]
    public void An_equity_resolves_to_its_eq_series_security_id_and_nse_eq_segment()
    {
        var resolution = Sample().Resolve("RELIANCE", SymbolKind.Equity);

        resolution.Status.ShouldBe(ResolutionStatus.Found);
        resolution.Key.ShouldBe(new InstrumentKey("2885", "NSE_EQ", "EQUITY"));
    }

    [Fact]
    public void An_index_resolves_through_its_aliases_to_the_idx_segment()
    {
        var resolution = Sample().Resolve("NIFTY 50", SymbolKind.Index, ["NIFTY", "Nifty 50"]);

        resolution.Status.ShouldBe(ResolutionStatus.Found);
        resolution.Key.ShouldBe(new InstrumentKey("13", "IDX_I", "INDEX"));
    }

    [Fact]
    public void Unknown_symbols_are_reported_missing_with_near_matches_and_never_guessed()
    {
        var resolution = Sample().Resolve("RELIANC", SymbolKind.Equity);

        resolution.Status.ShouldBe(ResolutionStatus.Missing);
        resolution.Key.ShouldBeNull();
        resolution.Candidates.ShouldContain(r => r.SecurityId == "2885");
    }

    [Fact]
    public void Two_equally_good_matches_are_ambiguous()
    {
        var master = InstrumentMaster.Parse(new StringReader(string.Join("\n", Header, "NSE,E,1,EQUITY,0,ABC,1,Abc,,,,0.05,,ES,EQ,ABC", "NSE,E,2,EQUITY,0,ABC,1,Abc,,,,0.05,,ES,EQ,ABC")));

        master.Resolve("ABC", SymbolKind.Equity).Status.ShouldBe(ResolutionStatus.Ambiguous);
    }

    [Fact]
    public void A_header_without_the_security_id_column_fails_loudly_and_shows_the_header()
    {
        var ex = Should.Throw<FormatException>(() => InstrumentMaster.Parse(new StringReader("A,B,C\n1,2,3")));

        ex.Message.ShouldContain("A, B, C");
    }
}
