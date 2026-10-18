using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Scanning;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Strategies.BuiltIn;

namespace XDrishti.Simulation.Tests.Scanning;

/// <summary>Criterion 17 of P3-03a (a and b): scanning allocates exactly 0 bytes in steady state (so no handle is looked up per step), and the work is one frame step per minute.</summary>
[Collection(ExclusiveRun.Name)]
public sealed class ScanAllocationTests
{
    private static readonly DateOnly[] Dates = [new(2026, 3, 2), new(2026, 3, 3), new(2026, 3, 4), new(2026, 3, 5), new(2026, 3, 6), new(2026, 3, 9), new(2026, 3, 10)];

    /// <summary>The F8 shape (a close equal to the level, so nothing fires) on consecutive dates.</summary>
    private static Bar[] Session(DateOnly date) => Day.P(date).Set("09:44 99.00/100.00/99.00/100.00").Bars();

    [Theory]
    [InlineData(Side.Buy)]
    [InlineData(Side.Sell)]
    public void Six_sessions_that_fire_nothing_allocate_nothing_after_one_warm_up_session(Side side)
    {
        var warmUp = Session(Dates[0]);
        var measured = Dates.Skip(1).Select(Session).ToArray();
        var minutes = measured.Sum(s => s.Length);
        EntryScanner? seen = null;

        AllocationProbe.ShouldAllocateNothing(
            window =>
            {
                var scanner = Scan.Scanner(Scan.Compile(OrbV1.Create(), side));
                foreach (var bar in warmUp)
                {
                    scanner.Step(bar);
                }

                window.Begin();
                foreach (var session in measured)
                {
                    for (var i = 0; i < session.Length; i++)
                    {
                        scanner.Step(session[i]);
                    }
                }

                window.End();
                seen = scanner;
            },
            "six sessions of the F8 shape through the scanner");

        seen!.Candidates.ShouldBeEmpty();
        seen.Counters.Bars.ShouldBe(warmUp.Length + minutes);
        seen.Counters.DecisionSteps.ShouldBe(7 * 25);
    }

    [Fact]
    public void The_frame_takes_one_step_per_minute_and_the_counts_of_each_item_add_up()
    {
        foreach (var fixture in Fixtures.All)
        {
            foreach (var scanner in Scan.Both(fixture.Definition, fixture.Bars, fixture.Timeframe))
            {
                scanner.Counters.Bars.ShouldBe(fixture.Bars.Length, fixture.Name);
                Identities.ShouldHold(scanner);
            }
        }
    }
}
