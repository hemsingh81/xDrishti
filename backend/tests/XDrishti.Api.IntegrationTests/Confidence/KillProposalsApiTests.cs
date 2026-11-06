using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Confidence;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Confidence;
using XDrishti.Application.Learning;
using XDrishti.Domain.Learning;

namespace XDrishti.Api.IntegrationTests.Confidence;

/// <summary>
/// CF-12 part 12b, AC5 and AC6 (ADR 0219) over HTTP and a real database: a proposal is applied by the owner (session and CSRF, no password) through the real lifecycle, twice and concurrently without a second pause; the automatic setting is the owner's act with a fresh password (a missing
/// one is 403, a wrong one 401) and OFF without a row; an automatic apply records <c>auto</c> and the switch row and never an owner; the failure boundary is never applied by auto; the re-enable needs the same fresh password, the owner and a paused cell; the bell shows an open proposal
/// and not an applied one. Nothing here can place an order: the only writes are the changelog and the kill tables.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class KillProposalsApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly Uri Proposals = new("/api/confidence/kill-proposals", UriKind.Relative);

    private static readonly Uri Switch = new("/api/confidence/kill-settings", UriKind.Relative);

    private static readonly Uri Notices = new("/api/plans/notices", UriKind.Relative);

    private readonly string _strategy = "KT" + Guid.NewGuid().ToString("N")[..10];

    private string Cell(string side) => $"{_strategy}|15m|{side}|intraday|FIXED_RR|*";

    private static Uri Apply(long id) => new($"/api/confidence/kill-proposals/{id}/apply", UriKind.Relative);

    private static Uri ReEnable(string cell) => new($"/api/learning/cells/{Uri.EscapeDataString(cell)}/re-enable", UriKind.Relative);

    private async Task WalkAsync(string cell, params CellState[] states)
    {
        using var scope = Factory.Services.CreateScope();
        var record = scope.ServiceProvider.GetRequiredService<ICommandHandler<RecordCellTransitionCommand, ChangelogOutcome>>();
        foreach (var state in states)
        {
            var moved = await record.HandleAsync(new RecordCellTransitionCommand(cell, state, "setup", "test", new TransitionFacts(true, true, true, true, 20), ["run:setup"]), Ct);
            moved.IsSuccess.ShouldBeTrue(moved.IsFailure ? moved.Error.Message : string.Empty);
        }
    }

    private async Task<long> ProposeAsync(string rule = "cost_stress", params string[] cells)
    {
        using var scope = Factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IKillProposalStore>();
        var proposal = new NewKillProposal(_strategy, null, rule, rule == "failure_boundary", -0.56, 0, 100, $"the {rule} numbers", cells, new string('a', 64), 130);
        return (await store.InsertAsync(proposal, new DateOnly(2026, 11, 6), Ct))!.Value;
    }

    private async Task<(HttpStatusCode Status, JsonElement Body)> SendAsync(HttpMethod method, Uri uri, object? body = null, HttpClient? client = null)
    {
        using var request = new HttpRequestMessage(method, uri) { Content = body is null ? null : JsonContent.Create(body) };
        using var response = await (client ?? Client).SendAsync(request, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    private Task<long> ChangelogAsync(string cell, string to) => ScalarAsync<long>("SELECT count(*) FROM research.learning_changelog WHERE subject = @s AND to_state = @t", ("s", cell), ("t", to));

    private async Task<(HttpStatusCode Status, JsonElement Body)> SwitchAsync(string? password, bool auto = true, string? reason = "I have read two proposals") =>
        await SendAsync(HttpMethod.Put, Switch, new { auto, reason, password, actor = "owner:evil@example.test" });

    [Fact]
    public async Task Without_a_session_every_route_is_401_and_nothing_is_written()
    {
        var anonymous = Factory.CreateAnonymousClient();
        var cell = Cell("BUY");

        var results = new[]
        {
            await SendAsync(HttpMethod.Get, Proposals, client: anonymous),
            await SendAsync(HttpMethod.Post, Apply(1), client: anonymous),
            await SendAsync(HttpMethod.Put, Switch, new { auto = true, reason = "x", password = Factory.OwnerPassword }, anonymous),
            await SendAsync(HttpMethod.Post, ReEnable(cell), new { reason = "x", password = Factory.OwnerPassword }, anonymous),
        };

        results.ShouldAllBe(r => r.Status == HttpStatusCode.Unauthorized);
        (await ScalarAsync<long>("SELECT count(*) FROM confidence.kill_settings")).ShouldBe(0);
    }

    [Fact]
    public async Task The_list_is_empty_at_first_and_the_automatic_setting_is_off_with_no_row()
    {
        var result = await SendAsync(HttpMethod.Get, Proposals);

        result.Status.ShouldBe(HttpStatusCode.OK);
        result.Body.GetProperty("items").GetArrayLength().ShouldBe(0);
        result.Body.GetProperty("settings").GetProperty("auto").GetBoolean().ShouldBeFalse();
        result.Body.GetProperty("settings").GetProperty("settingId").ValueKind.ShouldBe(JsonValueKind.Null);
        result.Body.GetProperty("settings").GetProperty("stressMultiplier").GetDouble().ShouldBe(1.5);
        (await SendAsync(HttpMethod.Get, new Uri("/api/confidence/kill-proposals?state=done", UriKind.Relative))).Status.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_owner_applies_a_proposal_through_the_lifecycle_and_a_second_apply_changes_nothing()
    {
        await WalkAsync(Cell("BUY"), CellState.Candidate, CellState.Paper);
        await WalkAsync(Cell("SELL"), CellState.Candidate, CellState.Paper, CellState.Active);
        var id = await ProposeAsync("drawdown", Cell("BUY"), Cell("SELL"));

        var first = await SendAsync(HttpMethod.Post, Apply(id));
        var second = await SendAsync(HttpMethod.Post, Apply(id));

        first.Status.ShouldBe(HttpStatusCode.OK);
        first.Body.GetProperty("pausedCells").GetArrayLength().ShouldBe(2);
        first.Body.GetProperty("alreadyApplied").GetBoolean().ShouldBeFalse();
        second.Status.ShouldBe(HttpStatusCode.OK);
        second.Body.GetProperty("alreadyApplied").GetBoolean().ShouldBeTrue();
        (await ChangelogAsync(Cell("BUY"), "paused")).ShouldBe(1);
        (await ChangelogAsync(Cell("SELL"), "paused")).ShouldBe(1);
        (await ScalarAsync<string>("SELECT actor FROM research.learning_changelog WHERE subject = @s AND to_state = 'paused'", ("s", Cell("BUY")))).ShouldStartWith("owner:");
        (await ScalarAsync<string>("SELECT evidence_ids[1] FROM research.learning_changelog WHERE subject = @s AND to_state = 'paused'", ("s", Cell("BUY")))).ShouldBe($"kill-proposal:{id}");
        (await ScalarAsync<string>("SELECT state || ',' || actor FROM confidence.kill_proposals WHERE id = @id", ("id", id))).ShouldStartWith("applied,owner:");
        (await SendAsync(HttpMethod.Post, Apply(id + 1000))).Body.GetProperty("code").GetString().ShouldBe("confidence.proposal_not_found");
    }

    [Fact]
    public async Task Concurrent_applies_pause_each_cell_once_and_decide_the_proposal_once()
    {
        string[] cells = [Cell("BUY"), Cell("SELL"), $"{_strategy}|5m|BUY|intraday|FIXED_RR|*"];
        foreach (var cell in cells)
        {
            await WalkAsync(cell, CellState.Candidate, CellState.Paper);
        }

        var id = await ProposeAsync("cost_stress", cells);

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            using var scope = Factory.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<ICommandHandler<ApplyKillCommand, ApplyKillOutcome>>().HandleAsync(new ApplyKillCommand(id, "owner:racer@example.test"), Ct);
        }));

        outcomes.ShouldAllBe(o => o.IsSuccess, string.Join("; ", outcomes.Where(o => o.IsFailure).Select(o => o.Error.Message)));
        outcomes.Count(o => !o.Value.AlreadyApplied).ShouldBe(1, "exactly one call records the decision");
        foreach (var cell in cells)
        {
            (await ChangelogAsync(cell, "paused")).ShouldBe(1, cell);
        }

        (await ScalarAsync<long>("SELECT count(*) FROM confidence.kill_proposals WHERE id = @id AND state = 'applied'", ("id", id))).ShouldBe(1);
    }

    [Fact]
    public async Task A_change_of_the_automatic_setting_needs_a_fresh_password_a_missing_one_is_403_and_a_wrong_one_401()
    {
        var missing = await SwitchAsync(null);
        var empty = await SwitchAsync(string.Empty);
        var wrong = await SwitchAsync("not-the-password");

        missing.Status.ShouldBe(HttpStatusCode.Forbidden);
        missing.Body.GetProperty("code").GetString().ShouldBe("auth.step_up_required");
        empty.Status.ShouldBe(HttpStatusCode.Forbidden);
        wrong.Status.ShouldBe(HttpStatusCode.Unauthorized);
        wrong.Body.GetProperty("code").GetString().ShouldBe("auth.invalid_credentials");
        (await ScalarAsync<long>("SELECT count(*) FROM confidence.kill_settings")).ShouldBe(0);
    }

    [Fact]
    public async Task The_right_password_appends_a_row_for_the_session_owner_and_the_newest_row_is_in_force_ON_and_OFF()
    {
        var on = await SwitchAsync(Factory.OwnerPassword, auto: true);
        var offWithout = await SwitchAsync(null, auto: false);
        var off = await SwitchAsync(Factory.OwnerPassword, auto: false, reason: "back to manual");

        on.Status.ShouldBe(HttpStatusCode.OK);
        on.Body.GetProperty("auto").GetBoolean().ShouldBeTrue();
        offWithout.Status.ShouldBe(HttpStatusCode.Forbidden);
        off.Status.ShouldBe(HttpStatusCode.OK);
        (await ScalarAsync<string>("SELECT string_agg(auto::text, ',' ORDER BY id) FROM confidence.kill_settings")).ShouldBe("true,false");
        (await ScalarAsync<long>("SELECT count(*) FROM confidence.kill_settings WHERE actor LIKE '%evil%' OR actor NOT LIKE 'owner:%'")).ShouldBe(0, "a body cannot name the actor");
        (await SendAsync(HttpMethod.Get, Proposals)).Body.GetProperty("settings").GetProperty("auto").GetBoolean().ShouldBeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task A_missing_reason_with_the_right_password_is_400(string? reason)
    {
        var result = await SwitchAsync(Factory.OwnerPassword, reason: reason);

        result.Status.ShouldBe(HttpStatusCode.BadRequest);
        result.Body.GetProperty("code").GetString().ShouldBe("confidence.reason_invalid");
    }

    [Fact]
    public async Task With_the_setting_on_an_automatic_apply_records_auto_and_the_switch_row_and_never_an_owner()
    {
        await WalkAsync(Cell("BUY"), CellState.Candidate, CellState.Paper);
        var id = await ProposeAsync("decay_twice", Cell("BUY"));
        using var scope = Factory.Services.CreateScope();
        var apply = scope.ServiceProvider.GetRequiredService<ICommandHandler<ApplyKillCommand, ApplyKillOutcome>>();

        var whileOff = await apply.HandleAsync(new ApplyKillCommand(id, KillActors.Auto), Ct);
        var switched = await SwitchAsync(Factory.OwnerPassword);
        var switchId = switched.Body.GetProperty("id").GetInt64();
        var automatic = await apply.HandleAsync(new ApplyKillCommand(id, KillActors.Auto), Ct);

        whileOff.Error.Code.ShouldBe(KillErrors.AutoOffCode);
        (await ChangelogAsync(Cell("BUY"), "paused")).ShouldBe(1);
        automatic.IsSuccess.ShouldBeTrue(automatic.IsFailure ? automatic.Error.Message : string.Empty);
        (await ScalarAsync<string>("SELECT actor FROM research.learning_changelog WHERE subject = @s AND to_state = 'paused'", ("s", Cell("BUY")))).ShouldBe("auto");
        (await ScalarAsync<string>("SELECT before_ref FROM research.learning_changelog WHERE subject = @s AND to_state = 'paused'", ("s", Cell("BUY")))).ShouldBe($"kill-switch:{switchId}");
        (await ScalarAsync<string>("SELECT state || ',' || actor || ',' || auto_setting_id FROM confidence.kill_proposals WHERE id = @id", ("id", id))).ShouldBe($"applied,auto,{switchId}");
        (await ScalarAsync<long>("SELECT count(*) FROM research.learning_changelog WHERE subject = @s AND actor LIKE 'owner:%' AND to_state = 'paused'", ("s", Cell("BUY")))).ShouldBe(0);
    }

    [Fact]
    public async Task The_failure_boundary_is_never_applied_by_auto_even_with_the_setting_on_but_the_owner_can()
    {
        await WalkAsync(Cell("BUY"), CellState.Candidate, CellState.Paper);
        await SwitchAsync(Factory.OwnerPassword);
        var id = await ProposeAsync("failure_boundary", Cell("BUY"));
        using var scope = Factory.Services.CreateScope();

        var automatic = await scope.ServiceProvider.GetRequiredService<ICommandHandler<ApplyKillCommand, ApplyKillOutcome>>().HandleAsync(new ApplyKillCommand(id, KillActors.Auto), Ct);
        var stillOpen = await ScalarAsync<string>("SELECT state FROM confidence.kill_proposals WHERE id = @id", ("id", id));
        var owner = await SendAsync(HttpMethod.Post, Apply(id));

        automatic.Error.Code.ShouldBe(KillErrors.ProposalOnlyCode);
        stillOpen.ShouldBe("proposed");
        owner.Status.ShouldBe(HttpStatusCode.OK);
        (await ChangelogAsync(Cell("BUY"), "paused")).ShouldBe(1);
    }

    [Fact]
    public async Task An_open_proposal_is_a_bell_notice_and_an_applied_one_is_not()
    {
        await WalkAsync(Cell("BUY"), CellState.Candidate, CellState.Paper);
        var id = await ProposeAsync("drawdown", Cell("BUY"));

        var open = (await Client.GetFromJsonAsync<JsonElement>(Notices, Ct)).GetProperty("items").EnumerateArray().Where(i => i.GetProperty("id").GetString() == $"kill:{id}").ToList();
        await SendAsync(HttpMethod.Post, Apply(id));
        var closed = (await Client.GetFromJsonAsync<JsonElement>(Notices, Ct)).GetProperty("items").EnumerateArray().Where(i => i.GetProperty("id").GetString() == $"kill:{id}").ToList();

        open.Count.ShouldBe(1);
        open[0].GetProperty("kind").GetString().ShouldBe("kill_proposed");
        open[0].GetProperty("text").GetString().ShouldNotBeNull().ShouldContain(_strategy);
        closed.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_re_enable_without_the_password_is_403_with_a_wrong_one_401_and_the_cell_stays_paused()
    {
        var cell = Cell("BUY");
        await WalkAsync(cell, CellState.Candidate, CellState.Paper, CellState.Paused);

        var missing = await SendAsync(HttpMethod.Post, ReEnable(cell), new { reason = "recovered" });
        var wrong = await SendAsync(HttpMethod.Post, ReEnable(cell), new { reason = "recovered", password = "not-the-password" });

        missing.Status.ShouldBe(HttpStatusCode.Forbidden);
        missing.Body.GetProperty("code").GetString().ShouldBe("auth.step_up_required");
        wrong.Status.ShouldBe(HttpStatusCode.Unauthorized);
        (await ChangelogAsync(cell, "paper")).ShouldBe(1, "only the setup move to paper");
    }

    [Fact]
    public async Task With_the_password_the_owner_re_enables_a_paused_cell_to_paper_with_the_gate_warning()
    {
        var cell = Cell("BUY");
        await WalkAsync(cell, CellState.Candidate, CellState.Paper, CellState.Paused);

        var result = await SendAsync(HttpMethod.Post, ReEnable(cell), new { reason = "recovered in shadow", password = Factory.OwnerPassword, actor = "owner:evil@example.test" });

        result.Status.ShouldBe(HttpStatusCode.OK);
        result.Body.GetProperty("to").GetString().ShouldBe("paper");
        result.Body.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()).ShouldContain(w => w!.Contains("CF-11", StringComparison.Ordinal) && w.Contains("NOT checked", StringComparison.Ordinal));
        (await ScalarAsync<string>("SELECT actor FROM research.learning_changelog WHERE subject = @s ORDER BY id DESC LIMIT 1", ("s", cell))).ShouldStartWith("owner:");
        (await ScalarAsync<string>("SELECT actor FROM research.learning_changelog WHERE subject = @s ORDER BY id DESC LIMIT 1", ("s", cell))).ShouldNotBeNull().ShouldNotContain("evil");
        (await ScalarAsync<string>("SELECT to_state FROM research.learning_changelog WHERE subject = @s ORDER BY id DESC LIMIT 1", ("s", cell))).ShouldBe("paper");
    }

    [Fact]
    public async Task A_cell_paused_by_a_kill_proposal_whose_rule_cannot_be_evaluated_is_not_re_enabled_and_a_revert_cannot_lift_the_pause()
    {
        var cell = Cell("BUY");
        await WalkAsync(cell, CellState.Candidate, CellState.Paper);
        var id = await ProposeAsync("cost_stress", cell);
        await SendAsync(HttpMethod.Post, Apply(id));
        using var scope = Factory.Services.CreateScope();
        var pauseRow = await ScalarAsync<long>("SELECT id FROM research.learning_changelog WHERE subject = @s AND to_state = 'paused'", ("s", cell));

        var reEnable = await SendAsync(HttpMethod.Post, ReEnable(cell), new { reason = "recovered", password = Factory.OwnerPassword });
        var revert = await scope.ServiceProvider.GetRequiredService<ICommandHandler<RevertChangelogCommand, ChangelogOutcome>>().HandleAsync(new RevertChangelogCommand(pauseRow, "undo", "owner:me@example.test"), Ct);

        reEnable.Status.ShouldBe(HttpStatusCode.Conflict);
        reEnable.Body.GetProperty("code").GetString().ShouldBe("confidence.rule_not_cleared");
        revert.Error.Code.ShouldBe(ChangelogErrors.NotRevertibleCode);
        (await ScalarAsync<string>("SELECT to_state FROM research.learning_changelog WHERE subject = @s ORDER BY id DESC LIMIT 1", ("s", cell))).ShouldBe("paused");
    }

    [Fact]
    public async Task A_cell_that_is_not_paused_is_409_and_a_bad_key_is_422()
    {
        var cell = Cell("BUY");
        await WalkAsync(cell, CellState.Candidate, CellState.Paper);

        var notPaused = await SendAsync(HttpMethod.Post, ReEnable(cell), new { reason = "x", password = Factory.OwnerPassword });
        var bad = await SendAsync(HttpMethod.Post, ReEnable("not-a-cell"), new { reason = "x", password = Factory.OwnerPassword });

        notPaused.Status.ShouldBe(HttpStatusCode.Conflict);
        notPaused.Body.GetProperty("code").GetString().ShouldBe("confidence.cell_not_paused");
        bad.Status.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Apply_without_the_CSRF_token_is_refused()
    {
        var client = Factory.CreateAnonymousClient();
        client.DefaultRequestHeaders.Add("Cookie", Owner.CookieHeader);

        var result = await SendAsync(HttpMethod.Post, Apply(1), client: client);

        result.Status.ShouldBe(HttpStatusCode.Forbidden);
        result.Body.GetProperty("code").GetString().ShouldBe("auth.csrf_invalid");
    }

    [Fact]
    public async Task The_record_refuses_an_automatic_decision_after_the_switch_was_turned_off_and_the_store_says_so_instead_of_throwing()
    {
        var id = await ProposeAsync("drawdown", Cell("BUY"));
        var on = (await SwitchAsync(Factory.OwnerPassword, auto: true)).Body.GetProperty("id").GetInt64();
        await SwitchAsync(Factory.OwnerPassword, auto: false, reason: "off again");
        using var scope = Factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IKillProposalStore>();

        var stale = await store.DecideAsync(id, "applied", KillActors.Auto, on, Ct);
        var owner = await store.DecideAsync(id, "applied", "owner:me@example.test", null, Ct);
        var again = await store.DecideAsync(id, "applied", "owner:me@example.test", null, Ct);

        stale.ShouldBe(KillDecideResult.Refused);
        owner.ShouldBe(KillDecideResult.Decided);
        again.ShouldBe(KillDecideResult.NotProposed);
    }

    [Fact]
    public async Task A_change_of_the_switch_or_a_re_enable_without_the_CSRF_token_is_refused_even_with_the_session_and_the_right_password()
    {
        var cell = Cell("BUY");
        await WalkAsync(cell, CellState.Candidate, CellState.Paper, CellState.Paused);
        var client = Factory.CreateAnonymousClient();
        client.DefaultRequestHeaders.Add("Cookie", Owner.CookieHeader);

        var switched = await SendAsync(HttpMethod.Put, Switch, new { auto = true, reason = "x", password = Factory.OwnerPassword }, client);
        var reEnabled = await SendAsync(HttpMethod.Post, ReEnable(cell), new { reason = "x", password = Factory.OwnerPassword }, client);

        switched.Status.ShouldBe(HttpStatusCode.Forbidden);
        switched.Body.GetProperty("code").GetString().ShouldBe("auth.csrf_invalid");
        reEnabled.Status.ShouldBe(HttpStatusCode.Forbidden);
        reEnabled.Body.GetProperty("code").GetString().ShouldBe("auth.csrf_invalid");
        (await ScalarAsync<long>("SELECT count(*) FROM confidence.kill_settings")).ShouldBe(0);
        (await ChangelogAsync(cell, "paper")).ShouldBe(1, "the cell is still paused");
    }

    [Fact]
    public async Task The_daily_run_over_an_empty_record_does_nothing_and_leaves_every_table_empty()
    {
        using var scope = Factory.Services.CreateScope();

        var outcome = await scope.ServiceProvider.GetRequiredService<ICommandHandler<EvaluateDecayCommand, DecayRunOutcome>>().HandleAsync(new EvaluateDecayCommand(), Ct);

        outcome.Value.ShouldBe(new DecayRunOutcome(0, 0, 0, 0, 0, 0));
        (await ScalarAsync<long>("SELECT (SELECT count(*) FROM confidence.kill_proposals) + (SELECT count(*) FROM confidence.decay_checks) + (SELECT count(*) FROM confidence.kill_settings)")).ShouldBe(0);
    }
}
