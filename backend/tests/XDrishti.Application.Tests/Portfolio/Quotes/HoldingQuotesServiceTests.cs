using Microsoft.Extensions.Logging.Abstractions;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Platform.Services;
using XDrishti.Application.Portfolio.Attention;
using XDrishti.Application.Portfolio.Heal;
using XDrishti.Application.Portfolio.Quotes;
using XDrishti.Application.Tests.Platform.Services;
using XDrishti.Application.Tests.Portfolio.Actions;
using XDrishti.Application.Tests.Portfolio.Heal;
using XDrishti.Domain.Platform.Services;
using XDrishti.Domain.Portfolio;
using static XDrishti.Application.Tests.Platform.Services.ServicesWorld;

namespace XDrishti.Application.Tests.Portfolio.Quotes;

/// <summary>The quote poll as a service (ADR 0019): what it quotes, the message, the skipped and failed runs and the registration. Run through the real scheduler path with Run now; Monday 2026-10-12, India time.</summary>
public sealed class HoldingQuotesServiceTests : IDisposable
{
    private readonly FakeHoldingQuotes _holdings = new();
    private readonly FakeQuoteSource _source = new();
    private readonly FakeDataAccount _account = FakeDataAccount.Connected();
    private readonly ActionsHarness _actions = new();
    private readonly InMemorySyncHealth _health = new();
    private readonly FixedHealSettings _heal = new();
    private readonly ServicesWorld _world;

    public HoldingQuotesServiceTests() =>
        _world = new ServicesWorld(
            (world, scopes) => [new HoldingQuotesService(scopes, world.Clock, NullLogger<HoldingQuotesService>.Instance)],
            provider => provider
                .Singleton<IHoldingQuotes>(_holdings)
                .Singleton<IQuoteSource>(_source)
                .Singleton<IDataAccountStatus>(_account)
                .Singleton(_actions.Checker())
                .Singleton(_actions.Reconciler())
                .Singleton<ISyncHealthStore>(_health)
                .Singleton<IHealSettings>(_heal)
                .Singleton<IAttentionReconciler>(new XDrishti.Application.Tests.Portfolio.Heal.RecordingAttentionReconciler())
                .Singleton<SyncHealthRecorder, SyncHealthRecorder>());

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _world.Dispose();

    private async Task<ServiceRun> RunAsync()
    {
        var started = await _world.RunNowAsync(HoldingQuotesService.Id);
        started.IsSuccess.ShouldBeTrue();
        await _world.Runner.WhenIdleAsync();
        return _world.Data.RunsOf(HoldingQuotesService.Id).Last();
    }

    // ---- the definition ----

    [Fact]
    public async Task It_is_listed_with_the_defaults_of_the_story()
    {
        var view = await _world.ViewAsync(HoldingQuotesService.Id);

        (view.Name, view.RunsAs, view.Enabled, view.IsDefault).ShouldBe(("Holdings quotes", "xd-api", true, true));
        view.AllowedScheduleTypes.ShouldBe(["interval"]);
        (view.Schedule.Type, view.Schedule.From, view.Schedule.To, view.Schedule.EveryMinutes, view.Schedule.TradingDaysOnly).ShouldBe(("interval", "09:15", "15:30", 5, true));
        view.Schedule.Days.ShouldBe(["mon", "tue", "wed", "thu", "fri"]);
        (view.Execution.Retries, view.Execution.TimeoutMinutes, view.Execution.CatchUp, view.Execution.NotifyOnFailure).ShouldBe((0, 2, false, true));
        view.Parameters.ShouldBeEmpty();
        view.Issues.ShouldBeEmpty();
        view.ScheduleWords.ShouldBe("Every 5 min from 09:15 to 15:30 IST on trading days");
        view.NextRun.ShouldBe(At(12, 10, 5)); // the clock reads 10:00
    }

    [Fact]
    public async Task A_window_that_does_not_cover_the_session_is_saved_with_a_warning()
    {
        var narrower = Schedule.Interval(5, new TimeOnly(10, 0), new TimeOnly(15, 30));

        var saved = await _world.SaveAsync(HoldingQuotesService.Id, Svc.Input(narrower, timeout: 2, catchUp: false));

        saved.IsSuccess.ShouldBeTrue();
        saved.Value.Issues.ShouldContain(i => i.Code == ScheduleIssueCodes.WindowNotCovering && i.Severity == "warning");
    }

    [Fact]
    public async Task Another_schedule_type_is_refused()
    {
        var saved = await _world.SaveAsync(HoldingQuotesService.Id, Svc.Input(Schedule.Daily(new TimeOnly(10, 0)), timeout: 2, catchUp: false));

        saved.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task The_preview_lists_the_next_three_runs_and_skips_the_weekend()
    {
        _world.SetClock(At(9, 15, 31)); // Friday 15:31, after the last run

        var preview = await _world.PreviewAsync(HoldingQuotesService.Id, Svc.Input(HoldingQuotesService.Definition.Defaults.Schedule, timeout: 2, catchUp: false));

        preview.Value.NextRuns.ShouldBe([At(12, 9, 15), At(12, 9, 20), At(12, 9, 25)]);
    }

    // ---- the runs ----

    [Fact]
    public async Task Without_open_holdings_the_run_succeeds_and_makes_no_request()
    {
        var run = await RunAsync();

        (run.Status, run.Message).ShouldBe((ServiceRunStatus.Succeeded, "No open holdings"));
        _source.Calls.ShouldBeEmpty();
        _holdings.Applied.ShouldBeEmpty();
    }

    [Fact]
    public async Task It_quotes_the_distinct_nse_securities_once_and_writes_them_with_the_fetch_time()
    {
        _holdings.Targets = FakeHoldingQuotes.Held(open: 43, nse: 41);

        var run = await RunAsync();

        (run.Status, run.Message).ShouldBe((ServiceRunStatus.Succeeded, "Quoted 41 of 43 holdings; 2 not on NSE"));
        _source.Calls.ShouldHaveSingleItem().Count.ShouldBe(41);
        var (quotes, at) = _holdings.Applied.ShouldHaveSingleItem();
        quotes.Count.ShouldBe(41);
        quotes[0].ShouldBe(new HoldingQuote(Guid.Parse("00000000-0000-7000-8000-000000000001"), 100m, 98m));
        at.ShouldBe(At(12, 10, 0));
    }

    [Fact]
    public async Task All_ids_go_to_the_source_in_one_call_which_splits_them()
    {
        _holdings.Targets = FakeHoldingQuotes.Held(open: 1500, nse: 1500);

        await RunAsync();

        _source.Calls.ShouldHaveSingleItem().Count.ShouldBe(1500);
    }

    [Fact]
    public async Task A_security_missing_from_the_answer_keeps_its_price_and_is_counted()
    {
        _holdings.Targets = FakeHoldingQuotes.Held(open: 4, nse: 3);
        _source.Respond = ids => new QuoteResult([.. ids.Where(i => i != "1002").Select(i => new SecurityQuote(i, 50m, null))], null, 1);

        var run = await RunAsync();

        run.Message.ShouldBe("Quoted 2 of 4 holdings; 1 not on NSE; 1 without a quote");
        _holdings.Applied.ShouldHaveSingleItem().Quotes.Select(q => q.PreviousClose).ShouldAllBe(p => p == null);
        _holdings.Applied[0].Quotes.Count.ShouldBe(2);
    }

    [Fact]
    public async Task When_nothing_is_on_the_nse_no_request_is_made_and_the_run_succeeds()
    {
        _holdings.Targets = FakeHoldingQuotes.Held(open: 2, nse: 0);

        var run = await RunAsync();

        (run.Status, run.Message).ShouldBe((ServiceRunStatus.Succeeded, "Quoted 0 of 2 holdings; 2 not on NSE"));
        _source.Calls.ShouldBeEmpty();
        _holdings.Applied.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(DataAccountStatuses.None, "No account has the primary Data role.")]
    [InlineData(DataAccountStatuses.Expired, "The Dhan sign-in has expired.")]
    [InlineData(DataAccountStatuses.NotConnected, "The Data account is not connected to Dhan.")]
    public async Task Without_a_usable_data_account_the_run_is_skipped_with_the_reason_and_changes_nothing(string status, string reason)
    {
        _holdings.Targets = FakeHoldingQuotes.Held(open: 3, nse: 3);
        _account.State = new DataAccountState(null, null, status, reason);

        var run = await RunAsync();

        (run.Status, run.Message).ShouldBe((ServiceRunStatus.Skipped, reason));
        _source.Calls.ShouldBeEmpty();
        _holdings.Applied.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_refused_token_fails_the_run_at_once_with_the_safe_message_and_changes_no_price()
    {
        const string message = "The Data account's token was refused by Dhan.";
        _holdings.Targets = FakeHoldingQuotes.Held(open: 3, nse: 3);
        _source.Respond = _ => QuoteResult.Failed(new QuoteFailure(QuoteFailureKind.Unauthorized, message), 1);

        var run = await RunAsync();

        (run.Status, run.Message).ShouldBe((ServiceRunStatus.Failed, message));
        _holdings.Applied.ShouldBeEmpty();
        _world.Data.RunsOf(HoldingQuotesService.Id).Count().ShouldBe(1); // retries are 0
        _health.Of(_account.State.AccountId!.Value, SyncHealthKind.Quotes).LastFailureReason.ShouldBe("token_refused");
    }

    [Theory]
    [InlineData(QuoteFailureKind.RateLimited, "Dhan's rate limit was reached; the next tick tries again.", "rate_limited")]
    [InlineData(QuoteFailureKind.Failed, "Dhan's quote service answered HTTP 503.", "unavailable")]
    public async Task A_poll_that_dhan_did_not_answer_is_skipped_quietly_with_the_reason_and_changes_no_price(QuoteFailureKind kind, string message, string reason)
    {
        _holdings.Targets = FakeHoldingQuotes.Held(open: 3, nse: 3);
        _source.Respond = _ => QuoteResult.Failed(new QuoteFailure(kind, message), 1);

        var run = await RunAsync();

        (run.Status, run.Message).ShouldBe((ServiceRunStatus.Skipped, message));
        _holdings.Applied.ShouldBeEmpty();
        var figures = _health.Of(_account.State.AccountId!.Value, SyncHealthKind.Quotes);
        (figures.ConsecutiveFailedDays, figures.LastFailureReason).ShouldBe((1, reason));
        (await NoticesAsync()).Items.ShouldBeEmpty(); // nothing is shown while the next polls can still put it right
    }

    [Fact]
    public async Task Failures_on_the_same_day_count_as_one_failed_day_and_a_success_clears_them()
    {
        _holdings.Targets = FakeHoldingQuotes.Held(open: 1, nse: 1);
        _source.Respond = _ => QuoteResult.Failed(new QuoteFailure(QuoteFailureKind.Failed, "Dhan's quote service answered HTTP 503."), 1);
        await RunAsync();
        _world.SetClock(At(12, 10, 5));
        await RunAsync();
        var id = _account.State.AccountId!.Value;

        var figures = _health.Of(id, SyncHealthKind.Quotes);

        (figures.ConsecutiveFailedDays, figures.Attempts).ShouldBe((1, 2));

        _source.Respond = ids => new QuoteResult([.. ids.Select(i => new SecurityQuote(i, 10m, 9m))], null, 1);
        _world.SetClock(At(12, 10, 10));
        (await RunAsync()).Status.ShouldBe(ServiceRunStatus.Succeeded);

        _health.Of(id, SyncHealthKind.Quotes).ShouldBe(SyncHealth.None(id, SyncHealthKind.Quotes) with { MapperVersion = 0 });
    }

    [Fact]
    public async Task The_second_failed_day_in_a_row_fails_the_run_and_raises_the_service_notice()
    {
        _holdings.Targets = FakeHoldingQuotes.Held(open: 1, nse: 1);
        _source.Respond = _ => QuoteResult.Failed(new QuoteFailure(QuoteFailureKind.RateLimited, "Dhan's rate limit was reached; the next tick tries again."), 1);
        (await RunAsync()).Status.ShouldBe(ServiceRunStatus.Skipped); // Monday: the first failed day

        _world.SetClock(At(13, 10, 0));
        var second = await RunAsync(); // Tuesday: the second

        second.Status.ShouldBe(ServiceRunStatus.Failed);
        (await NoticesAsync()).Items.ShouldHaveSingleItem().Id.ShouldBe("service:failed:holdings-quotes:2026-10-13");
    }

    [Fact]
    public async Task A_success_after_one_failed_day_means_the_next_failure_is_a_first_failed_day_again()
    {
        _holdings.Targets = FakeHoldingQuotes.Held(open: 1, nse: 1);
        _source.Respond = _ => QuoteResult.Failed(new QuoteFailure(QuoteFailureKind.Failed, "Dhan's quote service answered HTTP 503."), 1);
        await RunAsync();
        _source.Respond = ids => new QuoteResult([.. ids.Select(i => new SecurityQuote(i, 10m, 9m))], null, 1);
        _world.SetClock(At(13, 10, 0));
        await RunAsync();
        _source.Respond = _ => QuoteResult.Failed(new QuoteFailure(QuoteFailureKind.Failed, "Dhan's quote service answered HTTP 503."), 1);
        _world.SetClock(At(14, 10, 0));

        var run = await RunAsync();

        run.Status.ShouldBe(ServiceRunStatus.Skipped);
        _health.Of(_account.State.AccountId!.Value, SyncHealthKind.Quotes).ConsecutiveFailedDays.ShouldBe(1);
    }

    private async Task<ServiceNotices> NoticesAsync() =>
        await _world.WithScopeAsync(async sp => await Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
            .GetRequiredService<XDrishti.Application.Abstractions.Messaging.IQueryHandler<GetServiceNoticesQuery, ServiceNotices>>(sp).HandleAsync(new GetServiceNoticesQuery(), Ct));

    [Fact]
    public async Task An_answer_without_one_usable_price_fails_the_run()
    {
        _holdings.Targets = FakeHoldingQuotes.Held(open: 2, nse: 2);
        _source.Respond = _ => new QuoteResult([], null, 1);

        var run = await RunAsync();

        (run.Status, run.Message).ShouldBe((ServiceRunStatus.Failed, HoldingQuotesService.NoUsableQuotesMessage));
        _holdings.Applied.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_failed_run_raises_the_service_notice_and_the_next_success_clears_it()
    {
        _holdings.Targets = FakeHoldingQuotes.Held(open: 1, nse: 1);
        _source.Respond = _ => QuoteResult.Failed(new QuoteFailure(QuoteFailureKind.Unauthorized, "The Data account's token was refused by Dhan."), 1);
        var failed = await RunAsync();

        var notices = await _world.WithScopeAsync(async sp => await Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
            .GetRequiredService<XDrishti.Application.Abstractions.Messaging.IQueryHandler<GetServiceNoticesQuery, ServiceNotices>>(sp).HandleAsync(new GetServiceNoticesQuery(), Ct));

        notices.Items.ShouldHaveSingleItem().Id.ShouldBe("service:failed:holdings-quotes:2026-10-12");
        failed.Status.ShouldBe(ServiceRunStatus.Failed);

        _source.Respond = ids => new QuoteResult([.. ids.Select(i => new SecurityQuote(i, 10m, 9m))], null, 1);
        _world.SetClock(At(12, 10, 5));
        await RunAsync();
        notices = await _world.WithScopeAsync(async sp => await Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
            .GetRequiredService<XDrishti.Application.Abstractions.Messaging.IQueryHandler<GetServiceNoticesQuery, ServiceNotices>>(sp).HandleAsync(new GetServiceNoticesQuery(), Ct));
        notices.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Run_now_works_outside_the_window()
    {
        _world.SetClock(At(11, 23, 0)); // Sunday night
        _holdings.Targets = FakeHoldingQuotes.Held(open: 1, nse: 1);

        var run = await RunAsync();

        (run.Trigger, run.Status).ShouldBe((ServiceRunTrigger.Manual, ServiceRunStatus.Succeeded));
    }
}
