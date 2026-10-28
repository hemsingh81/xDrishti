using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;

namespace XDrishti.Api.IntegrationTests.LiveTrading;

/// <summary>P11-06 on a real database: the phase check answers an empty database with state empty and NOT EVALUABLE, 404 without a live account and 422 for a date after the last closed session.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class LivePhaseCheckApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly Guid LiveAccount = Guid.Parse("0b6f2a9e-7a52-4d7e-9c3a-5d0a7e0b7a71");

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await using var connection = await OpenAsync();
        await ExecuteAsync(
            connection,
            "INSERT INTO accounts.broker_accounts (id, slug, name, broker, client_id, api_key_issued_on, color, created_at, updated_at, data_enabled, data_primary, data_standby, live_feed_budget) VALUES (@id, 'probe-report', 'Probe', 'Dhan', '1000000082', current_date, '#1A2B3C', now(), now(), false, false, false, 200)",
            ("id", LiveAccount));
        await ExecuteAsync(connection, "INSERT INTO accounts.trading_settings (account_id, enabled, mode, capital_source, capital_amount, intraday, swing, intraday_instruments, swing_buy_instruments, swing_sell_instruments) VALUES (@id, true, 'ManualLive', 'Fixed', 100000, true, false, 1, 0, 0)", ("id", LiveAccount));
        await ExecuteAsync(connection, "INSERT INTO accounts.portfolio_settings (account_id, enabled, include_in_totals) VALUES (@id, false, true)", ("id", LiveAccount));
    }

    [Fact]
    public async Task An_empty_database_gives_an_empty_not_evaluable_report_and_never_met()
    {
        var report = await Client.GetFromJsonAsync<JsonElement>(new Uri("/api/live-trading/phase-check", UriKind.Relative), Ct);

        report.GetProperty("state").GetString().ShouldBe("empty");
        report.GetProperty("report").GetProperty("exitVerdict").GetString().ShouldBe("NOT EVALUABLE");
        report.GetProperty("report").GetProperty("criteria").GetArrayLength().ShouldBe(9);
        report.GetProperty("report").GetProperty("limits").GetArrayLength().ShouldBeGreaterThan(0);
        report.GetProperty("text").GetString()!.ShouldContain("NOT EVALUABLE");
    }

    [Fact]
    public async Task Unknown_accounts_are_404_and_a_date_after_the_last_closed_session_is_422()
    {
        using var missing = await Client.GetAsync(new Uri($"/api/live-trading/phase-check?accountId={Guid.NewGuid()}", UriKind.Relative), Ct);
        using var later = await Client.GetAsync(new Uri("/api/live-trading/phase-check?through=2999-01-01", UriKind.Relative), Ct);

        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        ((int)later.StatusCode).ShouldBe(422);
    }
}
