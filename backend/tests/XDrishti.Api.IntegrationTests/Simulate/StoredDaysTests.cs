using System.Runtime.Versioning;
using XDrishti.Api.Simulate.Assist;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Market.Actions;
using XDrishti.Application.Market.Tracking;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Actions;
using XDrishti.SimReader;

namespace XDrishti.Api.IntegrationTests.Simulate;

/// <summary>P3-09e: the grouping of a range of stored minutes into one session per India date.</summary>
[UnsupportedOSPlatform("windows")]
public sealed class StoredDaysTests
{
    private static ReaderBar At(string utc) => new(DateTimeOffset.Parse(utc, System.Globalization.CultureInfo.InvariantCulture).ToUnixTimeSeconds(), 1m, 1m, 1m, 1m, 1m);

    [Fact]
    public void Minutes_are_grouped_by_their_India_date_not_the_UTC_date_and_the_sessions_ascend()
    {
        // 2026-03-02 09:15 IST = 03:45 UTC; 15:29 IST = 09:59 UTC; 2026-03-01 18:30 UTC is 2026-03-02 00:00 IST; 2026-03-03 09:15 IST = 03:45 UTC
        var bars = new[] { At("2026-03-01T18:30:00Z"), At("2026-03-02T03:45:00Z"), At("2026-03-02T09:59:00Z"), At("2026-03-03T03:45:00Z") };

        var sessions = StoredDays.Sessions(bars);

        sessions.Select(s => s.Date).ShouldBe([new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 3)]);
        sessions[0].Bars.Count.ShouldBe(3);
        sessions[1].Bars.Count.ShouldBe(1);
    }

    [Fact]
    public void No_minutes_give_no_session()
    {
        StoredDays.Sessions([]).ShouldBeEmpty();
    }

    private sealed class Factors(params DateOnly[] exDates) : IAdjustmentFactors
    {
        public ValueTask<AdjustmentFactors> GetAsync(Guid instrumentId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(AdjustmentFactors.From(exDates.Select(d => new AppliedAction(d, CorporateActionKind.Split, 1, 5, 0.2m))));
    }

    private sealed class NothingResolves : IQueryHandler<ResolveInstrumentsQuery, Result<ResolveResult>>
    {
        public ValueTask<Result<ResolveResult>> HandleAsync(ResolveInstrumentsQuery query, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Result.Failure<ResolveResult>(Error.NotFound("test.none", "no such instrument")));
    }

    private static readonly DateOnly Day = new(2026, 3, 2);

    private static readonly DateOnly From = Day.AddDays(-130);

    /// <summary>The caveat of a VWAP_PULLBACK row: the range ends the day before the trade day, the last date used is the trade day.</summary>
    private static Task<string?> Vwap(DateOnly? exDate, DateOnly? admittedSeam = null) =>
        StoredDays.CaveatAsync(exDate is { } d ? new Factors(d) : new Factors(), Guid.NewGuid(), From, Day.AddDays(-1), Day, admittedSeam, TestContext.Current.CancellationToken);

    [Fact]
    public async Task An_applied_action_inside_the_range_is_a_caveat()
    {
        (await Vwap(Day.AddDays(-10))).ShouldNotBeNull().ShouldContain("corporate action");
    }

    [Fact]
    public async Task An_ex_date_equal_to_the_first_date_is_no_caveat_and_one_equal_to_the_trade_day_is()
    {
        (await Vwap(From)).ShouldBeNull();
        (await Vwap(Day)).ShouldNotBeNull();
    }

    [Fact]
    public async Task An_ex_date_after_the_last_used_date_is_no_caveat_because_it_scales_the_whole_range_alike()
    {
        (await Vwap(Day.AddDays(1))).ShouldBeNull();
        (await Vwap(null)).ShouldBeNull();
    }

    [Fact]
    public async Task A_seam_is_a_caveat_only_when_the_run_admitted_pre_seam_bars_from_it()
    {
        // admission off: the run record has no seam line, so the engine and the reader see the same cut bars
        (await Vwap(null, admittedSeam: null)).ShouldBeNull();
        // admitted: the reader drops the bars before the seam and the run keeps them, so they differ whenever the seam is after the first loaded date, wherever it falls
        (await Vwap(null, admittedSeam: Day.AddDays(-30))).ShouldNotBeNull().ShouldContain("pre-seam");
        (await Vwap(null, admittedSeam: Day)).ShouldNotBeNull();
        (await Vwap(null, admittedSeam: Day.AddDays(5))).ShouldNotBeNull(); // after the range
        (await Vwap(null, admittedSeam: From)).ShouldBeNull(); // nothing is cut
        (await Vwap(null, admittedSeam: From.AddDays(-1))).ShouldBeNull();
    }

    [Fact]
    public async Task An_unknown_symbol_has_no_caveat_and_no_session()
    {
        var days = new StoredDays(new NothingResolves(), null!, null!, null!, new Factors(Day.AddDays(-10)));

        var range = await days.GetRangeAsync("NSE:NOSUCH", From, Day.AddDays(-1), Day, Day.AddDays(-30), TestContext.Current.CancellationToken);

        range.Caveat.ShouldBeNull();
        range.Sessions.ShouldBeEmpty();
    }
}
