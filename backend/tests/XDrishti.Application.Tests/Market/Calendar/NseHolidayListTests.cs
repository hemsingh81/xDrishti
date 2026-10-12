using System.Text;
using XDrishti.Application.Market.Calendar;

namespace XDrishti.Application.Tests.Market.Calendar;

/// <summary>NSE's holiday list is read defensively: the format is not documented and may change (ADR 0017 §1).</summary>
public sealed class NseHolidayListTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Task<HolidayParse> ParseAsync(string json) => NseHolidayList.ParseAsync(new MemoryStream(Encoding.UTF8.GetBytes(json)), Ct).AsTask();

    [Fact]
    public async Task The_cash_market_entries_of_the_real_shape_are_read()
    {
        const string json = """
            {"CM":[{"Sequence":1,"tradingDate":"26-Jan-2026","weekDay":"Monday","description":"Republic Day","morning_session":"","evening_session":"","Sr_no":1},
                   {"Sequence":2,"tradingDate":"03-Mar-2026","weekDay":"Tuesday","description":"Holi","morning_session":"","evening_session":"","Sr_no":2}],
             "FO":[{"tradingDate":"01-Jan-2026","description":"Not this segment"}]}
            """;

        var parse = await ParseAsync(json);

        parse.Failure.ShouldBeNull();
        parse.Holidays.ShouldBe([new ListedHoliday(new DateOnly(2026, 1, 26), "Republic Day"), new ListedHoliday(new DateOnly(2026, 3, 3), "Holi")]);
        parse.Unreadable.ShouldBe(0);
    }

    [Fact]
    public async Task Names_are_matched_ignoring_case_and_iso_dates_are_accepted()
    {
        var parse = await ParseAsync("""{"cm":[{"TradingDate":"2026-10-02","Description":"Gandhi Jayanti"},{"tradingDate":" 5-Nov-2026 ","description":"x"}]}""");

        parse.Holidays.Select(h => h.Date).ShouldBe([new DateOnly(2026, 10, 2), new DateOnly(2026, 11, 5)]);
    }

    [Fact]
    public async Task Entries_that_cannot_be_read_are_counted_and_the_rest_is_kept()
    {
        var parse = await ParseAsync("""{"CM":[{"tradingDate":"31-Feb-2026","description":"impossible"},{"tradingDate":5},{"description":"no date"},"text",null,{"tradingDate":"15-Aug-2026","description":"Independence Day"}]}""");

        parse.Holidays.ShouldHaveSingleItem().Date.ShouldBe(new DateOnly(2026, 8, 15));
        parse.Unreadable.ShouldBe(5);
        parse.Failure.ShouldBeNull();
    }

    [Fact]
    public async Task A_date_listed_twice_counts_once_and_a_missing_description_is_empty()
    {
        var parse = await ParseAsync("""{"CM":[{"tradingDate":"15-Aug-2026","description":"first"},{"tradingDate":"15-Aug-2026","description":"second"},{"tradingDate":"02-Oct-2026"}]}""");

        parse.Holidays.ShouldBe([new ListedHoliday(new DateOnly(2026, 8, 15), "first"), new ListedHoliday(new DateOnly(2026, 10, 2), string.Empty)]);
    }

    [Fact]
    public async Task A_description_is_cleaned_and_cut()
    {
        var parse = await ParseAsync("{\"CM\":[{\"tradingDate\":\"15-Aug-2026\",\"description\":\"  Indep\\u0007endence " + new string('x', 300) + "\"}]}");

        var description = parse.Holidays.Single().Description;
        description.Length.ShouldBe(NseHolidayList.MaxDescriptionLength);
        description.ShouldStartWith("Independence xxx");
    }

    [Theory]
    [InlineData("{}", "no cash-market")]
    [InlineData("""{"FO":[]}""", "no cash-market")]
    [InlineData("""{"CM":"x"}""", "no cash-market")]
    [InlineData("""{"CM":{}}""", "no cash-market")]
    [InlineData("[]", "expected shape")]
    [InlineData("\"hello\"", "expected shape")]
    [InlineData("<html>Access Denied</html>", "not valid JSON")]
    [InlineData("", "not valid JSON")]
    [InlineData("{\"CM\":[", "not valid JSON")]
    public async Task Anything_else_is_a_failure_with_a_plain_message(string json, string expected)
    {
        var parse = await ParseAsync(json);

        parse.Failure.ShouldNotBeNull().ShouldContain(expected);
        parse.Holidays.ShouldBeEmpty();
    }

    [Fact]
    public async Task Very_deep_nesting_is_a_failure_not_a_crash()
    {
        var parse = await ParseAsync(new string('[', 200) + new string(']', 200));

        parse.Failure.ShouldNotBeNull();
    }

    [Fact]
    public async Task More_entries_than_the_cap_are_left_out()
    {
        var entries = string.Join(',', Enumerable.Range(0, NseHolidayList.MaxEntries + 3).Select(i => $"{{\"tradingDate\":\"{new DateOnly(2026, 1, 1).AddDays(i):yyyy-MM-dd}\",\"description\":\"d\"}}"));

        var parse = await ParseAsync($"{{\"CM\":[{entries}]}}");

        parse.Holidays.Count.ShouldBe(NseHolidayList.MaxEntries);
        parse.Unreadable.ShouldBe(3);
    }

    [Fact]
    public async Task A_download_that_breaks_off_is_a_failure_with_its_message()
    {
        var parse = await NseHolidayList.ParseAsync(new BrokenStream(), Ct);

        parse.Failure.ShouldBe("The download of NSE's holiday list was interrupted.");
    }

    private sealed class BrokenStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            throw new XDrishti.Application.Abstractions.Market.SourceReadException("The download of NSE's holiday list was interrupted.");

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
