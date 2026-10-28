using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.LiveTrading;

namespace XDrishti.Api.IntegrationTests.LiveTrading;

/// <summary>
/// P11-03 on a real database: the two report endpoints answer an empty database, 404 for an unknown account and 422 for a bad input; the paper-side SQL runs against the real schema (no epoch gives no accounts, an unknown epoch gives no trades); two identical calls are byte-identical.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class LiveReportApiTests(ApiFactory factory) : DataTestBase(factory)
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
    public async Task Both_reports_answer_an_empty_database_with_their_evidence_and_are_deterministic()
    {
        var uri = new Uri($"/api/live-trading/reports/live-vs-paper?accountId={LiveAccount}", UriKind.Relative);
        var first = await Client.GetStringAsync(uri, Ct);
        var second = await Client.GetStringAsync(uri, Ct);
        var report = JsonDocument.Parse(first).RootElement;
        var gap = await Client.GetFromJsonAsync<JsonElement>(new Uri($"/api/live-trading/reports/execution-gap?accountId={LiveAccount}&groupBy=month", UriKind.Relative), Ct);

        second.ShouldBe(first);
        report.GetProperty("state").GetString().ShouldBe("empty");
        report.GetProperty("summary").GetProperty("verdict").GetString().ShouldBe("insufficient");
        report.GetProperty("summary").GetProperty("mean").GetProperty("number").ValueKind.ShouldBe(JsonValueKind.Null);
        report.GetProperty("evidence").GetProperty("basis").GetString().ShouldBe("gross");
        report.GetProperty("evidence").GetProperty("limits").GetArrayLength().ShouldBeGreaterThan(0);
        gap.GetProperty("state").GetString().ShouldBe("empty");
        gap.GetProperty("groupBy").GetString().ShouldBe("month");
        gap.GetProperty("evidence").GetProperty("limits").GetArrayLength().ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Unknown_accounts_are_404_and_bad_inputs_are_422()
    {
        using var missing = await Client.GetAsync(new Uri($"/api/live-trading/reports/live-vs-paper?accountId={Guid.NewGuid()}", UriKind.Relative), Ct);
        using var basis = await Client.GetAsync(new Uri($"/api/live-trading/reports/live-vs-paper?accountId={LiveAccount}&basis=weird", UriKind.Relative), Ct);
        using var kind = await Client.GetAsync(new Uri($"/api/live-trading/reports/live-vs-paper?accountId={LiveAccount}&referenceKind=other", UriKind.Relative), Ct);
        using var range = await Client.GetAsync(new Uri($"/api/live-trading/reports/live-vs-paper?accountId={LiveAccount}&from=2024-01-01&to=2026-01-01", UriKind.Relative), Ct);
        using var groupBy = await Client.GetAsync(new Uri($"/api/live-trading/reports/execution-gap?accountId={LiveAccount}&groupBy=tag", UriKind.Relative), Ct);
        using var gapMissing = await Client.GetAsync(new Uri($"/api/live-trading/reports/execution-gap?accountId={Guid.NewGuid()}", UriKind.Relative), Ct);

        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        gapMissing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        ((int)basis.StatusCode).ShouldBe(422);
        ((int)kind.StatusCode).ShouldBe(422);
        ((int)range.StatusCode).ShouldBe(422);
        ((int)groupBy.StatusCode).ShouldBe(422);
    }

    [Fact]
    public async Task The_paper_side_reads_run_against_the_real_schema()
    {
        using var scope = Factory.Services.CreateScope();
        var source = scope.ServiceProvider.GetRequiredService<IPaperTradeSource>();

        var accounts = await source.AccountsAsync(Ct);
        var trades = await source.TradesAsync(999_999, LiveAccount, [1, 2, 3], Ct);

        accounts.Available.ShouldBeTrue();
        accounts.Accounts.ShouldBeEmpty();
        trades.Available.ShouldBeTrue();
        trades.Trades.ShouldBeEmpty();
        trades.SkipCodes.ShouldBeEmpty();
    }
}
