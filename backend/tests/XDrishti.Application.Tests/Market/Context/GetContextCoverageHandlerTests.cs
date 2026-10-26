using Microsoft.Extensions.Time.Testing;
using XDrishti.Application.Abstractions.Market.Context;
using XDrishti.Application.Market.Context;
using XDrishti.Application.Tests.Market.Calendar;
using XDrishti.Domain.Market.Context;

namespace XDrishti.Application.Tests.Market.Context;

/// <summary>P3-01d criteria 6, 7 and 8: the coverage names what is readable from when, how far behind the context is, what was skipped and what is missing.</summary>
public sealed class GetContextCoverageHandlerTests
{
    private static readonly DateOnly S1 = new(2026, 3, 2);

    private readonly FakeContextSettings _settings = new();

    private readonly FakeContextBarSource _bars = new();

    private readonly FakeContextDayStore _store = new();

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 3, 9, 16, 30, 0, TimeSpan.FromMinutes(330)));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private GetContextCoverageHandler Handler => new(_settings, _bars, _store, new FixedCalendar(), _clock);

    private static ContextDayRecord Day(DateOnly date) => new(date, 105, 95, 100, 15, 375, 375);

    private static DailyLeg Leg(DateOnly date, int bars = 375) => new(date, 105, 95, 100, bars, bars == 375);

    [Fact]
    public async Task An_empty_store_with_untracked_indices_names_the_first_missing_prerequisite()
    {
        var view = await Handler.HandleAsync(new GetContextCoverageQuery(), Ct);

        (view.Missing, view.Days, view.Stale, view.AgeSessions).ShouldBe(("index_not_tracked", 0, false, null));
        view.Fix.ShouldNotBeNull();
        view.Source.ShouldBe("derived from 1-minute bars; the official daily close can differ slightly");
    }

    [Fact]
    public async Task A_context_more_than_one_session_behind_is_stale_and_one_session_behind_is_current()
    {
        _store.Rows.AddRange([Day(S1), Day(S1.AddDays(1)), Day(S1.AddDays(2))]);

        var threeBehind = await Handler.HandleAsync(new GetContextCoverageQuery(), Ct);
        _store.Rows.Add(Day(S1.AddDays(3)));
        var twoBehind = await Handler.HandleAsync(new GetContextCoverageQuery(), Ct);
        _store.Rows.Add(Day(S1.AddDays(4)));
        var oneBehind = await Handler.HandleAsync(new GetContextCoverageQuery(), Ct);
        _store.Rows.Add(Day(S1.AddDays(7)));
        var current = await Handler.HandleAsync(new GetContextCoverageQuery(), Ct);

        (threeBehind.AgeSessions, threeBehind.Stale).ShouldBe((3, true));
        (twoBehind.AgeSessions, twoBehind.Stale).ShouldBe((2, true));
        (oneBehind.AgeSessions, oneBehind.Stale).ShouldBe((1, false));
        (current.AgeSessions, current.Stale, current.GapSessions).ShouldBe((0, false, 0));
    }

    [Fact]
    public async Task The_skipped_days_are_listed_with_reasons_up_to_fifty_and_counted_in_total()
    {
        var thin = Enumerable.Range(0, 60).Select(i => Leg(S1.AddDays(i * 7), 100)).ToList();
        _bars.Legs = new ContextLegs(new ContextInstrument(Guid.NewGuid(), "NSE:NIFTY"), new ContextInstrument(Guid.NewGuid(), "NSE:INDIA_VIX"), thin, []);
        _clock.SetUtcNow(new DateTimeOffset(2028, 3, 9, 16, 30, 0, TimeSpan.FromMinutes(330)));

        var view = await Handler.HandleAsync(new GetContextCoverageQuery(), Ct);

        view.Missing.ShouldBeNull();
        view.IndexSymbol.ShouldBe("NSE:NIFTY");
        view.SkippedTotal.ShouldBeGreaterThan(50);
        view.Skipped.Count.ShouldBe(50);
        view.Skipped[0].ShouldBe(new ContextSkippedView(S1, "index_thin", 100));
    }

    [Fact]
    public async Task A_disabled_job_says_so_and_reads_no_bars()
    {
        _settings.Enabled = false;

        var view = await Handler.HandleAsync(new GetContextCoverageQuery(), Ct);

        (view.Enabled, view.Missing).ShouldBe((false, "disabled"));
        _bars.Reads.ShouldBe(0);
    }

    [Fact]
    public async Task The_first_readable_dates_are_the_day_after_the_nth_record()
    {
        for (var i = 0; i < 56; i++)
        {
            _store.Rows.Add(Day(S1.AddDays(i)));
        }

        var view = await Handler.HandleAsync(new GetContextCoverageQuery(), Ct);

        view.IndexTrendReadableFrom.ShouldBe(S1.AddDays(55));
        view.RegimeReadableFrom.ShouldBeNull();
        view.VixDays.ShouldBe(56);
    }

    [Fact]
    public async Task The_breadth_of_the_latest_day_its_counts_the_thin_dates_and_the_honest_label_are_shown()
    {
        _store.Rows.AddRange([Day(S1), Day(S1.AddDays(1)), Day(S1.AddDays(2)), Day(S1.AddDays(3))]);
        _store.Breadth[S1] = new StoredBreadth(S1, null, 10, 214, 204, BreadthResult.Insufficient);
        _store.Breadth[S1.AddDays(1)] = new StoredBreadth(S1.AddDays(1), 27.5, 120, 214, 94, BreadthResult.ValueState);

        var view = await Handler.HandleAsync(new GetContextCoverageQuery(), Ct);

        var b = view.Breadth;
        (b.LatestDate, b.Latest, b.State, b.Voters, b.Universe, b.Excluded).ShouldBe((S1.AddDays(1), 27.5, "value", 120, 214, 94));
        (b.ComputedDays, b.PendingDays, b.InsufficientDays).ShouldBe((2, 2, 1));
        b.InsufficientDates.ShouldBe([S1], "the coverage names the dates");
        b.Label.ShouldBe("breadth of your 214 tracked shares (members as of today), not of the market");
    }

    [Fact]
    public async Task With_no_breadth_yet_the_view_says_none_and_the_label_names_no_count()
    {
        _store.Rows.Add(Day(S1));

        var view = await Handler.HandleAsync(new GetContextCoverageQuery(), Ct);

        (view.Breadth.State, view.Breadth.Latest, view.Breadth.PendingDays).ShouldBe((null, null, 1));
        view.Breadth.Label.ShouldBe("breadth of your tracked shares (members as of today), not of the market");
    }
}
