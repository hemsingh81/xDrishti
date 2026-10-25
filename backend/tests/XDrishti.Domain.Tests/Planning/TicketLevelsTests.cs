using XDrishti.Domain.Planning;

namespace XDrishti.Domain.Tests.Planning;

public sealed class TicketLevelsTests
{
    private static readonly DateOnly D = new(2026, 10, 23);

    [Fact]
    public void A_buy_has_the_stop_below_and_the_target_above_and_values_round_to_four_decimals()
    {
        var r = TicketLevels.Create("BUY", 100.00005m, D, 95m, StopAnchor.Level, 110m, 2m);

        Assert.True(r.IsSuccess);
        Assert.Equal(100.0001m, r.Value.EntryRef);
        Assert.Equal(StopAnchor.Level, r.Value.Anchor);
    }

    [Fact]
    public void A_sell_has_the_stop_above_and_the_target_below()
    {
        Assert.True(TicketLevels.Create("SELL", 100m, D, 105m, StopAnchor.CloseOfD, 90m, 2m).IsSuccess);
        Assert.True(TicketLevels.Create("SELL", 100m, D, 95m, StopAnchor.CloseOfD, null, null).IsFailure);
        Assert.True(TicketLevels.Create("SELL", 100m, D, 105m, StopAnchor.CloseOfD, 110m, 2m).IsFailure);
    }

    [Theory]
    [InlineData(100, 100)]
    [InlineData(100, 60)]
    [InlineData(100, 0)]
    public void A_zero_or_implausible_stop_distance_is_refused_with_its_code(int entry, int stop)
    {
        var r = TicketLevels.Create("BUY", entry, D, stop, StopAnchor.Level, null, null);

        Assert.Equal(TicketLevels.ImplausibleCode, r.Error.Code);
    }

    [Fact]
    public void A_stop_exactly_at_the_cap_is_accepted_and_an_entry_alone_is_valid()
    {
        Assert.True(TicketLevels.Create("BUY", 100m, D, 70m, StopAnchor.Level, null, null).IsSuccess);
        Assert.Null(TicketLevels.Create("BUY", 100m, D, null, null, null, null).Value.Stop);
    }

    [Fact]
    public void Inconsistent_arguments_are_invalid()
    {
        Assert.True(TicketLevels.Create("BUY", 100m, D, 95m, null, null, null).IsFailure);
        Assert.True(TicketLevels.Create("BUY", 100m, D, null, null, 110m, 2m).IsFailure);
        Assert.True(TicketLevels.Create("HOLD", 100m, D, null, null, null, null).IsFailure);
    }
}
