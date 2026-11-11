using XDrishti.Domain.Market.History;

namespace XDrishti.Domain.Tests.Market.History;

/// <summary>CF-04 Part B criterion 3: the chunk plan (half-open windows, newest first, at most 90 days) and the property that the windows tile the interval exactly.</summary>
public sealed class BackfillPlannerTests
{
    private static readonly DateOnly Today = new(2026, 10, 27);

    [Fact]
    public void A_first_csv_bar_of_2024_04_01_gives_ten_chunks_newest_first_and_the_oldest_is_77_days()
    {
        var chunks = BackfillPlanner.Chunks(new DateOnly(2024, 4, 1), Today, 5, 90);

        chunks.Count.ShouldBe(10);
        chunks[0].ShouldBe(new BackfillChunk(new DateOnly(2024, 1, 2), new DateOnly(2024, 4, 1)));
        chunks[0].Days.ShouldBe(90);
        chunks[^1].From.ShouldBe(new DateOnly(2021, 10, 27));
        chunks[^1].Days.ShouldBe(77);
    }

    [Fact]
    public void A_first_csv_bar_of_2024_12_18_gives_thirteen_chunks_and_the_two_instruments_need_23_requests()
    {
        var chunks = BackfillPlanner.Chunks(new DateOnly(2024, 12, 18), Today, 5, 90);

        chunks.Count.ShouldBe(13);
        chunks[^1].Days.ShouldBe(68);
        (chunks.Count + BackfillPlanner.Chunks(new DateOnly(2024, 4, 1), Today, 5, 90).Count).ShouldBe(23);
    }

    [Fact]
    public void A_first_csv_bar_on_or_before_the_start_gives_no_chunk_and_one_day_after_it_gives_one()
    {
        BackfillPlanner.Chunks(new DateOnly(2021, 10, 27), Today, 5, 90).ShouldBeEmpty();
        BackfillPlanner.Chunks(new DateOnly(2020, 1, 1), Today, 5, 90).ShouldBeEmpty();
        BackfillPlanner.Chunks(new DateOnly(2021, 10, 28), Today, 5, 90).ShouldBe([new BackfillChunk(new DateOnly(2021, 10, 27), new DateOnly(2021, 10, 28))]);
    }

    [Theory]
    [InlineData(0, 90)]
    [InlineData(6, 90)]
    [InlineData(5, 6)]
    [InlineData(5, 91)]
    public void Out_of_range_settings_are_refused(int years, int chunkDays) =>
        Should.Throw<ArgumentOutOfRangeException>(() => BackfillPlanner.Chunks(new DateOnly(2024, 4, 1), Today, years, chunkDays));

    [Fact]
    public void The_windows_tile_the_interval_with_no_gap_no_overlap_and_none_longer_than_the_limit()
    {
        var random = new Random(7);
        for (var i = 0; i < 200; i++)
        {
            var seam = Today.AddDays(-random.Next(1, 2_000));
            var chunkDays = random.Next(7, 91);

            var chunks = BackfillPlanner.Chunks(seam, Today, 5, chunkDays);

            var start = BackfillPlanner.StartOf(Today, 5);
            if (seam <= start)
            {
                chunks.ShouldBeEmpty();
                continue;
            }

            chunks[0].To.ShouldBe(seam);
            chunks[^1].From.ShouldBe(start);
            chunks.Sum(c => c.Days).ShouldBe(seam.DayNumber - start.DayNumber);
            chunks.ShouldAllBe(c => c.Days >= 1 && c.Days <= chunkDays);
            for (var k = 1; k < chunks.Count; k++)
            {
                chunks[k].To.ShouldBe(chunks[k - 1].From);
            }
        }
    }
}
