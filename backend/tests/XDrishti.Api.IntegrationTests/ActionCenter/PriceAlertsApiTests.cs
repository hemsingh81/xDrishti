using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio;
using XDrishti.Application.Portfolio.Alerts;
using XDrishti.Domain.Common;

namespace XDrishti.Api.IntegrationTests.ActionCenter;

/// <summary>
/// Price alerts end to end (P2-09, criteria 13-16) on the real database with the real quote client over the fake Dhan: create, list, cancel, the quote poll that checks them with the holdings in one request, the trigger with its audit event, and the notices.
/// Clock: Monday 2026-10-12 10:00 India time. NEWCO is in the master but not held (security 2002).
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class PriceAlertsApiTests(ApiFactory factory) : ActionCenterTestBase(factory)
{
    private const string QuotePath = "/v2/marketfeed/quote";

    private Guid _newco;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        _newco = await Factory.SeedInstrumentAsync("NEWCO", "INE000N01099", "2002", "New Company Limited", Ct);
    }

    /// <summary>The pinned clock may run a little between the pin and a request.</summary>
    private static bool IsAround(DateTimeOffset? actual, DateTimeOffset expected) => actual is { } at && Math.Abs((at - expected).TotalMinutes) < 1;

    private List<FakeBrokers.RecordedRequest> QuoteRequests => [.. Dhan.Requests.Where(r => r.Uri.AbsolutePath == QuotePath)];

    private static string[] AskedSecurities(FakeBrokers.RecordedRequest request)
    {
        using var body = JsonDocument.Parse(request.Body!);
        return [.. body.RootElement.GetProperty("NSE_EQ").EnumerateArray().Select(e => e.GetInt64().ToString(System.Globalization.CultureInfo.InvariantCulture)).Order()];
    }

    private void QuoteAll(decimal aaa = 70m, decimal? newco = null)
    {
        Dhan.Quotes["1001"] = (aaa, 70m);
        Dhan.Quotes["1002"] = (100m, 100m);
        Dhan.Quotes["1003"] = (140m, 140m);
        if (newco is { } price)
        {
            Dhan.Quotes["2002"] = (price, price);
        }
    }

    [Fact]
    public async Task An_alert_is_created_active_listed_and_audited()
    {
        using var response = await CreateAlertAsync(Aaa, "at_or_above", 80m, "take profit");

        var alert = await response.ReadAsync<PriceAlertView>(Ct);

        (alert.InstrumentId, alert.Symbol, alert.Name, alert.Direction, alert.Level, alert.Note, alert.Status).ShouldBe((Aaa, "AAA", "AAA Limited", "at_or_above", 80m, "take profit", "active"));
        (alert.TriggeredAt, alert.TriggerPrice).ShouldBe((null, null));
        var page = await AlertsAsync();
        page.Items.Select(a => a.Id).ShouldBe([alert.Id]);
        (page.Total, page.ActiveCount, page.ActiveLimit).ShouldBe((1, 1, 200));
        (await EventsAsync("PriceAlertCreated")).ShouldBe(1);
        (await TextAsync("SELECT detail FROM accounts.account_events WHERE event = 'PriceAlertCreated'")).ShouldBe("Price alert created: AAA at or above ₹80");
    }

    [Fact]
    public async Task A_condition_that_already_holds_at_the_holdings_last_price_is_refused_naming_the_price()
    {
        using var response = await CreateAlertAsync(Aaa, "at_or_below", 75m); // AAA's last price is 70

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var errors = await response.ReadFieldErrorsAsync(Ct);
        errors.Keys.ShouldBe(["level"]);
        errors["level"].Single().ShouldContain("₹70");
        (await AlertsAsync()).Total.ShouldBe(0);
    }

    [Fact]
    public async Task The_last_stored_daily_close_is_the_reference_for_an_instrument_that_is_not_held()
    {
        await StoreDailyAsync(_newco, (Oct(9), 50.0));

        using var refused = await CreateAlertAsync(_newco, "at_or_above", 45m);
        using var allowed = await CreateAlertAsync(_newco, "at_or_above", 55m);

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await refused.ReadFieldErrorsAsync(Ct))["level"].Single().ShouldContain("₹50");
        allowed.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_stored_close_older_than_five_trading_days_is_not_a_reference_price()
    {
        await StoreDailyAsync(_newco, (Oct(1), 50.0)); // 7 trading days before Monday 12 October

        using var response = await CreateAlertAsync(_newco, "at_or_above", 45m);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Without_any_known_price_the_alert_is_allowed()
    {
        using var response = await CreateAlertAsync(_newco, "at_or_below", 10m);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Only_nse_shares_and_etfs_that_are_listed_and_quotable_can_have_an_alert()
    {
        var index = await IndexAsync("NSE:NIFTY", "Nifty 50");
        var etf = await Factory.SeedInstrumentAsync("NIFTYBEES", "INE000E01011", "3001", "Nifty ETF", Ct);
        await ExecuteAsync("UPDATE market.instruments SET type = 'etf' WHERE id = @id", ("id", etf));
        var retired = await Factory.SeedInstrumentAsync("OLDCO", "INE000R01011", "3002", "Retired Limited", Ct);
        await ExecuteAsync("UPDATE market.instruments SET retired_at = now() WHERE id = @id", ("id", retired));
        var unquotable = await Factory.SeedInstrumentAsync("NOID", "INE000U01011", "3003", "No Id Limited", Ct);
        await ExecuteAsync("DELETE FROM market.instrument_broker_ids WHERE instrument_id = @id", ("id", unquotable));

        using var onIndex = await CreateAlertAsync(index, "at_or_below", 1m);
        using var onEtf = await CreateAlertAsync(etf, "at_or_below", 1m);
        using var onRetired = await CreateAlertAsync(retired, "at_or_below", 1m);
        using var onUnquotable = await CreateAlertAsync(unquotable, "at_or_below", 1m);
        using var unknown = await CreateAlertAsync(Guid.NewGuid(), "at_or_below", 1m);

        onIndex.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        onEtf.StatusCode.ShouldBe(HttpStatusCode.OK);
        onRetired.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        onUnquotable.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        unknown.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await onIndex.ReadFieldErrorsAsync(Ct)).Keys.ShouldBe(["instrumentId"]);
    }

    [Fact]
    public async Task Bad_values_are_400_with_a_field_error_each()
    {
        using var response = await Client.PostJsonAsync("/api/portfolio/alerts", new { instrumentId = Aaa, direction = "sideways", level = 0, note = new string('x', 201) }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadProblemAsync(Ct)).Code.ShouldBe("portfolio.invalid");
        (await response.ReadFieldErrorsAsync(Ct)).Keys.ShouldBe(["direction", "level", "note"], ignoreOrder: true);
    }

    [Fact]
    public async Task At_most_200_alerts_are_active_and_the_201st_is_a_409_with_its_code()
    {
        await ExecuteAsync(
            """
            INSERT INTO portfolio.price_alerts (id, instrument_id, direction, level, status, created_at, created_by)
            SELECT gen_random_uuid(), @id, 'at_or_below', 1 + n, 'active', now(), 'seed' FROM generate_series(1, 199) n
            """,
            ("id", _newco));

        using var two00 = await CreateAlertAsync(_newco, "at_or_below", 5000m);
        using var two01 = await CreateAlertAsync(_newco, "at_or_below", 5001m);

        two00.StatusCode.ShouldBe(HttpStatusCode.OK);
        two01.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await two01.ReadProblemAsync(Ct)).Code.ShouldBe("portfolio.alert_limit");
        (await AlertsAsync("?status=active")).ActiveCount.ShouldBe(200);
    }

    [Fact]
    public async Task Cancelling_is_idempotent_and_a_triggered_alert_stays_triggered()
    {
        var active = await CreatedAlertAsync(_newco, "at_or_below", 10m);
        var willTrigger = await CreatedAlertAsync(Aaa, "at_or_above", 80m);
        QuoteAll(aaa: 85m);
        (await RunQuotesAsync()).Status.ShouldBe("succeeded");

        using var first = await Client.PostAsync($"/api/portfolio/alerts/{active.Id}/cancel", Ct);
        using var second = await Client.PostAsync($"/api/portfolio/alerts/{active.Id}/cancel", Ct);
        using var triggered = await Client.PostAsync($"/api/portfolio/alerts/{willTrigger.Id}/cancel", Ct);
        using var unknown = await Client.PostAsync($"/api/portfolio/alerts/{Guid.NewGuid()}/cancel", Ct);

        (await first.ReadAsync<PriceAlertView>(Ct)).Status.ShouldBe("cancelled");
        (await second.ReadAsync<PriceAlertView>(Ct)).Status.ShouldBe("cancelled");
        var kept = await triggered.ReadAsync<PriceAlertView>(Ct);
        (kept.Status, kept.TriggerPrice).ShouldBe(("triggered", (decimal?)85m));
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await EventsAsync("PriceAlertCancelled")).ShouldBe(1);
        (await AlertsAsync("?status=cancelled")).Items.Select(a => a.Id).ShouldBe([active.Id]);
    }

    [Fact]
    public async Task The_list_filters_by_status_newest_first_and_pages()
    {
        var first = await CreatedAlertAsync(Aaa, "at_or_above", 81m);
        Factory.Clock.Pin(MondayMorning.AddMinutes(1));
        var second = await CreatedAlertAsync(Aaa, "at_or_above", 82m);

        (await AlertsAsync()).Items.Select(a => a.Id).ShouldBe([second.Id, first.Id]);
        (await AlertsAsync("?pageSize=1&page=2")).Items.Select(a => a.Id).ShouldBe([first.Id]);
        (await AlertsAsync("?status=triggered")).Total.ShouldBe(0);
        using var bad = await Client.GetAsync("/api/portfolio/alerts?status=nope", Ct);
        bad.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // ---- the quote poll ----

    [Fact]
    public async Task The_tick_quotes_the_holdings_and_the_alert_instruments_in_one_request_and_triggers_the_alerts_that_are_hit()
    {
        var held = await CreatedAlertAsync(Aaa, "at_or_above", 80m);
        var notHeld = await CreatedAlertAsync(_newco, "at_or_below", 50m);
        var far = await CreatedAlertAsync(Ccc, "at_or_above", 500m);
        QuoteAll(aaa: 85m, newco: 48m);

        var run = await RunQuotesAsync();

        (run.Status, run.Message).ShouldBe(("succeeded", "Quoted 3 of 3 holdings; 0 not on NSE; 2 alerts triggered"));
        AskedSecurities(QuoteRequests.ShouldHaveSingleItem()).ShouldBe(["1001", "1002", "1003", "2002"]);
        var triggered = await AlertsAsync("?status=triggered");
        triggered.Items.Select(a => (a.Id, a.TriggerPrice)).ShouldBe([(held.Id, (decimal?)85m), (notHeld.Id, (decimal?)48m)], ignoreOrder: true);
        triggered.Items.ShouldAllBe(a => IsAround(a.TriggeredAt, MondayMorning));
        (await AlertsAsync("?status=active")).Items.Select(a => a.Id).ShouldBe([far.Id]);
        (await EventsAsync("PriceAlertTriggered")).ShouldBe(2);
        (await CountAsync($"SELECT count(*) FROM accounts.account_events WHERE event = 'PriceAlertTriggered' AND actor = 'system:holdings-quotes'")).ShouldBe(2);
        (await ActionsAsync()).Items.Single(i => i.Rule == "below_cost").Numbers.Value.ShouldBe(8500m); // the holdings were updated by the same answer
    }

    [Fact]
    public async Task An_alert_triggers_once_and_the_next_tick_neither_repeats_nor_quotes_it()
    {
        await CreatedAlertAsync(_newco, "at_or_below", 50m);
        QuoteAll(newco: 48m);
        await RunQuotesAsync();

        var run = await RunQuotesAsync();

        run.Message.ShouldBe("Quoted 3 of 3 holdings; 0 not on NSE");
        AskedSecurities(QuoteRequests.Last()).ShouldBe(["1001", "1002", "1003"]);
        (await EventsAsync("PriceAlertTriggered")).ShouldBe(1);
    }

    [Fact]
    public async Task An_alert_without_a_quote_in_the_answer_stays_active()
    {
        var alert = await CreatedAlertAsync(_newco, "at_or_below", 50m);
        QuoteAll(); // Dhan leaves 2002 out of its answer

        var run = await RunQuotesAsync();

        run.Message.ShouldBe("Quoted 3 of 3 holdings; 0 not on NSE");
        (await AlertsAsync("?status=active")).Items.Select(a => a.Id).ShouldBe([alert.Id]);
    }

    [Fact]
    public async Task A_failed_run_triggers_nothing()
    {
        var alert = await CreatedAlertAsync(Aaa, "at_or_above", 80m);
        QuoteAll(aaa: 85m);
        Dhan.Override(QuotePath, _ => FakeBrokers.Json(HttpStatusCode.ServiceUnavailable, "{}"));

        var run = await RunQuotesAsync();

        run.Status.ShouldBe("failed");
        (await AlertsAsync("?status=active")).Items.Select(a => a.Id).ShouldBe([alert.Id]);
        (await EventsAsync("PriceAlertTriggered")).ShouldBe(0);
    }

    [Fact]
    public async Task Without_holdings_an_alert_is_still_quoted_and_checked()
    {
        await ExecuteAsync("UPDATE portfolio.holdings SET status = 'closed'");
        var alert = await CreatedAlertAsync(_newco, "at_or_below", 50m);
        QuoteAll(newco: 49m);

        var run = await RunQuotesAsync();

        (run.Status, run.Message).ShouldBe(("succeeded", "No open holdings; 1 alert triggered"));
        AskedSecurities(QuoteRequests.ShouldHaveSingleItem()).ShouldBe(["2002"]);
        (await AlertsAsync("?status=triggered")).Items.Single().Id.ShouldBe(alert.Id);
    }

    [Fact]
    public async Task Two_runs_that_race_for_the_same_alert_trigger_it_once_with_one_audit_event()
    {
        var alert = await CreatedAlertAsync(_newco, "at_or_below", 50m);

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 8).Select(async i =>
        {
            using var scope = Factory.Services.CreateScope();
            var unit = scope.ServiceProvider.GetRequiredService<IPortfolioUnitOfWork>();
            var alerts = scope.ServiceProvider.GetRequiredService<IPriceAlerts>();
            var won = false;
            var result = await unit.RunAsync(
                async token =>
                {
                    won = await alerts.TryTriggerAsync(alert.Id, 40m + i, MondayMorning, token);
                    return Result.Success();
                },
                Ct);
            result.IsSuccess.ShouldBeTrue();
            return won;
        }));

        outcomes.Count(o => o).ShouldBe(1);
        (await AlertsAsync("?status=triggered")).Items.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task The_trigger_is_rolled_back_when_the_transaction_it_runs_in_is_not_committed()
    {
        var alert = await CreatedAlertAsync(_newco, "at_or_below", 50m);

        using (var scope = Factory.Services.CreateScope())
        {
            var unit = scope.ServiceProvider.GetRequiredService<IPortfolioUnitOfWork>();
            var alerts = scope.ServiceProvider.GetRequiredService<IPriceAlerts>();
            var result = await unit.RunAsync(
                async token =>
                {
                    (await alerts.TryTriggerAsync(alert.Id, 40m, MondayMorning, token)).ShouldBeTrue();
                    return Result.Failure(new Error("test.abort", "the audit event could not be written"));
                },
                Ct);
            result.IsFailure.ShouldBeTrue();
        }

        (await AlertsAsync("?status=active")).Items.Select(a => a.Id).ShouldBe([alert.Id]);
    }

    // ---- the notices ----

    [Fact]
    public async Task A_triggered_alert_raises_a_warning_notice_with_its_id_and_the_open_items_raise_the_daily_information()
    {
        var alert = await CreatedAlertAsync(_newco, "at_or_below", 50m);
        QuoteAll(newco: 48m);
        await RunQuotesAsync();

        var notices = await NoticesAsync();

        var warning = notices.Items.Single(n => n.Kind == "price_alert_triggered");
        (warning.Id, warning.Severity, warning.AlertId, warning.InstrumentId).ShouldBe(($"portfolio:alert:{alert.Id}", "warn", (Guid?)alert.Id, (Guid?)_newco));
        IsAround(warning.At, MondayMorning).ShouldBeTrue();
        warning.Text.ShouldBe("NEWCO is at or below ₹50 (price ₹48)");
        var info = notices.Items.Single(n => n.Kind == "actions_need_decision");
        (info.Id, info.Severity, info.Text).ShouldBe(("portfolio:actions:2026-10-12", "info", "6 holdings need a decision"));
    }

    [Fact]
    public async Task The_alert_notice_goes_after_seven_days()
    {
        await CreatedAlertAsync(_newco, "at_or_below", 50m);
        QuoteAll(newco: 48m);
        await RunQuotesAsync();
        Factory.Clock.Pin(MondayMorning.AddDays(7).AddMinutes(1));
        var later = await Factory.SignInAsync(Ct);

        using var response = await later.Client.GetAsync("/api/portfolio/notices", Ct);

        (await response.ReadAsync<PortfolioNotices>(Ct)).Items.Select(n => n.Kind).ShouldNotContain("price_alert_triggered");
    }
}
