using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Replay;
using XDrishti.Application.Replay;
using XDrishti.Domain.Replay;

namespace XDrishti.Api.IntegrationTests.Replay;

/// <summary>
/// SR-01 part C over a real TimescaleDB: the owner-only replay routes: the refusals with their status and code, the stored runs read while they run (hindsight labels first, the costs label, completeness, the trial number), the session and trade reads, the cancel and the pair
/// comparison. The job is off in the tests, so runs are stored through the store port, as the job would leave them.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class ReplayApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly DateOnly D1 = new(2026, 10, 5);
    private static readonly DateOnly D2 = new(2026, 10, 6);
    private static readonly DateTimeOffset T0 = new(2026, 10, 12, 6, 30, 0, TimeSpan.Zero);

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, "DELETE FROM research.replay_trades; DELETE FROM research.replay_sessions; DELETE FROM research.replay_runs;");
    }

    private static NewReplayRun New(string mode = "frozen", string? sheet = null, int minutes = 0)
    {
        var p = new ReplayParams(D1, D2, new ReplayScope([Guid.NewGuid()], []), 1_000_000m, ReplayRiskProfile.Standard, sheet, ReplayVersionRule.Start, mode == "learning" ? ReplayMode.Learning : ReplayMode.Frozen);
        return new NewReplayRun(Guid.NewGuid(), D1, D2, mode, ReplayJson.Canonical(p), """{"fill":{}}""", "{}", null, new string('a', 64), new string('b', 64), 2, T0.AddMinutes(minutes));
    }

    private static ReplaySessionRow Session(DateOnly day, decimal equity)
    {
        var record = new ReplaySessionRecord(
            day,
            [new ReplayCandidateRow("AAA", "BUY", "S1|1D|BUY|swing|FIXED_RR|*", 1)],
            [],
            [],
            [new ReplayBlocked("AAA", "S1|1D|BUY|swing|FIXED_RR|*", BlockReason.MaxPositions, "full")],
            [],
            [],
            equity,
            0m,
            0m,
            0,
            ["survivorship"],
            []);
        var text = ReplayJson.Canonical(record);
        return new ReplaySessionRow(day, text, equity, 0m, ReplayJson.Sha256(text));
    }

    private async Task<T> WithStoreAsync<T>(Func<IReplayStore, Task<T>> action)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<IReplayStore>());
    }

    /// <summary>Stores a run, records it and claims it: it is running, with its first session stored.</summary>
    private async Task<Guid> RunningAsync(string mode = "frozen", string? sheet = null)
    {
        var run = New(mode, sheet);
        await WithStoreAsync(async s =>
        {
            (await s.InsertAsync(null, [run], Ct)).Started.ShouldBeTrue();
            await s.MarkRecordedAsync([run.Id], Ct);
            (await s.ClaimAsync(Ct)).ShouldNotBeNull();
            await s.SaveSessionAsync(run.Id, Session(D1, 1_000_000m), [], Ct);
            return 0;
        });
        return run.Id;
    }

    private async Task<JsonElement> GetAsync(string path)
    {
        using var response = await Client.GetAsync(new Uri(path, UriKind.Relative), Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private async Task<string> PostCodeAsync(string path, object body, HttpStatusCode expected)
    {
        using var response = await Client.PostAsJsonAsync(new Uri(path, UriKind.Relative), body, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(expected, text);
        return JsonDocument.Parse(text).RootElement.TryGetProperty("code", out var code) ? code.GetString()! : text;
    }

    [Fact]
    public async Task Every_replay_route_needs_the_owners_session()
    {
        using var anonymous = Factory.CreateAnonymousClient();
        var id = Guid.NewGuid();

        foreach (var path in new[] { "/api/replay/runs", $"/api/replay/runs/{id}", $"/api/replay/runs/{id}/equity", $"/api/replay/runs/{id}/sessions", $"/api/replay/runs/{id}/trades", $"/api/replay/pairs/{id}" })
        {
            (await anonymous.GetAsync(new Uri(path, UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized, path);
        }

        (await anonymous.PostAsJsonAsync(new Uri("/api/replay/runs", UriKind.Relative), new { from = "2026-10-05" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsync(new Uri($"/api/replay/runs/{id}/cancel", UriKind.Relative), null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_start_without_the_csrf_token_is_refused()
    {
        using var noToken = Factory.CreateAnonymousClient();
        noToken.DefaultRequestHeaders.Add("Cookie", Owner.CookieHeader);

        using var response = await noToken.PostAsJsonAsync(new Uri("/api/replay/runs", UriKind.Relative), new { from = "2026-10-05", to = "2026-10-06" }, Ct);

        response.StatusCode.ShouldBeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Bad_input_is_422_replay_invalid()
    {
        var basket = new { basketIds = new[] { Guid.NewGuid() } };

        (await PostCodeAsync("/api/replay/runs", new { from = "2026-10-06", to = "2026-10-05", scope = basket }, HttpStatusCode.UnprocessableEntity)).ShouldBe("replay.invalid"); // from after to
        (await PostCodeAsync("/api/replay/runs", new { from = "2026-10-05", to = "2026-10-06", scope = basket, capital = 0 }, HttpStatusCode.UnprocessableEntity)).ShouldBe("replay.invalid");
        (await PostCodeAsync("/api/replay/runs", new { from = "2026-10-05", to = "2026-10-06", scope = basket, modes = (string[])["hourly"] }, HttpStatusCode.UnprocessableEntity)).ShouldBe("replay.invalid");
        (await PostCodeAsync("/api/replay/runs", new { from = "2026-10-05", to = "2026-10-06", scope = new { basketIds = Array.Empty<Guid>() } }, HttpStatusCode.UnprocessableEntity)).ShouldBe("replay.invalid"); // no scope and no stored auto scope
    }

    [Fact]
    public async Task An_end_date_past_the_hold_out_is_409_replay_holdout()
    {
        var code = await PostCodeAsync("/api/replay/runs", new { from = "2026-10-05", to = "2099-01-01", scope = new { basketIds = new[] { Guid.NewGuid() } } }, HttpStatusCode.Conflict);

        code.ShouldBe("replay.holdout");
    }

    [Fact]
    public async Task A_second_start_while_a_replay_runs_is_409_replay_busy()
    {
        await RunningAsync();

        var code = await PostCodeAsync("/api/replay/runs", new { from = "2026-10-05", to = "2026-10-06", scope = new { basketIds = new[] { Guid.NewGuid() } } }, HttpStatusCode.Conflict);

        code.ShouldBe("replay.busy");
    }

    [Fact]
    public async Task A_run_is_read_while_it_runs_with_the_hindsight_labels_the_costs_label_and_its_trial_number_and_then_cancelled()
    {
        var id = await RunningAsync();

        var list = await GetAsync("/api/replay/runs");
        var run = await GetAsync($"/api/replay/runs/{id}");
        var equity = await GetAsync($"/api/replay/runs/{id}/equity");
        var sessions = await GetAsync($"/api/replay/runs/{id}/sessions?from=2026-10-05&to=2026-10-06");
        var session = await GetAsync($"/api/replay/runs/{id}/sessions/2026-10-05");
        var trades = await GetAsync($"/api/replay/runs/{id}/trades");

        list.GetArrayLength().ShouldBe(1);
        list[0].GetProperty("completeness").GetProperty("complete").GetBoolean().ShouldBeFalse();
        run.GetProperty("hindsight")[0].GetProperty("code").GetString().ShouldBe("before_costs"); // sorted by code; the label list is on the first screen
        run.GetProperty("hindsight").EnumerateArray().Select(l => l.GetProperty("code").GetString()).ShouldContain("versions_start");
        run.GetProperty("costsLabel").GetString().ShouldBe("before costs (charge sheet not filled, ISS-25)");
        run.GetProperty("trialNumber").GetInt32().ShouldBe(1);
        var completeness = run.GetProperty("completeness");
        (completeness.GetProperty("sessionsDone").GetInt32(), completeness.GetProperty("sessionsTotal").GetInt32(), completeness.GetProperty("asOfSession").GetString()).ShouldBe((1, 2, "2026-10-05"));
        run.GetProperty("state").GetString().ShouldBe("running");
        equity.GetProperty("points").GetArrayLength().ShouldBe(1);
        sessions.GetProperty("sessions")[0].GetProperty("blockedByReason").GetProperty("max_positions").GetInt32().ShouldBe(1);
        session.GetProperty("blocked")[0].GetProperty("reason").GetString().ShouldBe("max_positions");
        trades.GetProperty("trades").GetArrayLength().ShouldBe(0);

        using var cancel = await Client.PostAsync(new Uri($"/api/replay/runs/{id}/cancel", UriKind.Relative), null, Ct);
        cancel.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await cancel.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("state").GetString().ShouldBe("cancelling");
    }

    [Fact]
    public async Task A_run_with_a_sheet_carries_no_before_costs_label()
    {
        var id = await RunningAsync(sheet: "sheet-1");

        var run = await GetAsync($"/api/replay/runs/{id}");

        run.GetProperty("costsLabel").GetString()!.ShouldContain("sheet-1");
        run.GetProperty("hindsight").EnumerateArray().Select(l => l.GetProperty("code").GetString()).ShouldNotContain("before_costs");
    }

    [Fact]
    public async Task An_unknown_run_or_session_is_404_and_a_finished_run_cannot_be_cancelled()
    {
        var id = await RunningAsync();
        foreach (var path in new[] { $"/api/replay/runs/{Guid.NewGuid()}", $"/api/replay/runs/{Guid.NewGuid()}/equity", $"/api/replay/runs/{id}/sessions/2026-10-06", $"/api/replay/pairs/{Guid.NewGuid()}" })
        {
            (await Client.GetAsync(new Uri(path, UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound, path);
        }

        await WithStoreAsync(async s => await s.FinishAsync(id, ReplayRunStates.Completed, new string('d', 64), null, [], T0, Ct));

        using var cancel = await Client.PostAsync(new Uri($"/api/replay/runs/{id}/cancel", UriKind.Relative), null, Ct);
        cancel.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        using var unknown = await Client.PostAsync(new Uri($"/api/replay/runs/{Guid.NewGuid()}/cancel", UriKind.Relative), null, Ct);
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_pair_is_not_compared_until_both_runs_are_complete_and_then_answers_with_its_n()
    {
        var pair = Guid.NewGuid();
        var (frozen, learning) = (New("frozen"), New("learning", minutes: 1));
        await WithStoreAsync(async s =>
        {
            (await s.InsertAsync(pair, [frozen, learning], Ct)).Started.ShouldBeTrue();
            await s.MarkRecordedAsync([frozen.Id, learning.Id], Ct);
            return 0;
        });

        var waiting = await GetAsync($"/api/replay/pairs/{pair}");

        waiting.GetProperty("answer").ValueKind.ShouldBe(JsonValueKind.Null);
        waiting.GetProperty("reason").GetString()!.ShouldContain("complete");

        foreach (var run in new[] { frozen, learning })
        {
            await WithStoreAsync(async s =>
            {
                await s.ClaimAsync(Ct);
                await s.SaveSessionAsync(run.Id, Session(D1, 1_000_000m), [], Ct);
                await s.SaveSessionAsync(run.Id, Session(D2, 1_000_500m), [], Ct);
                return await s.FinishAsync(run.Id, ReplayRunStates.Completed, new string('d', 64), null, [], T0, Ct);
            });
        }

        var answered = await GetAsync($"/api/replay/pairs/{pair}");

        answered.GetProperty("answer").GetString().ShouldBe("too_little_history");
        answered.GetProperty("days").GetInt32().ShouldBe(2);
        answered.GetProperty("frozen").GetProperty("mode").GetString().ShouldBe("frozen");
        answered.GetProperty("learning").GetProperty("mode").GetString().ShouldBe("learning");
    }
}
