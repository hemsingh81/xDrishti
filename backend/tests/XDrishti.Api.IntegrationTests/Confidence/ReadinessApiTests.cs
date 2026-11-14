using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.IntegrationTests.Lab;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Confidence;
using XDrishti.Application.Learning;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Api.IntegrationTests.Confidence;

/// <summary>
/// CF-11 part 1 (ADR 0221) over HTTP and a real database: the scorecards are derived reads that say unavailable where no producer exists; the enforce switch is the owner's act (a fresh password to turn it ON, the session and the CSRF token to turn it OFF, a missing password 403 and
/// a wrong one 401, the actor from the session and never from the body); with the switch OFF a failed gate lets the lifecycle move go on and the changelog row cites the gate's snapshot, with it ON the move is refused with <c>confidence.gate_failed</c> and nothing is written; the
/// daily snapshot run is insert-only and idempotent. Nothing here can place an order.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class ReadinessApiTests(ApiFactory factory) : DataTestBase(factory)
{
    public override async ValueTask DisposeAsync()
    {
        await Factory.ResetAccountsAsync(); // leave no stored enforce-ON row (or snapshot) behind for the classes that share the database
        await base.DisposeAsync();
    }

    private static readonly Uri Scorecards = new("/api/confidence/scorecards", UriKind.Relative);

    private static readonly Uri Settings = new("/api/confidence/readiness-settings", UriKind.Relative);

    private static Uri One(string strategy, int version) => new($"/api/confidence/scorecards/{strategy}/versions/{version}", UriKind.Relative);

    private static readonly TransitionFacts Facts = new(true, true, true, true, 20);

    private async Task<(HttpStatusCode Status, JsonElement Body)> SendAsync(HttpMethod method, Uri uri, object? body = null, HttpClient? client = null)
    {
        using var request = new HttpRequestMessage(method, uri) { Content = body is null ? null : JsonContent.Create(body) };
        using var response = await (client ?? Client).SendAsync(request, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    private Task<(HttpStatusCode Status, JsonElement Body)> SwitchAsync(bool enforce, string? password, string? reason = "I watched gate R pass by hand") =>
        SendAsync(HttpMethod.Put, Settings, new { enforce, reason, password, actor = "owner:evil@example.test" });

    private async Task<string> CreateStrategyAsync()
    {
        var id = LabTexts.NewId();
        using var response = await Client.PostAsJsonAsync(new Uri("/api/lab/strategies", UriKind.Relative), new { text = LabTexts.Valid(id) }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return id;
    }

    private async Task<Result<ChangelogOutcome>> MoveAsync(string cell, CellState to)
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ICommandHandler<RecordCellTransitionCommand, ChangelogOutcome>>()
            .HandleAsync(new RecordCellTransitionCommand(cell, to, "owner move", "owner:me@example.test", Facts, ["run:x"]), Ct);
    }

    private static string CellOf(string strategy) => $"{strategy}|15m|BUY|intraday|FIXED_RR|*";

    [Fact]
    public async Task Without_a_session_every_route_is_401_and_nothing_is_written()
    {
        var anonymous = Factory.CreateAnonymousClient();

        var results = new[]
        {
            await SendAsync(HttpMethod.Get, Scorecards, client: anonymous),
            await SendAsync(HttpMethod.Get, One("X", 1), client: anonymous),
            await SendAsync(HttpMethod.Get, Settings, client: anonymous),
            await SendAsync(HttpMethod.Put, Settings, new { enforce = true, reason = "x", password = Factory.OwnerPassword }, anonymous),
        };

        results.ShouldAllBe(r => r.Status == HttpStatusCode.Unauthorized);
        (await ScalarAsync<long>("SELECT count(*) FROM readiness.settings")).ShouldBe(0);
    }

    [Fact]
    public async Task The_switch_is_off_with_no_row_and_the_thresholds_are_shown_beside_it()
    {
        var result = await SendAsync(HttpMethod.Get, Settings);

        result.Status.ShouldBe(HttpStatusCode.OK);
        result.Body.GetProperty("enforce").GetBoolean().ShouldBeFalse();
        result.Body.GetProperty("switchId").ValueKind.ShouldBe(JsonValueKind.Null);
        result.Body.GetProperty("settingsHash").GetString()!.Length.ShouldBe(64);
        result.Body.GetProperty("settings").GetString()!.ShouldContain("nEff=300");
    }

    [Fact]
    public async Task A_strategy_with_no_lab_result_reads_not_ready_with_every_producer_missing_named()
    {
        var id = await CreateStrategyAsync();

        var list = await SendAsync(HttpMethod.Get, Scorecards);
        var card = await SendAsync(HttpMethod.Get, One(id, 1));

        list.Status.ShouldBe(HttpStatusCode.OK);
        var line = list.Body.GetProperty("strategies").EnumerateArray().Single(s => s.GetProperty("strategy").GetString() == id);
        line.GetProperty("score").GetDouble().ShouldBe(0);
        line.GetProperty("label").GetString().ShouldBe("not_ready");
        line.GetProperty("gateR").GetBoolean().ShouldBeFalse();
        list.Body.GetProperty("system").GetProperty("score").GetDouble().ShouldBe(0);
        list.Body.GetProperty("system").GetProperty("equalWeights").GetBoolean().ShouldBeTrue();
        list.Body.GetProperty("statements").GetArrayLength().ShouldBeGreaterThan(0);
        card.Status.ShouldBe(HttpStatusCode.OK);
        card.Body.GetProperty("rows").GetArrayLength().ShouldBe(15);
        card.Body.GetProperty("rows").EnumerateArray().Count(r => r.GetProperty("status").GetString() == "pass").ShouldBe(0);
        card.Body.GetProperty("rows").EnumerateArray().ShouldAllBe(r => r.GetProperty("status").GetString() != "pass");
        card.Body.GetProperty("subScores").GetArrayLength().ShouldBe(6);
        card.Body.GetProperty("waiting").GetArrayLength().ShouldBeGreaterThan(0);
        card.Body.GetProperty("hardFails").GetArrayLength().ShouldBeGreaterThan(0);
        card.Body.GetProperty("gateR").GetProperty("passed").GetBoolean().ShouldBeFalse();
        card.Body.GetProperty("gateR").GetProperty("failing").GetArrayLength().ShouldBe(13);
        card.Body.GetProperty("settingsHash").GetString()!.Length.ShouldBe(64);
        card.Body.GetProperty("enforce").GetBoolean().ShouldBeFalse();
        card.Body.GetProperty("history").GetArrayLength().ShouldBe(0);
    }

    [Theory]
    [InlineData("NOPE_NOT_STORED", 1, "confidence.scorecard_not_found")]
    [InlineData("NOPE_NOT_STORED", 0, "confidence.readiness_invalid")]
    public async Task An_unknown_strategy_is_404_and_a_version_under_1_is_400(string strategy, int version, string code)
    {
        var result = await SendAsync(HttpMethod.Get, One(strategy, version));

        result.Status.ShouldBe(code == "confidence.scorecard_not_found" ? HttpStatusCode.NotFound : HttpStatusCode.BadRequest);
        result.Body.GetProperty("code").GetString().ShouldBe(code);
    }

    [Fact]
    public async Task An_unknown_version_of_a_known_strategy_is_404()
    {
        var id = await CreateStrategyAsync();

        var result = await SendAsync(HttpMethod.Get, One(id, 9));

        result.Status.ShouldBe(HttpStatusCode.NotFound);
        result.Body.GetProperty("code").GetString().ShouldBe("confidence.scorecard_not_found");
    }

    [Fact]
    public async Task A_change_of_the_switch_needs_a_fresh_password_both_ways_because_turning_it_off_removes_a_refusal()
    {
        var missing = await SwitchAsync(true, null);
        var empty = await SwitchAsync(true, string.Empty);
        var wrong = await SwitchAsync(true, "not-the-password");
        (await ScalarAsync<long>("SELECT count(*) FROM readiness.settings")).ShouldBe(0);

        var on = await SwitchAsync(true, Factory.OwnerPassword);
        var offWithoutPassword = await SwitchAsync(false, null, "back to warnings");
        var offWrong = await SwitchAsync(false, "not-the-password", "back to warnings");
        var stillOn = await SendAsync(HttpMethod.Get, Settings);
        var off = await SwitchAsync(false, Factory.OwnerPassword, "back to warnings");
        var after = await SendAsync(HttpMethod.Get, Settings);

        missing.Status.ShouldBe(HttpStatusCode.Forbidden);
        missing.Body.GetProperty("code").GetString().ShouldBe("auth.step_up_required");
        empty.Status.ShouldBe(HttpStatusCode.Forbidden);
        wrong.Status.ShouldBe(HttpStatusCode.Unauthorized);
        on.Status.ShouldBe(HttpStatusCode.OK);
        on.Body.GetProperty("enforce").GetBoolean().ShouldBeTrue();
        offWithoutPassword.Status.ShouldBe(HttpStatusCode.Forbidden);
        offWithoutPassword.Body.GetProperty("code").GetString().ShouldBe("auth.step_up_required");
        offWrong.Status.ShouldBe(HttpStatusCode.Unauthorized);
        stillOn.Body.GetProperty("enforce").GetBoolean().ShouldBeTrue("a refused OFF changes nothing");
        off.Status.ShouldBe(HttpStatusCode.OK);
        off.Body.GetProperty("enforce").GetBoolean().ShouldBeFalse();
        after.Body.GetProperty("enforce").GetBoolean().ShouldBeFalse();
        after.Body.GetProperty("switchId").GetInt64().ShouldBe(off.Body.GetProperty("id").GetInt64());
        (await ScalarAsync<long>("SELECT count(*) FROM readiness.settings")).ShouldBe(2, "every change is a row");
    }

    [Fact]
    public async Task The_actor_is_the_signed_in_owner_and_never_one_named_in_the_body()
    {
        await SwitchAsync(true, Factory.OwnerPassword);

        (await ScalarAsync<string>("SELECT actor FROM readiness.settings ORDER BY id DESC LIMIT 1")).ShouldBe("owner:" + ApiFactory.OwnerEmail);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_missing_reason_is_400_and_writes_nothing(string? reason)
    {
        var result = await SwitchAsync(true, Factory.OwnerPassword, reason);

        result.Status.ShouldBe(HttpStatusCode.BadRequest);
        result.Body.GetProperty("code").GetString().ShouldBe("confidence.reason_invalid");
        (await ScalarAsync<long>("SELECT count(*) FROM readiness.settings")).ShouldBe(0);
    }

    [Fact]
    public async Task A_change_without_the_CSRF_token_is_refused_even_with_the_session_and_the_right_password()
    {
        var client = Factory.CreateAnonymousClient();
        client.DefaultRequestHeaders.Add("Cookie", Owner.CookieHeader);

        var result = await SendAsync(HttpMethod.Put, Settings, new { enforce = true, reason = "x", password = Factory.OwnerPassword }, client);

        result.Status.ShouldBe(HttpStatusCode.Forbidden);
        result.Body.GetProperty("code").GetString().ShouldBe("auth.csrf_invalid");
        (await ScalarAsync<long>("SELECT count(*) FROM readiness.settings")).ShouldBe(0);
    }

    [Fact]
    public async Task With_enforcement_off_a_candidate_moves_to_paper_and_the_row_cites_the_gate_snapshot_and_the_failing_rows()
    {
        var cell = CellOf(await CreateStrategyAsync());
        (await MoveAsync(cell, CellState.Candidate)).IsSuccess.ShouldBeTrue();

        var moved = await MoveAsync(cell, CellState.Paper);

        moved.IsSuccess.ShouldBeTrue(moved.IsFailure ? moved.Error.Message : string.Empty);
        var evidence = await ScalarAsync<string>("SELECT array_to_string(evidence_ids, ';') FROM research.learning_changelog WHERE subject = @s AND to_state = 'paper'", ("s", cell));
        evidence.ShouldContain("run:x");
        evidence.ShouldContain("readiness-snapshot:");
        evidence.ShouldContain("readiness-gate-R:fail:enforce-off:rows-");
        (await ScalarAsync<long>("SELECT count(*) FROM readiness.scorecard_snapshots WHERE scope = 'gate' AND strategy_id = @s", ("s", cell.Split('|')[0]))).ShouldBe(1);
        (await ScalarAsync<string>("SELECT body->>'gate' FROM readiness.scorecard_snapshots WHERE scope = 'gate' AND strategy_id = @s", ("s", cell.Split('|')[0]))).ShouldBe("R");
    }

    [Fact]
    public async Task With_enforcement_on_the_move_is_refused_with_the_failing_rows_and_nothing_is_written()
    {
        var cell = CellOf(await CreateStrategyAsync());
        (await MoveAsync(cell, CellState.Candidate)).IsSuccess.ShouldBeTrue();
        (await SwitchAsync(true, Factory.OwnerPassword)).Status.ShouldBe(HttpStatusCode.OK);

        var refused = await MoveAsync(cell, CellState.Paper);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("confidence.gate_failed");
        refused.Error.Kind.ShouldBe(ErrorKind.Conflict);
        refused.Error.Message.ShouldContain("rows not passing");
        (await ScalarAsync<long>("SELECT count(*) FROM research.learning_changelog WHERE subject = @s AND to_state = 'paper'", ("s", cell))).ShouldBe(0);
        (await ScalarAsync<long>("SELECT count(*) FROM readiness.scorecard_snapshots WHERE scope = 'gate' AND strategy_id = @s", ("s", cell.Split('|')[0]))).ShouldBe(1, "the refusal is cited too");

        (await SwitchAsync(false, Factory.OwnerPassword, "off again")).Status.ShouldBe(HttpStatusCode.OK);
        (await MoveAsync(cell, CellState.Paper)).IsSuccess.ShouldBeTrue("with the switch OFF the same move goes through");
    }

    [Fact]
    public async Task The_owners_re_enable_is_refused_409_when_enforcement_is_on_and_gate_R_fails_and_goes_through_with_the_warning_and_the_evidence_when_it_is_off()
    {
        var cell = CellOf(await CreateStrategyAsync());
        foreach (var state in new[] { CellState.Candidate, CellState.Paper, CellState.Paused })
        {
            (await MoveAsync(cell, state)).IsSuccess.ShouldBeTrue();
        }

        var reEnable = new Uri($"/api/learning/cells/{Uri.EscapeDataString(cell)}/re-enable", UriKind.Relative);
        (await SwitchAsync(true, Factory.OwnerPassword)).Status.ShouldBe(HttpStatusCode.OK);

        var refused = await SendAsync(HttpMethod.Post, reEnable, new { reason = "it recovered", password = Factory.OwnerPassword });

        refused.Status.ShouldBe(HttpStatusCode.Conflict);
        refused.Body.GetProperty("code").GetString().ShouldBe("confidence.gate_failed");
        (await ScalarAsync<long>("SELECT count(*) FROM research.learning_changelog WHERE subject = @s AND to_state = 'paper'", ("s", cell))).ShouldBe(1, "only the first move to paper; the re-enable wrote nothing");

        (await SwitchAsync(false, Factory.OwnerPassword, "off again")).Status.ShouldBe(HttpStatusCode.OK);
        var allowed = await SendAsync(HttpMethod.Post, reEnable, new { reason = "it recovered", password = Factory.OwnerPassword });

        allowed.Status.ShouldBe(HttpStatusCode.OK);
        allowed.Body.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()).ShouldContain(w => w!.Contains("did NOT pass", StringComparison.Ordinal));
        var evidence = await ScalarAsync<string>("SELECT array_to_string(evidence_ids, ';') FROM research.learning_changelog WHERE subject = @s AND to_state = 'paper' ORDER BY id DESC LIMIT 1", ("s", cell));
        evidence.ShouldContain("re-enable:no-kill-proposal");
        evidence.ShouldContain("readiness-gate-R:fail:enforce-off:rows-");
    }

    [Fact]
    public async Task A_move_the_gates_do_not_judge_leaves_no_gate_snapshot_even_with_enforcement_on()
    {
        var cell = CellOf(await CreateStrategyAsync());
        (await SwitchAsync(true, Factory.OwnerPassword)).Status.ShouldBe(HttpStatusCode.OK);

        var candidate = await MoveAsync(cell, CellState.Candidate);

        candidate.IsSuccess.ShouldBeTrue();
        (await ScalarAsync<long>("SELECT count(*) FROM readiness.scorecard_snapshots WHERE scope = 'gate'")).ShouldBe(0);
    }

    [Fact]
    public async Task The_daily_run_stores_one_snapshot_per_strategy_and_the_system_and_a_second_run_stores_nothing()
    {
        var id = await CreateStrategyAsync();
        using var scope = Factory.Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<SnapshotScorecardsCommand, SnapshotRunOutcome>>();

        var first = await handler.HandleAsync(new SnapshotScorecardsCommand(), Ct);
        var second = await handler.HandleAsync(new SnapshotScorecardsCommand(), Ct);

        first.IsSuccess.ShouldBeTrue();
        first.Value.Stored.ShouldBeGreaterThanOrEqualTo(2);
        second.Value.Stored.ShouldBe(0);
        second.Value.AlreadyDone.ShouldBe(first.Value.Stored);
        (await ScalarAsync<long>("SELECT count(*) FROM readiness.scorecard_snapshots WHERE subject = 'system' AND scope = 'daily'")).ShouldBe(1);
        (await ScalarAsync<long>("SELECT count(*) FROM readiness.scorecard_snapshots WHERE subject = 'strategy' AND scope = 'daily' AND strategy_id = @s AND version = 1", ("s", id))).ShouldBe(1);
        (await ScalarAsync<string>("SELECT settings_hash FROM readiness.scorecard_snapshots WHERE strategy_id = @s", ("s", id))).Length.ShouldBe(64);
        (await ScalarAsync<long>("SELECT count(*) FROM readiness.scorecard_snapshots WHERE taken_on = '2026-10-12'")).ShouldBe(first.Value.Stored, "the India day of the pinned clock");

        var card = await SendAsync(HttpMethod.Get, One(id, 1));
        card.Body.GetProperty("history").GetArrayLength().ShouldBe(1);
        card.Body.GetProperty("history")[0].GetProperty("label").GetString().ShouldBe("not_ready");
    }
}
