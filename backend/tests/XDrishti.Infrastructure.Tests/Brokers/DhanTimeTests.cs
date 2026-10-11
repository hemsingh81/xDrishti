using XDrishti.Infrastructure.Brokers;

namespace XDrishti.Infrastructure.Tests.Brokers;

public sealed class DhanTimeTests
{
    private static readonly TimeSpan Ist = TimeSpan.FromMinutes(330);

    [Theory]
    [InlineData(" 2026-10-12T08:00:12 ", 2026, 10, 12, 8, 0, 12)]
    [InlineData("2026-10-12T08:00:00", 2026, 10, 12, 8, 0, 0)]
    [InlineData("2026-10-12T08:00:00.000", 2026, 10, 12, 8, 0, 0)]
    [InlineData("2026-10-12 08:00:00", 2026, 10, 12, 8, 0, 0)]
    public void Times_without_an_offset_are_India_time(string text, int year, int month, int day, int hour, int minute, int second) =>
        DhanTime.Parse(text).ShouldBe(new DateTimeOffset(year, month, day, hour, minute, second, Ist));

    [Theory]
    [InlineData("2026-10-12T02:30:00Z")]
    [InlineData("2026-10-12T08:00:00+05:30")]
    public void Times_with_an_offset_keep_it(string text) =>
        DhanTime.Parse(text).ShouldBe(new DateTimeOffset(2026, 10, 12, 2, 30, 0, TimeSpan.Zero));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("tomorrow")]
    [InlineData("30/03/2026 15:37")] // not the format of expiryTime
    [InlineData("1999-01-01T10:00:00")] // implausible
    public void Anything_else_is_unknown(string? text) => DhanTime.Parse(text).ShouldBeNull();
}
