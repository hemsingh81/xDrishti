using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Market.Bars.Pipeline;
using XDrishti.Application.Market.Calendar;
using XDrishti.Application.Tests.Market.Bars;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Application.Tests.Market.Calendar;

/// <summary>What follows every data job: NSE's list once a day (a failure is only a warning of the job) and the proposals from the stored bars.</summary>
public sealed class CalendarUpkeepTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 6, 30, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(Now);
    private readonly InMemoryCalendarData _data = new();
    private readonly FixedCalendar _calendar = new();
    private readonly FakeHolidaySource _source = new();
    private readonly FakeObservedDays _observed = new();
    private readonly FakeDataJobStore _jobs = new();
    private readonly HolidayFetchGate _gate = new();
    private readonly DataJob _job = DataJob.Start([DataSteps.Aggregate], false, "owner", Now);

    public CalendarUpkeepTests() => _jobs.Jobs[_job.Id] = _job;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private CalendarUpkeep Upkeep => new(
        new NseCalendarRefresher(_source, _data, _calendar, _data, _data, _clock, NullLogger<NseCalendarRefresher>.Instance),
        new ObservedCalendarRunner(_observed, _data, _calendar, _data, _clock, NullLogger<ObservedCalendarRunner>.Instance),
        _gate,
        _clock,
        NullLogger<CalendarUpkeep>.Instance);

    private JobReporter Reporter => new(_job, _jobs, _clock);

    [Fact]
    public async Task Nse_is_tried_once_a_day_however_many_jobs_run()
    {
        _source.Json = FakeHolidaySource.Sample(("03-Nov-2026", "Diwali"));

        await Upkeep.RunAsync(Reporter, Ct);
        await Upkeep.RunAsync(Reporter, Ct);
        _clock.Advance(TimeSpan.FromHours(24));
        await Upkeep.RunAsync(Reporter, Ct);

        _source.Calls.ShouldBe(2);
        _data.Days.ShouldHaveSingleItem().Source.ShouldBe(CalendarSource.Nse);
        _job.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_refused_nse_request_is_a_warning_of_the_job_and_the_observed_pass_still_runs()
    {
        _source.Failure = "NSE's holiday list answered HTTP 403.";
        _observed.Presence = new PresenceBuilder().Weekdays(20, new DateOnly(2024, 10, 14), new DateOnly(2024, 11, 15), (_, d) => d != new DateOnly(2024, 10, 22)).Build();

        await Upkeep.RunAsync(Reporter, Ct);

        _job.Warnings.ShouldBe(["Trading calendar: NSE's holiday list answered HTTP 403. Add the holidays by hand instead."]);
        _job.IsRunning.ShouldBeTrue();
        _data.Days.ShouldHaveSingleItem().Source.ShouldBe(CalendarSource.Observed);
    }

    [Fact]
    public async Task An_unexpected_failure_is_a_generic_warning_that_names_nothing()
    {
        _source.Json = FakeHolidaySource.Sample();
        _observed.Presence = null!; // makes the pass throw a NullReferenceException

        await Upkeep.RunAsync(Reporter, Ct);

        _job.Warnings.ShouldBe(["The trading calendar could not be updated. See the API log."]);
    }

    [Fact]
    public async Task Shutting_down_is_not_swallowed()
    {
        using var cancel = new CancellationTokenSource();
        await cancel.CancelAsync();
        var upkeep = new CalendarUpkeep(
            new NseCalendarRefresher(new CancellingSource(), _data, _calendar, _data, _data, _clock, NullLogger<NseCalendarRefresher>.Instance),
            new ObservedCalendarRunner(_observed, _data, _calendar, _data, _clock, NullLogger<ObservedCalendarRunner>.Instance),
            _gate,
            _clock,
            NullLogger<CalendarUpkeep>.Instance);

        await Should.ThrowAsync<OperationCanceledException>(async () => await upkeep.RunAsync(Reporter, cancel.Token));
    }

    private sealed class CancellingSource : IHolidaySource
    {
        public ValueTask<XDrishti.Application.Abstractions.Market.SourceDownload> OpenHolidaysAsync(CancellationToken cancellationToken) =>
            throw new OperationCanceledException(cancellationToken);
    }
}
