#pragma warning disable CA2012 // NSubstitute setup on ValueTask members is not awaited
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Market.Eod;
using XDrishti.Application.Market.Eod;

namespace XDrishti.Application.Tests.Market.Eod;

/// <summary>CF-04 criteria 1, 2 and 18 (Part A): zero calls when off, a refused endpoint, resumable and idempotent loading, fail-closed parsing.</summary>
public sealed class LoadEodHandlerTests
{
    // 2024-07-09 is a Tuesday: the range 2024-07-05 (Fri) .. 2024-07-08 (Mon) holds two weekdays.
    private static readonly DateOnly Friday = new(2024, 7, 5);

    private static readonly DateOnly Monday = new(2024, 7, 8);

    private readonly IEodSettings _settings = Substitute.For<IEodSettings>();

    private readonly IEodSource _source = Substitute.For<IEodSource>();

    private readonly IEodStore _store = Substitute.For<IEodStore>();

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2024, 7, 9, 10, 0, 0, TimeSpan.Zero));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private LoadEodHandler Handler()
    {
        _settings.Enabled.Returns(true);
        _settings.Series.Returns(["EQ"]);
        _settings.MaxPerDay.Returns(2000);
        _settings.Delay.Returns(TimeSpan.Zero);
        _settings.Years.Returns(10);
        _store.ListDoneDatesAsync(default, default, default, Ct).ReturnsForAnyArgs(new HashSet<DateOnly>());
        _store.ListSpecialSessionDatesAsync(default, default, Ct).ReturnsForAnyArgs(new HashSet<DateOnly>());
        return new LoadEodHandler(_settings, _source, _store, _clock);
    }

    [Fact]
    public async Task With_the_switch_off_nothing_is_requested_and_nothing_is_read_or_written()
    {
        var handler = Handler();
        _settings.Enabled.Returns(false);

        var result = await handler.HandleAsync(new LoadEodCommand(Friday, Monday), Ct);

        result.Error.Code.ShouldBe("history.disabled");
        await _source.DidNotReceiveWithAnyArgs().FetchAsync(default, default, Ct);
        await _store.DidNotReceiveWithAnyArgs().ListDoneDatesAsync(default, default, default, Ct);
        await _store.DidNotReceiveWithAnyArgs().WriteDayAsync(default, default!, default!, default, Ct);
    }

    [Fact]
    public async Task A_refused_endpoint_is_a_failure_that_names_the_owner_act_and_writes_nothing()
    {
        var handler = Handler();
        _source.FetchAsync(EodFileKind.Bhavcopy, Arg.Any<DateOnly>(), Ct).Returns(EodFetch.NotAllowed("The master-data endpoint GET host/path is not on the allow-list."));

        var result = await handler.HandleAsync(new LoadEodCommand(Friday, Monday), Ct);

        result.Error.Code.ShouldBe("history.not_allowed");
        result.Error.Message.ShouldContain("owner must add");
        await _store.DidNotReceiveWithAnyArgs().WriteDayAsync(default, default!, default!, default, Ct);
    }

    [Fact]
    public async Task Days_are_loaded_newest_first_one_file_each_and_the_delivery_is_merged()
    {
        var handler = Handler();
        _source.FetchAsync(EodFileKind.Bhavcopy, Monday, Ct).Returns(EodFetch.Ok(EodFixtures.Udiff(EodFixtures.UdiffRow("ABC", "EQ", date: "2024-07-08"))));
        _source.FetchAsync(EodFileKind.Bhavcopy, Friday, Ct).Returns(EodFetch.Ok(EodFixtures.Legacy(EodFixtures.LegacyRow("ABC", "EQ"))));
        _source.FetchAsync(EodFileKind.Delivery, Arg.Any<DateOnly>(), Ct).Returns(EodFetch.Ok(EodFixtures.Delivery("ABC,EQ,10000,6000,60.00")));

        var result = await handler.HandleAsync(new LoadEodCommand(Friday, Monday), Ct);

        result.Value.ShouldSatisfyAllConditions(s => s.DaysLoaded.ShouldBe(2), s => s.Rows.ShouldBe(2), s => s.Requests.ShouldBe(4), s => s.Stopped.ShouldBeNull());
        var written = _store.ReceivedCalls().Where(c => c.GetMethodInfo().Name == nameof(IEodStore.WriteDayAsync)).Select(c => (DateOnly)c.GetArguments()[0]!).ToList();
        written.ShouldBe([Monday, Friday]);
        await _store.Received(1).WriteDayAsync(Monday, Arg.Is<IReadOnlyList<EodRow>>(r => r.Single().DeliveryPct == 60.00m), Arg.Any<string>(), Arg.Any<string?>(), Ct);
    }

    [Fact]
    public async Task Days_already_in_the_ledger_are_not_requested_again()
    {
        var handler = Handler();
        _store.ListDoneDatesAsync(default, default, default, Ct).ReturnsForAnyArgs(new HashSet<DateOnly> { Friday, Monday });

        var result = await handler.HandleAsync(new LoadEodCommand(Friday, Monday), Ct);

        result.Value.ShouldSatisfyAllConditions(s => s.DaysAlreadyDone.ShouldBe(2), s => s.Requests.ShouldBe(0));
        await _source.DidNotReceiveWithAnyArgs().FetchAsync(default, default, Ct);
    }

    [Fact]
    public async Task A_day_without_a_file_is_recorded_as_absent_and_is_not_an_error()
    {
        var handler = Handler();
        _source.FetchAsync(default, default, Ct).ReturnsForAnyArgs(EodFetch.Absent());

        var result = await handler.HandleAsync(new LoadEodCommand(Friday, Friday), Ct);

        result.Value.DaysAbsent.ShouldBe(1);
        await _store.Received(1).MarkAbsentAsync(Friday, Ct);
    }

    [Fact]
    public async Task An_unknown_header_stops_the_run_and_writes_nothing()
    {
        var handler = Handler();
        _source.FetchAsync(EodFileKind.Bhavcopy, Arg.Any<DateOnly>(), Ct).Returns(EodFetch.Ok(System.Text.Encoding.UTF8.GetBytes("A,B,C\n1,2,3\n")));

        var result = await handler.HandleAsync(new LoadEodCommand(Friday, Monday), Ct);

        result.Error.Code.ShouldBe("history.unknown_format");
        await _store.DidNotReceiveWithAnyArgs().WriteDayAsync(default, default!, default!, default, Ct);
        await _source.Received(1).FetchAsync(Arg.Any<EodFileKind>(), Arg.Any<DateOnly>(), Ct);
    }

    [Fact]
    public async Task A_network_failure_stops_the_run_for_the_next_one_to_resume()
    {
        var handler = Handler();
        _source.FetchAsync(default, default, Ct).ReturnsForAnyArgs(EodFetch.Failed("NSE could not be reached."));

        var result = await handler.HandleAsync(new LoadEodCommand(Friday, Monday), Ct);

        result.Value.Stopped.ShouldBe("fetch_failed");
        result.Value.DaysLoaded.ShouldBe(0);
        await _store.DidNotReceiveWithAnyArgs().WriteDayAsync(default, default!, default!, default, Ct);
    }

    [Fact]
    public async Task A_delivery_fetch_that_fails_does_not_write_the_day_without_delivery()
    {
        var handler = Handler();
        _source.FetchAsync(EodFileKind.Bhavcopy, Arg.Any<DateOnly>(), Ct).Returns(EodFetch.Ok(EodFixtures.Legacy(EodFixtures.LegacyRow("ABC", "EQ"))));
        _source.FetchAsync(EodFileKind.Delivery, Arg.Any<DateOnly>(), Ct).Returns(EodFetch.Failed("timeout"));

        var result = await handler.HandleAsync(new LoadEodCommand(Friday, Friday), Ct);

        result.Value.Stopped.ShouldBe("fetch_failed");
        await _store.DidNotReceiveWithAnyArgs().WriteDayAsync(default, default!, default!, default, Ct);
    }

    [Fact]
    public async Task The_run_stops_at_the_request_budget()
    {
        var handler = Handler();
        _settings.MaxPerDay.Returns(2);
        _source.FetchAsync(default, default, Ct).ReturnsForAnyArgs(EodFetch.Absent());

        var result = await handler.HandleAsync(new LoadEodCommand(new DateOnly(2024, 6, 24), Monday), Ct);

        result.Value.Stopped.ShouldBe("max_per_day");
        result.Value.Requests.ShouldBe(2);
    }

    [Fact]
    public async Task To_is_clamped_to_yesterday_so_a_day_that_is_not_over_is_never_asked_for()
    {
        var handler = Handler();
        _source.FetchAsync(default, default, Ct).ReturnsForAnyArgs(EodFetch.Absent());

        await handler.HandleAsync(new LoadEodCommand(Monday, new DateOnly(2024, 7, 12)), Ct);

        await _store.Received(1).ListDoneDatesAsync(Monday, Monday, false, Ct); // the clock is 2024-07-09: yesterday is Monday 2024-07-08
        await _source.DidNotReceive().FetchAsync(Arg.Any<EodFileKind>(), new DateOnly(2024, 7, 9), Ct);
    }

    [Fact]
    public async Task A_404_on_a_confirmed_special_session_is_a_retryable_failure_and_is_not_recorded_as_absent()
    {
        var handler = Handler();
        _store.ListSpecialSessionDatesAsync(default, default, Ct).ReturnsForAnyArgs(new HashSet<DateOnly> { Friday });
        _source.FetchAsync(default, default, Ct).ReturnsForAnyArgs(EodFetch.Absent());

        var result = await handler.HandleAsync(new LoadEodCommand(Friday, Friday), Ct);

        result.Value.ShouldSatisfyAllConditions(s => s.Stopped.ShouldBe("fetch_failed"), s => s.DaysAbsent.ShouldBe(0));
        await _store.DidNotReceiveWithAnyArgs().MarkAbsentAsync(default, Ct);
    }

    [Fact]
    public async Task Recheck_absent_asks_the_ledger_for_the_days_that_are_not_loaded()
    {
        var handler = Handler();
        _source.FetchAsync(default, default, Ct).ReturnsForAnyArgs(EodFetch.Absent());

        await handler.HandleAsync(new LoadEodCommand(Friday, Friday, RecheckAbsent: true), Ct);

        await _store.Received(1).ListDoneDatesAsync(Friday, Friday, true, Ct);
        await _source.Received(1).FetchAsync(EodFileKind.Bhavcopy, Friday, Ct);
    }

    [Fact]
    public async Task A_weekend_day_the_calendar_confirms_as_a_special_session_is_loaded()
    {
        var handler = Handler();
        var sunday = new DateOnly(2024, 7, 7);
        _store.ListSpecialSessionDatesAsync(default, default, Ct).ReturnsForAnyArgs(new HashSet<DateOnly> { sunday });
        _source.FetchAsync(EodFileKind.Bhavcopy, sunday, Ct).Returns(EodFetch.Ok(EodFixtures.Legacy(EodFixtures.LegacyRow("ABC", "EQ", date: "07-JUL-2024"))));
        _source.FetchAsync(EodFileKind.Delivery, sunday, Ct).Returns(EodFetch.Absent());

        var result = await handler.HandleAsync(new LoadEodCommand(sunday, sunday), Ct);

        result.Value.DaysLoaded.ShouldBe(1);
        LoadEodHandler.Weekdays(new DateOnly(2024, 7, 6), new DateOnly(2024, 7, 8), new HashSet<DateOnly> { sunday }).ShouldBe([sunday, Monday]);
        LoadEodHandler.Weekdays(new DateOnly(2024, 7, 6), new DateOnly(2024, 7, 8), new HashSet<DateOnly>()).ShouldBe([Monday]);
    }
}
