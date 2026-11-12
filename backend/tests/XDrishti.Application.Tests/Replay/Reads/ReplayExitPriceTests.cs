using XDrishti.Application.Replay.Reads;

namespace XDrishti.Application.Tests.Replay.Reads;

/// <summary>SR-02, D4: the exit price is derived as entry + gross result / (quantity x side).</summary>
public sealed class ReplayExitPriceTests
{
    [Theory]
    [InlineData("BUY", 100, 500.0, 2000.0, 520.0)]
    [InlineData("BUY", 100, 500.0, -1000.0, 490.0)]
    [InlineData("SELL", 100, 500.0, 2000.0, 480.0)]
    [InlineData("SELL", 250, 500.0, -2500.0, 510.0)]
    public void The_exit_follows_from_the_entry_the_quantity_and_the_gross_result(string side, int qty, double entry, double gross, double expected)
    {
        ReplayExitPrice.Derive(side, qty, (decimal)entry, (decimal)gross).ShouldBe((decimal)expected);
    }
}
