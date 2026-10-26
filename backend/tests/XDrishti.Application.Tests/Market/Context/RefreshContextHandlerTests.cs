using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using XDrishti.Application.Abstractions.Market.Context;
using XDrishti.Application.Market.Context;
using XDrishti.Application.Tests.Market.Calendar;
using XDrishti.Domain.Market.Context;

namespace XDrishti.Application.Tests.Market.Context;

/// <summary>P3-01d criteria 2, 3 and 8 at the level of the use case: what is written, what waits, and what is named when a prerequisite is missing.</summary>
public sealed class RefreshContextHandlerTests
{
    private static readonly DateOnly S1 = new(2026, 3, 2);

    private static readonly DateOnly[] Sessions = [S1, S1.AddDays(1), S1.AddDays(2), S1.AddDays(3), S1.AddDays(4), S1.AddDays(7)];

    private static readonly ContextInstrument Nifty = new(Guid.NewGuid(), "NSE:NIFTY");

    private static readonly ContextInstrument Vix = new(Guid.NewGuid(), "NSE:INDIA_VIX");

    private readonly FakeContextSettings _settings = new() { VixGraceSessions = 2 };

    private readonly FakeContextBarSource _bars = new();

    private readonly FakeContextDayStore _store = new();

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 3, 9, 16, 30, 0, TimeSpan.FromMinutes(330)));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private RefreshContextHandler Handler => HandlerAt(_clock);

    private RefreshContextHandler HandlerAt(TimeProvider clock) => new(_settings, _bars, _store, new FixedCalendar(), clock, NullLogger<RefreshContextHandler>.Instance);

    private static DailyLeg Leg(DateOnly date, double close, int bars = 375) => new(date, close + 5, close - 5, close, bars, bars == 375);

    private void Fixture() => _bars.Legs = new ContextLegs(
        Nifty,
        Vix,
        [.. Sessions.Select((d, i) => Leg(d, 22_000 + i))],
        [Leg(Sessions[0], 15), Leg(Sessions[2], 16), Leg(Sessions[3], 14)]);

    [Fact]
    public async Task The_hand_fixture_writes_four_days_leaves_two_waiting_and_remembers_the_index_instrument()
    {
        Fixture();

        var result = await Handler.HandleAsync(new RefreshContextCommand(), Ct);

        result.Value.ShouldBe(new ContextRefreshed(4, 2, 0, Sessions[3], null, null));
        _store.Rows.Select(r => r.Date).ShouldBe(Sessions[..4]);
        _store.InstrumentOfLastInsert.ShouldBe(Nifty.Id);
    }

    [Fact]
    public async Task A_second_run_writes_nothing_and_leaves_the_stored_rows_alone()
    {
        Fixture();
        await Handler.HandleAsync(new RefreshContextCommand(), Ct);
        var before = _store.Rows.ToList();

        var again = await Handler.HandleAsync(new RefreshContextCommand(), Ct);

        again.Value.Written.ShouldBe(0);
        _store.Rows.ShouldBe(before);
    }

    [Fact]
    public async Task A_late_vix_leg_is_used_while_the_day_still_waits()
    {
        Fixture();
        await Handler.HandleAsync(new RefreshContextCommand(), Ct);
        _bars.Legs = _bars.Legs with { VixLegs = [.. _bars.Legs.VixLegs, Leg(Sessions[4], 13.5)] };

        var result = await Handler.HandleAsync(new RefreshContextCommand(), Ct);

        result.Value.ShouldBe(new ContextRefreshed(1, 1, 0, Sessions[4], null, null));
        _store.Rows.Single(r => r.Date == Sessions[4]).VixClose.ShouldBe(13.5);
    }

    [Theory]
    [InlineData(true, "index_not_tracked")]
    [InlineData(false, "vix_not_tracked")]
    public async Task A_missing_index_names_the_prerequisite_with_its_fix_and_writes_nothing(bool indexMissing, string code)
    {
        Fixture();
        _bars.Legs = indexMissing ? _bars.Legs with { Index = null } : _bars.Legs with { Vix = null };

        var result = await Handler.HandleAsync(new RefreshContextCommand(), Ct);

        result.IsSuccess.ShouldBeTrue("a missing prerequisite never fails the job");
        result.Value.Missing.ShouldBe(code);
        result.Value.Fix!.ShouldContain("Instruments screen");
        result.Value.Written.ShouldBe(0);
        _store.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_disabled_job_reads_nothing()
    {
        Fixture();
        _settings.Enabled = false;

        var result = await Handler.HandleAsync(new RefreshContextCommand(), Ct);

        result.Value.Missing.ShouldBe("disabled");
        _bars.Reads.ShouldBe(0);
        _store.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_running_day_is_not_written_before_the_session_has_closed()
    {
        Fixture();
        var midSession = new FakeTimeProvider(new DateTimeOffset(2026, 3, 9, 14, 0, 0, TimeSpan.FromMinutes(330)));

        await HandlerAt(midSession).HandleAsync(new RefreshContextCommand(), Ct);

        _store.Rows.ShouldNotContain(r => r.Date == Sessions[5]);
    }
}
