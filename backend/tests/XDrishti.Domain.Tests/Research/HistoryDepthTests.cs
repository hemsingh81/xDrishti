using XDrishti.Domain.Research;
using XDrishti.Tests.Shared;

namespace XDrishti.Domain.Tests.Research;

public sealed class HistoryDepthTests
{
    private static readonly DateOnly End = new(2026, 4, 30);

    private static SimulationRun Run(DateOnly? to = null, UniverseBasisRecord? basis = null, params InstrumentRun[] instruments)
    {
        var run = FabricatedRun.Assemble(FabricatedRun.List((FabricatedRun.IdA, "NSE:AAA"), (FabricatedRun.IdB, "NSE:BBB")), instruments, to: to ?? End);
        return run with { UniverseBasis = basis };
    }

    private static InstrumentRun[] Two(DateOnly a, DateOnly b) => [FabricatedRun.Ran(FabricatedRun.IdA, "NSE:AAA", a, End), FabricatedRun.Ran(FabricatedRun.IdB, "NSE:BBB", b, End)];

    [Fact]
    public void A_run_with_no_instrument_of_three_years_has_no_depth_or_survivorship_line_so_old_labels_are_unchanged()
    {
        var run = Run(instruments: Two(new DateOnly(2024, 4, 1), new DateOnly(2024, 12, 18)));

        HistoryDepth.Lines(run).ShouldBeEmpty();
        RunLabels.Of(run, FillRules.Default).Lines.ShouldNotContain(l => l.StartsWith("depth:", StringComparison.Ordinal) || l.StartsWith("survivorship:", StringComparison.Ordinal));
    }

    [Fact]
    public void A_backfilled_run_counts_instruments_by_depth_bucket_and_says_where_the_history_starts()
    {
        var run = Run(instruments: Two(new DateOnly(2021, 5, 3), new DateOnly(2024, 12, 18)));

        var lines = HistoryDepth.Lines(run);

        lines[0].ShouldBe("depth: history from 2021-05-03; instruments by years of stored bars: under 2 1, 2 to 3 0, 3 to 5 1, 5 or more 0 (per instrument: firstBar in the record)");
        RunLabels.Of(run, FillRules.Default).Lines.ShouldContain(lines[0]);
    }

    [Fact]
    public void The_survivorship_line_is_the_honest_snapshot_text_and_never_says_point_in_time()
    {
        var run = Run(instruments: Two(new DateOnly(2021, 5, 3), new DateOnly(2022, 1, 3)));

        var lines = HistoryDepth.Lines(run);

        lines[1].ShouldBe("survivorship: members as of today; instruments delisted in the window are absent (Dhan serves active instruments only; ISS-22)");
        lines.ShouldAllBe(l => !l.Contains("point in time", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_dated_basis_changes_the_survivorship_line_and_still_admits_the_minute_data_limit()
    {
        var dated = new UniverseBasisRecord(Guid.NewGuid(), null, null, UniverseBasisRecord.Dated, "abc");
        var run = Run(basis: dated, instruments: Two(new DateOnly(2021, 5, 3), new DateOnly(2022, 1, 3)));

        HistoryDepth.Lines(run)[1].ShouldBe(HistoryDepth.SurvivorshipDated);
        HistoryDepth.SurvivorshipDated.ShouldContain("no 1-minute bars");
    }

    [Fact]
    public void Depth_never_reaches_past_the_last_date_of_the_run()
    {
        // The last bar read is 2026-04-30 but the run ends 2023-05-03: a bar after the window is not depth.
        var run = Run(to: new DateOnly(2023, 5, 3), instruments: Two(new DateOnly(2021, 5, 3), new DateOnly(2021, 5, 3)));

        HistoryDepth.YearsOf(run).ShouldAllBe(y => y < 2.01 && y > 1.99);
        HistoryDepth.Lines(run).ShouldBeEmpty();
    }

    [Fact]
    public void Skipped_instruments_are_not_counted()
    {
        var run = Run(instruments: [FabricatedRun.Ran(FabricatedRun.IdA, "NSE:AAA", new DateOnly(2021, 5, 3), End), FabricatedRun.Skipped(FabricatedRun.IdB, "NSE:BBB", SkipReason.NoBars)]);

        HistoryDepth.YearsOf(run).Count.ShouldBe(1);
    }

    [Theory]
    [InlineData(1.9, 0)]
    [InlineData(2.0, 1)]
    [InlineData(3.0, 2)]
    [InlineData(5.0, 3)]
    public void Bucket_edges_are_lower_inclusive(double years, int bucket)
    {
        var (a, b, c, d) = HistoryDepth.Buckets([years]);

        new[] { a, b, c, d }[bucket].ShouldBe(1);
        (a + b + c + d).ShouldBe(1);
    }
}
