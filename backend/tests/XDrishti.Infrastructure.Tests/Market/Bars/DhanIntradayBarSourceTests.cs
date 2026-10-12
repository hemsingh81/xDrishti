using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Market.Bars.Dhan;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Market.Bars;
using XDrishti.Infrastructure.Brokers;
using XDrishti.Infrastructure.Market.Bars;
using XDrishti.Infrastructure.Tests.Brokers;

namespace XDrishti.Infrastructure.Tests.Market.Bars;

/// <summary>The Dhan source over a stubbed Dhan: windows, several instruments, a refused token, alignment problems and the running minute (made-up values, fake clock).</summary>
public sealed class DhanIntradayBarSourceTests : IDisposable
{
    private const string Marker = "FAKE-SECRET-MARKER-DO-NOT-LEAK";

    /// <summary>Monday 2026-10-12 12:00:30 India time.</summary>
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 6, 30, 30, TimeSpan.Zero);

    private readonly StubBroker _broker = new();

    private readonly FakeTimeProvider _clock = new(Now);

    private readonly AutoAdvancingClock _waits;

    private readonly FakeAccount _account = new();

    private readonly List<string> _logged = [];

    public DhanIntradayBarSourceTests() => _waits = new AutoAdvancingClock(_clock);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _broker.Dispose();

    private DhanIntradayBarSource Source => new(
        _account,
        new DhanIntradayChartClient(_broker, new RequestThrottle(5, _waits), _waits, NullLogger<DhanIntradayChartClient>.Instance),
        _waits,
        new ListLogger(_logged));

    private static long At(int day, int minuteOfDayIndia) =>
        new DateTimeOffset(2026, 10, day, 0, 0, 0, TimeSpan.FromMinutes(330)).ToUnixTimeSeconds() + (minuteOfDayIndia * 60L);

    private static string Arrays(IEnumerable<long> starts)
    {
        var list = starts.ToList();
        string Join(Func<int, string> value) => string.Join(',', list.Select((_, i) => value(i)));
        return $$"""{"open":[{{Join(i => (100 + i).ToString(CultureInfo.InvariantCulture))}}],"high":[{{Join(i => (101 + i).ToString(CultureInfo.InvariantCulture))}}],"low":[{{Join(i => (99 + i).ToString(CultureInfo.InvariantCulture))}}],"close":[{{Join(i => (100 + i).ToString(CultureInfo.InvariantCulture))}}],"volume":[{{Join(_ => "10")}}],"timestamp":[{{string.Join(',', list)}}]}""";
    }

    /// <summary>Answers each request with the sessions of the days inside its fromDate–toDate (09:15 to 09:19 India time, five bars a day).</summary>
    private HttpResponseMessage Sessions(HttpRequestMessage request)
    {
        using var body = JsonDocument.Parse(_broker.Requests[^1].Body!);
        var from = DateTime.ParseExact(body.RootElement.GetProperty("fromDate").GetString()!, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        var to = DateTime.ParseExact(body.RootElement.GetProperty("toDate").GetString()!, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        var starts = new List<long>();
        for (var day = from.Date; day <= to.Date; day = day.AddDays(1))
        {
            if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                continue;
            }

            for (var m = 0; m < 5; m++)
            {
                var start = new DateTimeOffset(day.AddMinutes(SessionClock.OpenMinute + m), TimeSpan.FromMinutes(330));
                if (start.DateTime >= from && start.DateTime <= to)
                {
                    starts.Add(start.ToUnixTimeSeconds());
                }
            }
        }

        return StubBroker.Json(HttpStatusCode.OK, Arrays(starts));
    }

    private static IntradayTarget Target(string symbol, params FetchWindow[] windows) => new(Guid.NewGuid(), symbol, "share", "1001", "NSE_EQ", windows);

    /// <summary>Runs the fetch; the throttle's waits end at once because the clock moves on by what they wait for.</summary>
    private async Task<List<IntradayResult>> FetchAsync(params IntradayTarget[] targets) =>
        await Source.FetchAsync(new IntradayFetchPlan(targets), Ct).ToListAsync(Ct);

    [Fact]
    public async Task The_source_is_always_ready_and_leaves_the_account_question_to_the_pipeline()
    {
        (await Source.CheckAsync(Ct)).ShouldBe(SourceAvailability.Ready);
    }

    [Fact]
    public async Task Each_window_is_one_request_and_the_bars_of_all_windows_come_back_together()
    {
        _broker.Respond = Sessions;
        var windows = DhanFetchPlanner.Windows(null, Now, 120); // 120 days: two windows of at most 90 days
        windows.Count.ShouldBe(2);
        var target = Target("NSE:ABB", [.. windows]);

        var result = (await FetchAsync(target)).ShouldHaveSingleItem();

        result.Failure.ShouldBeNull();
        result.Requests.ShouldBe(2);
        _broker.Requests.Count.ShouldBe(2);
        _broker.Requests.ShouldAllBe(r => r.Uri.AbsoluteUri == "https://api.dhan.co/v2/charts/intraday");
        using var first = JsonDocument.Parse(_broker.Requests[0].Body!);
        using var second = JsonDocument.Parse(_broker.Requests[1].Body!);
        first.RootElement.GetProperty("toDate").GetString().ShouldBe(second.RootElement.GetProperty("fromDate").GetString()); // neighbours share their boundary instant
        result.Bars.Count.ShouldBeGreaterThan(5 * 60); // about 86 weekdays × 5 bars
        result.Bars.Select(b => b.EpochSeconds).ShouldAllBe(s => s <= Now.ToUnixTimeSeconds());
        result.Bars.Select(b => b.EpochSeconds).Distinct().Count().ShouldBe(result.Bars.Count); // the bars at the boundary exist once here (the pipeline dedupes anyway)
    }

    [Fact]
    public async Task Instruments_are_fetched_one_after_the_other_and_a_failed_one_does_not_stop_the_rest()
    {
        var window = new FetchWindow(Now.AddDays(-3), Now);
        var calls = 0;
        _broker.Respond = request =>
        {
            calls++;
            return calls == 1 ? StubBroker.Json(HttpStatusCode.BadRequest, """{"errorCode":"DH-905"}""") : Sessions(request);
        };
        var a = Target("NSE:AAA", window);
        var b = Target("NSE:BBB", window);

        var results = await FetchAsync(a, b);

        results.Select(r => r.InstrumentId).ShouldBe([a.InstrumentId, b.InstrumentId]);
        results[0].Failure!.Kind.ShouldBe(IntradayFailureKind.Failed);
        results[0].Bars.ShouldBeEmpty();
        results[1].Failure.ShouldBeNull();
        results[1].Bars.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task A_failing_later_window_fails_the_instrument_and_discards_the_bars_of_earlier_ones()
    {
        var windows = DhanFetchPlanner.Windows(null, Now, 120);
        var calls = 0;
        _broker.Respond = request => ++calls == 1 ? Sessions(request) : StubBroker.Json(HttpStatusCode.NotFound, "");

        var result = (await FetchAsync(Target("NSE:ABB", [.. windows]))).ShouldHaveSingleItem();

        result.Failure!.Message.ShouldContain("HTTP 404");
        result.Bars.ShouldBeEmpty();
        result.Requests.ShouldBe(2);
    }

    [Fact]
    public async Task A_refused_token_ends_the_fetch_and_the_remaining_instruments_are_not_asked()
    {
        var window = new FetchWindow(Now.AddDays(-3), Now);
        _broker.Respond = _ => StubBroker.Json(HttpStatusCode.Unauthorized, """{"errorCode":"DH-901"}""");
        var a = Target("NSE:AAA", window);
        var b = Target("NSE:BBB", window);

        var results = await FetchAsync(a, b);

        var result = results.ShouldHaveSingleItem();
        (result.InstrumentId, result.Failure!.Kind).ShouldBe((a.InstrumentId, IntradayFailureKind.Unauthorized));
        _broker.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Without_an_access_token_nothing_is_sent_and_the_phase_is_told_the_token_is_unusable()
    {
        _account.Token = null;
        var a = Target("NSE:AAA", new FetchWindow(Now.AddDays(-3), Now));

        var result = (await FetchAsync(a)).ShouldHaveSingleItem();

        (result.Failure!.Kind, result.Requests).ShouldBe((IntradayFailureKind.Unauthorized, 0));
        _broker.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_empty_plan_asks_for_nothing()
    {
        (await FetchAsync()).ShouldBeEmpty();
        _broker.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_token_of_the_data_account_goes_into_the_header_of_every_request()
    {
        _broker.Respond = Sessions;

        await FetchAsync(Target("NSE:ABB", [.. DhanFetchPlanner.Windows(null, Now, 120)]));

        _broker.Requests.ShouldAllBe(r => r.Headers["access-token"] == "token-" + Marker);
    }

    [Fact]
    public async Task The_minute_that_has_not_ended_yet_is_left_for_the_next_fetch()
    {
        // now = 12:00:30 India: the 11:59 bar has ended (12:00:00), the 12:00 bar is still running.
        _broker.Respond = _ => StubBroker.Json(HttpStatusCode.OK, Arrays([At(12, 11 * 60 + 58), At(12, 11 * 60 + 59), At(12, 12 * 60)]));

        var result = (await FetchAsync(Target("NSE:ABB", new FetchWindow(Now.AddHours(-1), Now)))).ShouldHaveSingleItem();

        result.Bars.Select(b => b.EpochSeconds).ShouldBe([At(12, 11 * 60 + 58), At(12, 11 * 60 + 59)]);
    }

    [Theory]
    [InlineData(-330, "adding 5 h 30 min", 5)] // the stamps are 5 h 30 too early: they fit after adding
    [InlineData(330, "subtracting 5 h 30 min", 4)] // India time sent as if it were UTC: 5 h 30 too late (14:45 still lies in the session)
    public async Task Timestamps_that_fit_the_sessions_only_after_a_5h30_shift_fail_the_instrument_and_nothing_is_shifted(int shiftMinutes, string expectedWording, int outside)
    {
        // Bars at 09:15, 10:00, 10:30, 11:00 and 11:30 India time, delivered shifted by shiftMinutes.
        var starts = new[] { 9 * 60 + 15, 10 * 60, 10 * 60 + 30, 11 * 60, 11 * 60 + 30 }.Select(m => At(9, m) + (shiftMinutes * 60L));
        _broker.Respond = _ => StubBroker.Json(HttpStatusCode.OK, Arrays(starts));

        var result = (await FetchAsync(Target("NSE:ABB", new FetchWindow(Now.AddDays(-5), Now)))).ShouldHaveSingleItem();

        result.Failure!.Kind.ShouldBe(IntradayFailureKind.Failed);
        result.Failure.Message.ShouldContain(expectedWording);
        result.Failure.Message.ShouldContain($"{outside} of 5 bars lie outside the regular trading session");
        result.Failure.Message.ShouldContain("nothing was shifted");
        result.Bars.ShouldBeEmpty();
    }

    [Fact]
    public async Task Timestamps_in_another_time_base_are_named_as_such()
    {
        // The same instants with another epoch (ten years early): outside the period asked for.
        var starts = Enumerable.Range(0, 5).Select(m => At(9, SessionClock.OpenMinute + m) - (3652L * 86400));
        _broker.Respond = _ => StubBroker.Json(HttpStatusCode.OK, Arrays(starts));

        var result = (await FetchAsync(Target("NSE:ABB", new FetchWindow(Now.AddDays(-5), Now)))).ShouldHaveSingleItem();

        result.Failure!.Message.ShouldContain("time base of the timestamps is not Unix time");
    }

    [Fact]
    public async Task A_few_stray_bars_outside_a_session_are_the_pipelines_to_reject_not_an_offset_problem()
    {
        var starts = Enumerable.Range(0, 5).Select(m => At(9, SessionClock.OpenMinute + m)).Append(At(9, 3 * 60));
        _broker.Respond = _ => StubBroker.Json(HttpStatusCode.OK, Arrays(starts));

        var result = (await FetchAsync(Target("NSE:ABB", new FetchWindow(Now.AddDays(-5), Now)))).ShouldHaveSingleItem();

        result.Failure.ShouldBeNull();
        result.Bars.Count.ShouldBe(6);
    }

    [Fact]
    public async Task Only_counts_and_the_instrument_slug_are_logged()
    {
        _broker.Respond = Sessions;
        await FetchAsync(Target("NSE:ABB", new FetchWindow(Now.AddDays(-3), Now)));
        _broker.Respond = _ => StubBroker.Json(HttpStatusCode.BadRequest, $$"""{"errorCode":"DH-905","errorMessage":"token-{{Marker}}"}""");
        await FetchAsync(Target("NSE:XYZ", new FetchWindow(Now.AddDays(-3), Now)));

        var logged = string.Join('\n', _logged);
        logged.ShouldContain("NSE:ABB");
        logged.ShouldContain("NSE:XYZ");
        logged.ShouldNotContain(Marker);
        logged.ShouldNotContain("api.dhan.co");
        logged.ShouldNotContain("1001"); // the security id
    }

    private sealed class FakeAccount : IDataAccountStatus
    {
        public SecretValue? Token { get; set; } = SecretValue.TryCreate("token-" + Marker, out var value, out _) ? value : null;

        public ValueTask<DataAccountState> GetAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(new DataAccountState(Guid.NewGuid(), "Data", DataAccountStatuses.Connected, null));

        public ValueTask<SecretValue?> GetAccessTokenAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Token);
    }

    private sealed class ListLogger(List<string> messages) : ILogger<DhanIntradayBarSource>
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => NullLogger.Instance.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            messages.Add(formatter(state, exception));
    }
}
