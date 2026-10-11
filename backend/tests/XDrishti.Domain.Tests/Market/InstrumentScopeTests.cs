using XDrishti.Domain.Market;

namespace XDrishti.Domain.Tests.Market;

public sealed class InstrumentScopeTests
{
    [Theory]
    [InlineData("NSE", "E", "EQUITY", "ES", InstrumentType.Share)]
    [InlineData("NSE", "E", "EQUITY", "ETF", InstrumentType.Etf)]
    [InlineData("NSE", "I", "INDEX", "INDEX", InstrumentType.Index)]
    [InlineData("NSE", "D", "FUTSTK", "FUT", InstrumentType.Future)]
    [InlineData("NSE", "D", "FUTIDX", "FUT", InstrumentType.Future)]
    public void NSE_shares_ETFs_indices_and_futures_are_kept(string exchange, string segment, string instrument, string type, InstrumentType expected) =>
        InstrumentScope.Classify(exchange, segment, instrument, type, "ABC", "ABC LTD", "Abc").ShouldBe(expected);

    [Theory]
    [InlineData("BSE", "E", "EQUITY", "ES")] // another exchange
    [InlineData("MCX", "D", "FUTCOM", "FUT")]
    [InlineData("NSE", "E", "EQUITY", "DBT")] // debt
    [InlineData("NSE", "E", "EQUITY", "DEB")]
    [InlineData("NSE", "E", "EQUITY", "MF")] // mutual fund units
    [InlineData("NSE", "E", "EQUITY", "Other")] // gold bonds
    [InlineData("NSE", "D", "OPTSTK", "OP")] // options
    [InlineData("NSE", "D", "OPTIDX", "OP")]
    [InlineData("NSE", "C", "FUTCUR", "FUT")] // currency
    [InlineData("NSE", "C", "OPTCUR", "CUR OP")]
    [InlineData("NSE", "I", "FUTIDX", "FUT")] // an index segment row that is not an index
    [InlineData("NSE", "D", "EQUITY", "ES")]
    public void Everything_else_is_out_of_scope(string exchange, string segment, string instrument, string type) =>
        InstrumentScope.Classify(exchange, segment, instrument, type, "ABC", "ABC LTD", "Abc").ShouldBeNull();

    [Theory]
    [InlineData("011NSETEST", "011NSETEST-Nov2036-FUT", "011NSETEST NOV FUT")]
    [InlineData("OK", "OK LTD", "nsetest something")]
    [InlineData("OK", "NSETEST", "Ok")]
    public void Exchange_test_symbols_are_excluded_whichever_field_names_them(string underlying, string name, string display) =>
        InstrumentScope.Classify("NSE", "D", "FUTSTK", "FUT", underlying, name, display).ShouldBeNull();

    [Theory]
    [InlineData("NSE", "E", "EQUITY", true)]
    [InlineData("NSE", "E", "anything", true)]
    [InlineData("NSE", "I", "INDEX", true)]
    [InlineData("NSE", "D", "FUTSTK", true)]
    [InlineData("NSE", "D", "FUTIDX", true)]
    [InlineData("NSE", "D", "OPTSTK", false)]
    [InlineData("NSE", "D", "OPTIDX", false)]
    [InlineData("NSE", "I", "FUTIDX", false)]
    [InlineData("NSE", "C", "FUTCUR", false)]
    [InlineData("BSE", "E", "EQUITY", false)]
    [InlineData("nse", "E", "EQUITY", false)]
    [InlineData("", "", "", false)]
    public void The_cheap_precheck_never_rejects_a_row_Classify_could_keep(string exchange, string segment, string instrument, bool might) =>
        InstrumentScope.MightBeInScope(exchange, segment, instrument).ShouldBe(might);

    [Fact]
    public void Whatever_the_precheck_rejects_Classify_rejects_too()
    {
        foreach (var exchange in new[] { "NSE", "BSE" })
        {
            foreach (var segment in new[] { "E", "I", "D", "C", "M" })
            {
                foreach (var instrument in new[] { "EQUITY", "INDEX", "FUTSTK", "FUTIDX", "OPTSTK", "FUTCUR" })
                {
                    foreach (var type in new[] { "ES", "ETF", "INDEX", "FUT", "OP", "DBT" })
                    {
                        if (!InstrumentScope.MightBeInScope(exchange, segment, instrument))
                        {
                            InstrumentScope.Classify(exchange, segment, instrument, type, "ABC").ShouldBeNull($"{exchange} {segment} {instrument} {type}");
                        }
                    }
                }
            }
        }
    }

    [Theory]
    [InlineData("E", "NSE_EQ")]
    [InlineData("I", "IDX_I")]
    [InlineData("D", "NSE_FNO")]
    public void The_broker_segment_is_Dhans_exchange_segment(string segment, string expected) =>
        InstrumentScope.BrokerSegment(segment).ShouldBe(expected);

    [Theory]
    [InlineData(InstrumentType.Share, "share")]
    [InlineData(InstrumentType.Etf, "etf")]
    [InlineData(InstrumentType.Index, "index")]
    [InlineData(InstrumentType.Future, "future")]
    public void Types_have_a_stable_code_that_parses_back(InstrumentType type, string code)
    {
        type.ToCode().ShouldBe(code);
        InstrumentTypeCodes.TryParse(code, out var parsed).ShouldBeTrue();
        parsed.ShouldBe(type);
    }

    [Theory]
    [InlineData("Share")]
    [InlineData("bond")]
    [InlineData("")]
    [InlineData(null)]
    public void Unknown_type_codes_do_not_parse(string? code) => InstrumentTypeCodes.TryParse(code, out _).ShouldBeFalse();

    [Fact]
    public void India_time_is_a_fixed_offset_of_five_and_a_half_hours()
    {
        IndiaTime.Offset.ShouldBe(TimeSpan.FromMinutes(330));
        IndiaTime.Today(new DateTimeOffset(2026, 10, 11, 18, 29, 59, TimeSpan.Zero)).ShouldBe(new DateOnly(2026, 10, 11));
        IndiaTime.Today(new DateTimeOffset(2026, 10, 11, 18, 30, 0, TimeSpan.Zero)).ShouldBe(new DateOnly(2026, 10, 12));
    }
}
