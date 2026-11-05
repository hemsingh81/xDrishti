using XDrishti.Domain.Learning;

namespace XDrishti.Domain.Tests.Learning;

/// <summary>CF-10 part 10b (ADR 0215), spec AC3: the events-per-variable cap counts the smaller class, not the trades.</summary>
public sealed class EventsPerVariableTests
{
    [Fact]
    public void One_hundred_and_twenty_trades_with_40_percent_wins_have_48_events_and_a_cap_of_3_at_15_and_4_at_10()
    {
        var events = EventsPerVariable.Events(wins: 48, losses: 72);

        events.ShouldBe(48);
        EventsPerVariable.Cap(events, 15, 8).ShouldBe(3);
        EventsPerVariable.Cap(events, 10, 8).ShouldBe(4);
    }

    [Fact]
    public void Two_hundred_trades_with_50_percent_wins_have_100_events_and_a_cap_of_6_at_15()
    {
        var events = EventsPerVariable.Events(100, 100);

        events.ShouldBe(100);
        EventsPerVariable.Cap(events, 15, 8).ShouldBe(6);
    }

    [Fact]
    public void The_events_are_the_smaller_class_whichever_it_is()
    {
        EventsPerVariable.Events(30, 90).ShouldBe(30);
        EventsPerVariable.Events(90, 30).ShouldBe(30);
    }

    [Theory]
    [InlineData(120, 8)]
    [InlineData(1000, 8)]
    [InlineData(14, 0)]
    [InlineData(15, 1)]
    [InlineData(0, 0)]
    public void The_cap_never_exceeds_the_limit_and_is_zero_with_fewer_events_than_one_variable_needs(int events, int expected)
    {
        EventsPerVariable.Cap(events, 15, 8).ShouldBe(expected);
    }

    [Fact]
    public void Nonsense_input_is_refused()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => EventsPerVariable.Cap(10, 0, 8));
        Should.Throw<ArgumentOutOfRangeException>(() => EventsPerVariable.Cap(-1, 15, 8));
        Should.Throw<ArgumentOutOfRangeException>(() => EventsPerVariable.Events(-1, 5));
    }
}
