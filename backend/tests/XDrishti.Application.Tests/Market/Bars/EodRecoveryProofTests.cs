using NSubstitute;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.Quality;
using XDrishti.Application.Market.Bars.Eod;
using XDrishti.Application.Market.Quality;
using XDrishti.Application.Tests.Market.Calendar;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Tests.Market.Bars;

/// <summary>The proof of a recovered end-of-day step (spec AU-03) over fakes: five missed sessions Mon 2026-10-05 .. Fri 2026-10-09, one instrument unless said.</summary>
public sealed class EodRecoveryProofTests
{
    private static readonly DateOnly[] Week = [new(2026, 10, 5), new(2026, 10, 6), new(2026, 10, 7), new(2026, 10, 8), new(2026, 10, 9)];
    private static readonly DateTimeOffset RunStart = new(2026, 10, 12, 10, 20, 0, TimeSpan.Zero);

    private readonly FakeFetchTargets _targets = new();
    private readonly FakeBarStore _bars = new();
    private readonly FakeBarGaps _gaps = new();
    private readonly IBarAggregates _aggregates = Substitute.For<IBarAggregates>();
    private readonly IQualityQueries _quality = Substitute.For<IQualityQueries>();
    private readonly FakeBarDayCounts _counts;
    private readonly FakeDataSettings _settings = new();

    public EodRecoveryProofTests()
    {
        _counts = new FakeBarDayCounts(_bars);
        _aggregates.ListDirtyAsync(Arg.Any<CancellationToken>()).Returns(new List<DirtyInstrument>());
        QualityRan(true);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private void QualityRan(bool succeeded) =>
        _quality.GetLastFinishedFullRunAsync(Arg.Any<CancellationToken>()).Returns(new QualityRunView(Guid.NewGuid(), "daily", succeeded ? "succeeded" : "failed", RunStart, RunStart.AddMinutes(5), 5, 1, 1, 0, 0, 100, null, null, null));

    private Guid Instrument(string symbol = "ABB")
    {
        var id = Guid.CreateVersion7();
        _targets.Targets.Add(new FetchTarget(id, "NSE:" + symbol, "share", "1", "NSE_EQ", null, new DateTimeOffset(2026, 9, 1, 9, 15, 0, TimeSpan.FromMinutes(330))));
        return id;
    }

    private void Bars(Guid id, params (DateOnly Day, int Count)[] days) => _counts.Preset[id] = days.ToDictionary(d => d.Day, d => d.Count);

    private ValueTask<XDrishti.Application.Abstractions.Platform.RecoveryProofResult> ProveAsync() =>
        new EodRecoveryProof(_targets, _counts, _gaps, _aggregates, _quality, new FixedCalendar(), _settings).ProveAsync(Week, RunStart, Ct);

    [Fact]
    public async Task Five_missed_sessions_that_all_have_their_bars_are_five_recovered_sessions()
    {
        Bars(Instrument(), [.. Week.Select(d => (d, 375))]);

        var result = await ProveAsync();

        result.AllRecovered.ShouldBeTrue();
        result.Reason.ShouldBeNull();
        result.Detail.Split("\"state\":\"recovered\"").Length.ShouldBe(6);
    }

    [Fact]
    public async Task A_run_that_fills_three_of_five_is_partial_with_the_two_missing_sessions_named_by_date()
    {
        Bars(Instrument(), (Week[0], 375), (Week[1], 375), (Week[2], 375), (Week[3], 100));

        var result = await ProveAsync();

        result.AllRecovered.ShouldBeFalse();
        result.Reason.ShouldBe("3 of 5 sessions recovered; not recovered: 2026-10-08 still_missing, 2026-10-09 still_missing");
        result.Detail.ShouldContain("\"date\":\"2026-10-09\",\"state\":\"still_missing\"");
    }

    [Fact]
    public async Task A_session_unfillable_in_the_gap_store_is_reported_as_unfillable_never_recovered()
    {
        var id = Instrument();
        Bars(id, [.. Week.Select(d => (d, 375))]);
        var gap = BarGap.Detect(id, Week[2], 375, 200, RunStart);
        gap.RecordRefetch(200, 200, RunStart);
        _gaps.Rows[(id, Week[2])] = gap;

        var result = await ProveAsync();

        result.AllRecovered.ShouldBeFalse();
        result.Reason.ShouldBe("4 of 5 sessions recovered; not recovered: 2026-10-07 unfillable");
    }

    [Fact]
    public async Task A_session_with_no_bars_is_still_missing_whatever_the_run_reported()
    {
        Bars(Instrument(), [.. Week.Where(d => d != Week[1]).Select(d => (d, 375))]);

        var result = await ProveAsync();

        result.Reason.ShouldNotBeNull().ShouldContain("2026-10-06 still_missing");
    }

    [Fact]
    public async Task Bars_whose_aggregates_are_not_built_are_not_recovered()
    {
        var id = Instrument();
        Bars(id, [.. Week.Select(d => (d, 375))]);
        var dayStart = new DateTimeOffset(2026, 10, 7, 9, 15, 0, TimeSpan.FromMinutes(330));
        _aggregates.ListDirtyAsync(Arg.Any<CancellationToken>()).Returns(new List<DirtyInstrument> { new(id, "ABB", dayStart, dayStart.AddMinutes(374)) });

        (await ProveAsync()).Reason.ShouldBe("4 of 5 sessions recovered; not recovered: 2026-10-07 still_missing");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Without_a_succeeded_quality_run_after_the_run_started_no_session_is_recovered(bool stale)
    {
        Bars(Instrument(), [.. Week.Select(d => (d, 375))]);
        if (stale)
        {
            _quality.GetLastFinishedFullRunAsync(Arg.Any<CancellationToken>()).Returns(new QualityRunView(Guid.NewGuid(), "daily", "succeeded", RunStart.AddDays(-1), RunStart.AddDays(-1), 5, 1, 1, 0, 0, 100, null, null, null));
        }
        else
        {
            QualityRan(false);
        }

        var result = await ProveAsync();

        result.AllRecovered.ShouldBeFalse();
        result.Detail.ShouldContain("\"qualityVerified\":false");
    }

    [Fact]
    public async Task Nothing_to_judge_is_not_a_proof_and_the_read_is_one_bounded_range_per_instrument()
    {
        (await ProveAsync()).AllRecovered.ShouldBeFalse();

        var a = Instrument("A");
        var b = Instrument("B");
        Bars(a, [.. Week.Select(d => (d, 375))]);
        Bars(b, [.. Week.Select(d => (d, 375))]);
        await ProveAsync();

        _counts.Asked.ShouldBe([(a, Week[0], Week[4]), (b, Week[0], Week[4])]);
    }

    [Fact]
    public async Task One_thin_instrument_and_one_unfillable_one_among_full_ones_are_served_but_a_real_hole_is_still_missing()
    {
        var thin = Instrument("THIN");
        var dead = Instrument("DEAD");
        var hole = Instrument("HOLE");
        var full = Instrument("FULL");
        Bars(full, [.. Week.Select(d => (d, 375))]);
        Bars(thin, [.. Week.Select(d => (d, 373))]); // short by 2 of 375: within the 1 % of the quality check
        Bars(dead, [.. Week.Select(d => (d, 100))]);
        foreach (var day in Week)
        {
            var gap = BarGap.Detect(dead, day, 375, 100, RunStart);
            gap.RecordRefetch(100, 100, RunStart);
            _gaps.Rows[(dead, day)] = gap;
        }

        Bars(hole, [.. Week.Where(d => d != Week[4]).Select(d => (d, 375)), (Week[4], 200)]); // a real hole on the last day

        var result = await ProveAsync();

        result.Reason.ShouldBe("4 of 5 sessions recovered; not recovered: 2026-10-09 still_missing");
        result.Detail.ShouldContain("\"complete\":3,\"unfillable\":1,\"missing\":0");
    }

    [Fact]
    public async Task A_session_in_which_every_instrument_is_unfillable_does_not_read_as_recovered()
    {
        var dead = Instrument("DEAD");
        Bars(dead, [.. Week.Select(d => (d, 100))]);
        foreach (var day in Week)
        {
            var gap = BarGap.Detect(dead, day, 375, 100, RunStart);
            gap.RecordRefetch(100, 100, RunStart);
            _gaps.Rows[(dead, day)] = gap;
        }

        var result = await ProveAsync();

        result.AllRecovered.ShouldBeFalse();
        result.Reason.ShouldNotBeNull().ShouldContain("2026-10-05 unfillable");
    }
}
