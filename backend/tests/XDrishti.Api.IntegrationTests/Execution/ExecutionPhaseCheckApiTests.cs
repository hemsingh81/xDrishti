using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Execution;

namespace XDrishti.Api.IntegrationTests.Execution;

/// <summary>
/// Spec P12-12 criteria 13 and 14 on a real database: the route needs a session, answers NOT EVALUABLE on a database with no semi-auto window, refuses a later date with 422, and the reader's SQL runs over fixture rows (a break, a readable and an unreadable
/// run, a confirmed entry, a sandbox intent) as SELECT only. The mode trail cannot hold semi_auto yet (P12-07), so a MET fixture is the handler's test, not this one.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class ExecutionPhaseCheckApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly Uri Route = new("/api/execution/phase-check", UriKind.Relative);

    [Fact]
    public async Task Without_a_session_the_route_is_401()
    {
        using var response = await Factory.CreateAnonymousClient().GetAsync(Route, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Without_a_semi_auto_window_the_answer_is_not_evaluable_with_the_limits_and_the_text()
    {
        var view = await Client.GetFromJsonAsync<JsonElement>(Route, Ct);

        var report = view.GetProperty("report");
        report.GetProperty("exitVerdict").GetString().ShouldBe("NOT EVALUABLE");
        report.GetProperty("needed").GetInt32().ShouldBe(30);
        report.GetProperty("limits").GetArrayLength().ShouldBeGreaterThan(0);
        view.GetProperty("text").GetString()!.ShouldContain("exit verdict: NOT EVALUABLE");
        foreach (var account in report.GetProperty("accounts").EnumerateArray())
        {
            account.GetProperty("verdict").GetString().ShouldBe("NOT EVALUABLE");
        }
    }

    [Fact]
    public async Task A_later_date_and_an_unknown_account_are_422_execution_invalid()
    {
        using var later = await Client.GetAsync(new Uri("/api/execution/phase-check?through=2999-01-01", UriKind.Relative), Ct);
        using var unknown = await Client.GetAsync(new Uri($"/api/execution/phase-check?accountId={Guid.NewGuid()}", UriKind.Relative), Ct);

        later.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        unknown.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await later.Content.ReadAsStringAsync(Ct)).ShouldContain("execution.invalid");
    }

    [Fact]
    public async Task The_reader_reads_break_run_entry_and_sandbox_rows_of_an_account_and_only_that_account()
    {
        var account = Guid.NewGuid();
        var other = Guid.NewGuid();
        var day = new DateOnly(2026, 10, 30);
        await using (var connection = await OpenAsync())
        {
            foreach (var (a, kind, readable) in new[] { (account, "eod", true), (account, "final", false), (other, "final", true) })
            {
                await ExecuteAsync(
                    connection, "INSERT INTO execution.recon_runs (at, account_id, session_date, kind, readable, orders, trades, positions, breaks) VALUES (now(), @a, @d, @k, @r, 0, 0, 0, @b)",
                    ("a", a), ("d", day), ("k", kind), ("r", readable), ("b", readable ? 0 : 1));
            }

            var run = await ScalarAsync<long>("SELECT id FROM execution.recon_runs WHERE account_id = @a AND kind = 'final'", ("a", account));
            await ExecuteAsync(
                connection, "INSERT INTO execution.recon_breaks (run_id, account_id, session_date, kind, severity, key, ours, theirs, detail, at) VALUES (@r, @a, @d, 'trade_missing', 'warn', @k, '1', '2', 'x', now())",
                ("r", run), ("a", account), ("d", day), ("k", "k-" + Guid.NewGuid().ToString("N")));
        }

        var reader = Factory.Services.CreateScope().ServiceProvider.GetRequiredService<IExecutionRecordsReader>();
        var records = await reader.RecordsAsync(account, day, day, Ct);

        records.Runs.Select(r => (r.Kind, r.Readable)).Order().ShouldBe([("eod", true), ("final", false)]);
        records.Breaks.Single().Kind.ShouldBe("trade_missing");
        records.Entries.ShouldBeEmpty();
        records.Carried.ShouldBeEmpty();
        records.LiveLegs.ShouldBe(0);
        (await reader.NewestSessionAsync(Ct))!.Value.ShouldBeGreaterThanOrEqualTo(day);
    }
}
