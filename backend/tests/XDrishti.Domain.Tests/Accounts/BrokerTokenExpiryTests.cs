using XDrishti.Domain.Accounts;

namespace XDrishti.Domain.Tests.Accounts;

/// <summary>Dhan tokens last until the time Dhan states, else 24 hours (ADR 0008, 0009).</summary>
public sealed class BrokerTokenExpiryTests
{
    private static readonly TimeSpan Ist = TimeSpan.FromMinutes(330);

    private static DateTimeOffset India(int day, int hour, int minute) => new(2026, 10, day, hour, minute, 0, Ist);

    [Fact]
    public void A_Dhan_token_lasts_as_Dhan_states_or_else_24_hours()
    {
        var issued = India(11, 9, 0);

        BrokerTokenExpiry.Of(issued, India(12, 8, 0)).ShouldBe(India(12, 8, 0));
        BrokerTokenExpiry.Of(issued, India(12, 8, 0)).Offset.ShouldBe(TimeSpan.Zero);
        BrokerTokenExpiry.Of(issued, null).ShouldBe(India(12, 9, 0));
        BrokerTokenExpiry.Of(issued, India(11, 8, 0)).ShouldBe(India(12, 9, 0)); // a stated time in the past is not believed
    }

    [Fact]
    public void The_Indian_day_runs_from_midnight_to_midnight_IST()
    {
        var (start, end) = BrokerTokenExpiry.IndianDay(new DateTimeOffset(2026, 10, 11, 19, 0, 0, TimeSpan.Zero)); // 00:30 IST on the 12th

        start.ShouldBe(India(12, 0, 0));
        end.ShouldBe(India(13, 0, 0));
        start.Offset.ShouldBe(TimeSpan.Zero);
    }
}
