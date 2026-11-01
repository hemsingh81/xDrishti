using XDrishti.Application.Market.Import;
using XDrishti.Application.Options.Import;
using XDrishti.Application.Tests.Market;
using XDrishti.Domain.Options;

namespace XDrishti.Application.Tests.Options;

/// <summary>Spec P14-01 criteria 1, 2 and 10: the option rows of the listed underlyings are collected by the same single read, and the master list's parse does not change.</summary>
public sealed class OptionRowCollectorTests
{
    private static readonly OptionUnderlyings Listed = OptionUnderlyings.From(["NSE:NIFTY", "NSE:BANKNIFTY", "NSE:RELIANCE", "NSE:CROMPTON"]);

    private static readonly OptionUnderlyings NiftyOnly = OptionUnderlyings.From(["NSE:NIFTY"]);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<MasterParse> ParseAsync(string text, OptionUnderlyings? listed)
    {
        await using var stream = MasterFixtures.AsStream(text);
        var parsed = await DhanMasterParser.ParseAsync(stream, null, Ct, listed);
        parsed.IsSuccess.ShouldBeTrue(parsed.IsFailure ? parsed.Error.Message : null);
        return parsed.Value;
    }

    [Fact]
    public async Task The_master_lists_parse_is_the_same_with_or_without_a_listing()
    {
        var text = OptionMasterRows.Master();

        var without = await ParseAsync(text, null);
        var with = await ParseAsync(text, Listed);

        with.Rows.ShouldBe(without.Rows);
        (with.RowsRead, with.OutOfScope).ShouldBe((without.RowsRead, without.OutOfScope));
        with.Warnings.ShouldBe(without.Warnings);
        with.Protected.ShouldBe(without.Protected);
        without.Options.ShouldBeNull();
    }

    [Fact]
    public async Task Only_nse_option_rows_of_listed_underlyings_are_kept_the_others_are_counted_not_listed()
    {
        var parse = await ParseAsync(OptionMasterRows.Master(), OptionUnderlyings.From(["NSE:NIFTY", "NSE:RELIANCE"]));

        var options = parse.Options.ShouldNotBeNull();
        options.Rows.Select(r => r.SecurityId).Order().ShouldBe([35008L, 35009, 35062, 106474, 106475, 106476]);
        options.SkippedNotListed.ShouldBe(2, "BANKNIFTY and CROMPTON; the BSE and currency options are not NSE option rows at all");
        options.Invalid.ShouldBe(0);
        options.Unavailable.ShouldBeNull();
    }

    [Fact]
    public async Task A_row_maps_to_the_contract_columns_strike_in_paise_and_the_weekly_flag()
    {
        var parse = await ParseAsync(OptionMasterRows.Master(), Listed);

        var rows = parse.Options!.Rows.ToDictionary(r => r.SecurityId);
        rows[35008].ShouldBe(new OptionContractRow(35008, "NSE:NIFTY", "OPTIDX", new DateOnly(2026, 11, 9), 1_735_000, "CE", "week", 65, 5));
        rows[35009].Side.ShouldBe("PE");
        rows[35062].ExpiryKind.ShouldBe("month");
        rows[35000].ShouldBe(new OptionContractRow(35000, "NSE:BANKNIFTY", "OPTIDX", new DateOnly(2026, 12, 29), 6_940_000, "CE", "month", 30, 5));
        rows[100325].ShouldBe(new OptionContractRow(100325, "NSE:CROMPTON", "OPTSTK", new DateOnly(2026, 11, 23), 14_750, "CE", "month", 2150, 1));
        rows[106474].ShouldBe(new OptionContractRow(106474, "NSE:RELIANCE", "OPTSTK", new DateOnly(2026, 10, 27), 72_000, "CE", "month", 500, 5));
    }

    [Theory]
    [InlineData(13, "NA")] // expiry
    [InlineData(13, "27-10-2026")]
    [InlineData(14, "")] // strike
    [InlineData(14, "abc")]
    [InlineData(14, "0.00000")]
    [InlineData(14, "-100.0")]
    [InlineData(15, "XX")] // side
    [InlineData(15, "")]
    [InlineData(12, "NA")] // lot
    [InlineData(12, "0.0")]
    [InlineData(12, "12.5")]
    [InlineData(12, "79228162514264337593543950335")]
    [InlineData(14, "12345678901234567890123456789")] // a 29-digit strike never fails the master import
    [InlineData(3, "99999999999999999999999")]
    [InlineData(3, "x")] // security id
    public async Task A_row_with_an_unreadable_expiry_strike_side_lot_or_id_is_counted_invalid_and_never_stored(int column, string value)
    {
        var bad = OptionMasterRows.With(OptionMasterRows.NiftyWeeklyCall, column - 1, value);

        var parse = await ParseAsync(OptionMasterRows.Master(bad, OptionMasterRows.NiftyWeeklyPut), Listed);

        parse.Options!.Invalid.ShouldBe(1);
        parse.Options.Rows.Select(r => r.SecurityId).ShouldNotContain(35008);
        parse.Options.Rows.ShouldAllBe(r => r.StrikePx > 0 && r.LotSize > 0);
        parse.Warnings.ShouldBeEmpty("an option row never adds a warning to the master list's parse");
    }

    [Theory]
    [InlineData("17350.00000", 1_735_000)]
    [InlineData("147.50000", 14_750)]
    [InlineData("100.005", 10_001)]
    [InlineData("100.004", 10_000)]
    [InlineData("0.005", 1)]
    [InlineData("21474836.47", int.MaxValue)]
    public void The_strike_is_rupees_times_100_rounded_half_away_from_zero(string text, int paise)
    {
        OptionRowCollector.StrikePaise(text).ShouldBe(paise);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("0.004")]
    [InlineData("-1")]
    [InlineData("21474836.48")]
    [InlineData("21474836.475")]
    [InlineData("12345678901234567890123456789")]
    [InlineData("79228162514264337593543950335")]
    [InlineData("NA")]
    [InlineData("1e3")]
    public void A_strike_that_is_not_above_zero_or_not_an_int_of_paise_is_none(string text)
    {
        OptionRowCollector.StrikePaise(text).ShouldBeNull();
    }

    [Theory]
    [InlineData("5.0000", 5)]
    [InlineData("1.0000", 1)]
    [InlineData("0.5", null)]
    [InlineData("0", null)]
    [InlineData("NA", null)]
    public void The_tick_is_whole_paise_or_none(string text, int? paise)
    {
        OptionRowCollector.TickPaise(text).ShouldBe(paise);
    }

    [Theory]
    [InlineData("W", "week")]
    [InlineData("M", "month")]
    [InlineData("N", null)]
    [InlineData("", null)]
    public async Task The_expiry_flag_gives_the_kind_or_null(string flag, string? kind)
    {
        var row = OptionMasterRows.With(OptionMasterRows.NiftyWeeklyCall, 16, flag);

        var parse = await ParseAsync(OptionMasterRows.Master(row), NiftyOnly);

        parse.Options!.Rows.Single().ExpiryKind.ShouldBe(kind);
    }

    [Fact]
    public async Task A_quoted_option_row_is_read_like_the_others()
    {
        var quoted = OptionMasterRows.With(OptionMasterRows.NiftyWeeklyCall, 8, "\"NIFTY 09 NOV 17350 CALL, weekly\"");

        var parse = await ParseAsync(OptionMasterRows.Master(quoted), NiftyOnly);

        parse.Options!.Rows.ShouldHaveSingleItem().SecurityId.ShouldBe(35008);
    }

    [Fact]
    public async Task An_option_row_with_a_wrong_number_of_columns_is_invalid_and_leaves_the_master_list_alone()
    {
        var parse = await ParseAsync(OptionMasterRows.Master(OptionMasterRows.NiftyWeeklyCall + "extra,"), Listed);

        parse.Options!.Invalid.ShouldBe(1);
        parse.Warnings.ShouldBeEmpty();
        parse.Protected.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_exchange_test_symbol_is_not_kept()
    {
        var test = OptionMasterRows.With(OptionMasterRows.NiftyWeeklyCall, 7, "NSETEST-Nov2026-1-CE");

        var parse = await ParseAsync(OptionMasterRows.Master(test), NiftyOnly);

        parse.Options!.Rows.ShouldBeEmpty();
        parse.Options.SkippedNotListed.ShouldBe(1 + 3, "the test row and the three RELIANCE rows of the sample");
    }

    [Fact]
    public async Task A_header_without_an_option_column_reads_no_option_and_the_master_list_as_before()
    {
        var text = OptionMasterRows.Master().Replace("OPTION_TYPE,", "OPTION_KIND,", StringComparison.Ordinal);

        var parse = await ParseAsync(text, Listed);

        parse.Options.ShouldNotBeNull().Unavailable.ShouldNotBeNull().ShouldContain("OPTION_TYPE");
        parse.Options.Rows.ShouldBeEmpty();
        parse.Rows.Count.ShouldBe(63);
    }

    [Fact]
    public async Task An_empty_list_reads_no_option()
    {
        (await ParseAsync(OptionMasterRows.Master(), OptionUnderlyings.Empty)).Options.ShouldBeNull();
    }

    [Fact]
    public async Task The_file_is_read_once_front_to_back()
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(OptionMasterRows.Master());
        await using var stream = new ForwardOnlyStream(bytes);

        var parsed = await DhanMasterParser.ParseAsync(stream, null, Ct, Listed);

        parsed.IsSuccess.ShouldBeTrue();
        stream.BytesRead.ShouldBe(bytes.Length);
        parsed.Value.Options!.Rows.Count.ShouldBe(8);
    }

    /// <summary>A stream that can only be read forward once and counts what was read.</summary>
    private sealed class ForwardOnlyStream(byte[] content) : Stream
    {
        private int _position;

        public long BytesRead { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = Math.Min(count, content.Length - _position);
            Array.Copy(content, _position, buffer, offset, n);
            _position += n;
            BytesRead += n;
            return n;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
