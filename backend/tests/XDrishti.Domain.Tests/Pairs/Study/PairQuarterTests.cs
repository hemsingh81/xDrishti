using XDrishti.Domain.PairResearch;

namespace XDrishti.Domain.Tests.Pairs.Study;

public sealed class PairQuarterTests
{
    [Theory]
    [InlineData(2025, 1, 1, 2025, 1, 1)]
    [InlineData(2025, 3, 31, 2025, 1, 1)]
    [InlineData(2025, 4, 1, 2025, 4, 1)]
    [InlineData(2025, 11, 15, 2025, 10, 1)]
    public void The_quarter_starts_in_january_april_july_or_october(int y, int m, int d, int sy, int sm, int sd)
    {
        PairQuarter.StartOf(new DateOnly(y, m, d)).ShouldBe(new DateOnly(sy, sm, sd));
    }

    [Fact]
    public void A_range_lists_every_quarter_it_touches_and_a_quarter_ends_on_its_last_day()
    {
        PairQuarter.Between(new DateOnly(2025, 2, 10), new DateOnly(2025, 7, 1)).ShouldBe([new DateOnly(2025, 1, 1), new DateOnly(2025, 4, 1), new DateOnly(2025, 7, 1)]);
        PairQuarter.Between(new DateOnly(2025, 5, 1), new DateOnly(2025, 4, 1)).ShouldBeEmpty();
        PairQuarter.EndOf(new DateOnly(2025, 10, 1)).ShouldBe(new DateOnly(2025, 12, 31));
        PairQuarter.Label(new DateOnly(2025, 7, 1)).ShouldBe("2025Q3");
    }
}
