using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Replay;
using XDrishti.Application.Replay;
using XDrishti.Domain.Replay;

namespace XDrishti.Api.IntegrationTests.Replay;

/// <summary>
/// SR-04 part A over a real TimescaleDB: the owner-only simulation routes, the refusals with their status and code (422 sim.invalid, 409 sim.busy, sim.not_settled, sim.not_complete, sim.final), settle, advance, the day read, auto and cancel, and the database rules of the two tables (one active run, a
/// run keeps what it was started with, a snapshot is immutable). The runner is off in the tests, so a settled day is stored through the store port, as the runner would leave it.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class SimDaysApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly DateOnly D1 = new(2026, 10, 5);
    private static readonly DateOnly D2 = new(2026, 10, 6);
    private static readonly DateTimeOffset T0 = new(2026, 10, 12, 6, 30, 0, TimeSpan.Zero);

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, "DELETE FROM research.sim_days_snapshots; DELETE FROM research.sim_days_runs;");
    }

    private static NewSimDaysRun New()
    {
        var p = new ReplayParams(D1, D2, new ReplayScope([Guid.NewGuid()], []), 1_000_000m, ReplayRiskProfile.Standard, null, ReplayVersionRule.Start, ReplayMode.Learning);
        return new NewSimDaysRun(Guid.NewGuid(), new string('a', 64), ReplayJson.Canonical(p), """{"fill":{}}""", "{}", null, 1_000_000m, "learning", D1, D2, D2, D1, null, null, T0);
    }

    private static SimDayPending Pending(DateOnly day)
    {
        var record = new ReplaySessionRecord(day, [], [], [new ReplayCandidateRow("AAA", "BUY", "S1|1D|BUY|swing|FIXED_RR|*", 1)], [new ReplayBlocked("BBB", "S1|1D|BUY|swing|FIXED_RR|*", BlockReason.MaxPositions, "full")], [], [], 1_000_500m, 0m, 0m, 1, [], []);
        var text = ReplayJson.Canonical(record);
        return new SimDayPending(day, text, "[]", 1_000_500m, ReplayJson.Sha256(text), D2);
    }

    private async Task<T> WithStoreAsync<T>(Func<ISimDaysStore, Task<T>> action)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<ISimDaysStore>());
    }

    private async Task<Guid> StoredAsync()
    {
        var run = New();
        (await WithStoreAsync(s => s.InsertAsync(run, Ct).AsTask())).ShouldBeTrue();
        return run.Id;
    }

    private async Task<(HttpStatusCode Status, JsonElement Body)> PostAsync(string path, object? body = null)
    {
        using var response = body is null ? await Client.PostAsync(new Uri(path, UriKind.Relative), null, Ct) : await Client.PostAsJsonAsync(new Uri(path, UriKind.Relative), body, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    private static string Code(JsonElement body) => body.GetProperty("code").GetString()!;

    [Fact]
    public async Task Every_simulation_route_needs_the_owners_session()
    {
        using var anonymous = Factory.CreateAnonymousClient();
        var id = Guid.NewGuid();

        (await anonymous.GetAsync(new Uri($"/api/sim/days/{id}", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync(new Uri($"/api/sim/days/{id}/days/2026-10-05", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsJsonAsync(new Uri("/api/sim/days", UriKind.Relative), new { start = "2026-10-05" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        foreach (var action in new[] { "settle", "advance", "continue", "cancel" })
        {
            (await anonymous.PostAsync(new Uri($"/api/sim/days/{id}/{action}", UriKind.Relative), null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized, action);
        }
    }

    [Theory]
    [InlineData("""{"start":"2026-10-05","scope":{}}""")]
    [InlineData("""{"start":"2026-10-05","scope":{"instrumentIds":["00000000-0000-0000-0000-0000000000a1"]},"capital":0}""")]
    [InlineData("""{"start":"2026-10-05","scope":{"instrumentIds":["00000000-0000-0000-0000-0000000000a1"]},"mode":"later"}""")]
    [InlineData("""{"start":"2026-10-05","scope":{"instrumentIds":["00000000-0000-0000-0000-0000000000a1"]}}""")] // no bar is stored in this database: the start is before the first bar plus the warm-up
    public async Task A_start_that_cannot_be_used_is_422_sim_invalid(string json)
    {
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        using var response = await Client.PostAsync(new Uri("/api/sim/days", UriKind.Relative), content, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity, await response.Content.ReadAsStringAsync(Ct));
        Code(await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).ShouldBe("sim.invalid");
    }

    [Fact]
    public async Task A_second_active_simulation_is_409_sim_busy_and_the_database_refuses_it_too()
    {
        await StoredAsync();

        var (status, body) = await PostAsync("/api/sim/days", new { start = "2026-10-05", scope = new { instrumentIds = new[] { Guid.NewGuid() } } });

        (status, Code(body)).ShouldBe((HttpStatusCode.Conflict, "sim.busy"));
        (await WithStoreAsync(s => s.InsertAsync(New(), Ct).AsTask())).ShouldBeFalse("the partial unique index admits one active run");
    }

    [Fact]
    public async Task Settle_advance_and_the_day_read_move_the_cursor_and_store_an_immutable_day()
    {
        var id = await StoredAsync();

        var early = await PostAsync($"/api/sim/days/{id}/advance");
        var settle = await PostAsync($"/api/sim/days/{id}/settle");
        (await WithStoreAsync(s => s.SettleAsync(id, Pending(D1), T0, Ct).AsTask())).ShouldBeTrue();
        var pending = await Client.GetFromJsonAsync<JsonElement>(new Uri($"/api/sim/days/{id}/days/2026-10-05", UriKind.Relative), Ct);
        var again = await PostAsync($"/api/sim/days/{id}/settle");
        var advanced = await PostAsync($"/api/sim/days/{id}/advance");
        var day = await Client.GetFromJsonAsync<JsonElement>(new Uri($"/api/sim/days/{id}/days/2026-10-05", UriKind.Relative), Ct);

        (early.Status, Code(early.Body)).ShouldBe((HttpStatusCode.Conflict, "sim.not_settled"));
        (settle.Status, settle.Body.GetProperty("phase").GetString()).ShouldBe((HttpStatusCode.Accepted, "settling"));
        pending.GetProperty("stored").GetBoolean().ShouldBeFalse();
        (again.Status, Code(again.Body)).ShouldBe((HttpStatusCode.Conflict, "sim.busy"));
        advanced.Status.ShouldBe(HttpStatusCode.OK);
        advanced.Body.GetProperty("cursor").GetString().ShouldBe("2026-10-06");
        advanced.Body.GetProperty("phase").GetString().ShouldBe("idle");
        advanced.Body.GetProperty("daysDone").GetInt32().ShouldBe(1);
        advanced.Body.GetProperty("equity").GetDecimal().ShouldBe(1_000_500m);
        advanced.Body.GetProperty("costsLabel").GetString()!.ShouldStartWith("gross");
        advanced.Body.GetProperty("hindsight").GetArrayLength().ShouldBeGreaterThan(0);
        day.GetProperty("stored").GetBoolean().ShouldBeTrue();
        day.GetProperty("picks")[0].GetProperty("symbol").GetString().ShouldBe("AAA");
        day.GetProperty("blocked")[0].GetProperty("reason").GetString().ShouldBe("max_positions");
    }

    [Fact]
    public async Task The_last_day_completes_the_run_and_continue_cancel_and_auto_answer_by_phase()
    {
        var id = await StoredAsync();
        var notComplete = await PostAsync($"/api/sim/days/{id}/continue");
        await WithStoreAsync(async s =>
        {
            foreach (var day in new[] { D1, D2 })
            {
                (await s.MovePhaseAsync(id, "idle", "settling", T0, Ct)).ShouldBeTrue();
                (await s.SettleAsync(id, Pending(day), T0, Ct)).ShouldBeTrue();
                (await PostAsync($"/api/sim/days/{id}/advance")).Status.ShouldBe(HttpStatusCode.OK);
            }

            return 0;
        });

        var status = await Client.GetFromJsonAsync<JsonElement>(new Uri($"/api/sim/days/{id}", UriKind.Relative), Ct);
        var auto = await PostAsync($"/api/sim/days/{id}/auto", new { on = true });
        var cancelled = await PostAsync($"/api/sim/days/{id}/cancel");
        var twice = await PostAsync($"/api/sim/days/{id}/cancel");
        var missing = await PostAsync($"/api/sim/days/{Guid.NewGuid()}/settle");

        (notComplete.Status, Code(notComplete.Body)).ShouldBe((HttpStatusCode.Conflict, "sim.not_complete"));
        (status.GetProperty("phase").GetString(), status.GetProperty("daysDone").GetInt32(), status.GetProperty("daysTotal").GetInt32()).ShouldBe(("complete", 2, 2));
        (auto.Status, Code(auto.Body)).ShouldBe((HttpStatusCode.Conflict, "sim.final"), "auto-run applies to an active run");
        cancelled.Body.GetProperty("phase").GetString().ShouldBe("cancelled");
        (twice.Status, Code(twice.Body)).ShouldBe((HttpStatusCode.Conflict, "sim.final"));
        (missing.Status, Code(missing.Body)).ShouldBe((HttpStatusCode.NotFound, "sim.not_found"));
    }

    [Fact]
    public async Task A_run_keeps_what_it_was_started_with_and_a_cancelled_run_stays_cancelled()
    {
        var id = await StoredAsync();
        await using var connection = await OpenAsync();

        await using (var rewrite = new NpgsqlCommand("UPDATE research.sim_days_runs SET capital = 5 WHERE id = @id", connection))
        {
            rewrite.Parameters.AddWithValue("id", id);
            (await Should.ThrowAsync<PostgresException>(() => rewrite.ExecuteNonQueryAsync(Ct))).SqlState.ShouldBe(PostgresErrorCodes.CheckViolation, "even the owner cannot rewrite what a run was started with (the runtime role cannot update the column at all: ResearchGrantsTests)");
        }

        (await WithStoreAsync(s => s.CancelAsync(id, T0, Ct).AsTask())).ShouldBeTrue();
        await using var reopen = new NpgsqlCommand("UPDATE research.sim_days_runs SET phase = 'idle' WHERE id = @id", connection);
        reopen.Parameters.AddWithValue("id", id);
        (await Should.ThrowAsync<PostgresException>(() => reopen.ExecuteNonQueryAsync(Ct))).SqlState.ShouldBe(PostgresErrorCodes.CheckViolation, "the trigger keeps a cancelled run final");
    }
}
