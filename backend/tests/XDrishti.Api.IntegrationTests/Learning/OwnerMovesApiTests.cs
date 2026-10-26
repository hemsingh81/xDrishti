using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.IntegrationTests.Research;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Learning;
using XDrishti.Application.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Api.IntegrationTests.Learning;

/// <summary>
/// Spec P8-19 (ADR 0130) end to end on a real database: with the capabilities off (the default) the six writes are refused with the right password; with them on a wrong or missing password is a 401 and the lock advances; the actor is the session, a body cannot name a state or a number;
/// the lesson and hypothesis moves follow the state machines and show the domain's reason; a revert is a new row; a stale form and two moves at once write one row; the audit row names ids and states, never a password or a reason; and nothing that configures anything moves.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class OwnerMovesApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private const string Cell = "ORB|15m|BUY|intraday|FIXED_RR|*";

    private const string Secret = "a-reason-that-must-never-reach-the-audit-feed";

    private static readonly string[] UntouchedTables =
    [
        "lab.strategies", "lab.strategy_versions", "platform.app_settings", "platform.service_schedules", "research.cell_stats", "research.model_versions", "research.model_designations", "trading.plans", "trading.plan_items",
        "trading.plan_events", "workflow.approvals", "platform.capability_changes", "workflow.charge_sheet_events", "research.exit_selections", "research.experiments", "research.experiment_jobs",
    ];

    private static readonly string[] Events = ["LessonMoved", "HypothesisMoved", "ChangelogReverted", "AutonomySettingsChanged"];

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await using var connection = await OpenAsync();
        foreach (var table in new[] { "research.lessons", "research.hypotheses", "research.experiments", "research.learning_changelog" })
        {
            await ExecuteAsync(connection, "DELETE FROM " + table);
        }
    }

    private async Task OnAsync(params string[] names)
    {
        await using var connection = await OpenAsync();
        foreach (var name in names)
        {
            await ExecuteAsync(connection, "INSERT INTO platform.capability_changes (capability, enabled, reason, actor) VALUES (@n, true, 'test', 'owner:test@example.test')", ("n", name));
        }
    }

    private async Task<TResult> WithAsync<TResult>(Func<IServiceProvider, Task<TResult>> work)
    {
        using var scope = Factory.Services.CreateScope();
        return await work(scope.ServiceProvider);
    }

    private Task<Result<TOut>> SendAsync<TCommand, TOut>(TCommand command)
        where TCommand : ICommand =>
        WithAsync(p => p.GetRequiredService<ICommandHandler<TCommand, TOut>>().HandleAsync(command, Ct).AsTask());

    private async Task<(string Base, string Cand)> StoreRunsAsync()
    {
        var to = RunStoreRoundTripTests.NextTo();
        var a = StoredRunFixtures.Run(to: to);
        var b = StoredRunFixtures.Run(to: to, fills: XDrishti.Domain.Research.FillRules.Create(0.1, XDrishti.Domain.Research.SlippageConfig.Create(2, 0.05, 2).Value).Value);
        foreach (var run in new[] { a, b })
        {
            (await SendAsync<StoreRunCommand, XDrishti.Application.Abstractions.Research.StoreOutcome>(new StoreRunCommand(run, StoreKit.Record(run), SimulateStore.Actor))).IsSuccess.ShouldBeTrue();
        }

        return (a.RunKey, b.RunKey);
    }

    private async Task<long> ProposedAsync(string baseKey, string candidate = "2")
    {
        var proposed = await SendAsync<ProposeHypothesisCommand, HypothesisMoveOutcome>(new ProposeHypothesisCommand(HypothesisSource.Owner, null, new ExperimentKnob("slippage", ExperimentKnobKind.Slippage, "1", candidate), "fills may be dearer", baseKey, new HypothesisGrid(), "cli:xd"));
        proposed.IsSuccess.ShouldBeTrue(proposed.IsFailure ? proposed.Error.Message : string.Empty);
        return proposed.Value.Id;
    }

    /// <summary>A hypothesis judged by the real experiment (an outcome that is not Better on this data: accepting it is refused).</summary>
    private async Task<long> JudgedAsync()
    {
        var (b, c) = await StoreRunsAsync();
        var id = await ProposedAsync(b);
        (await SendAsync<MoveHypothesisCommand, HypothesisMoveOutcome>(new MoveHypothesisCommand(id, HypothesisState.Approved, "go", "cli:xd"))).IsSuccess.ShouldBeTrue();
        (await SendAsync<StartHypothesisCommand, HypothesisMoveOutcome>(new StartHypothesisCommand(id, c, "cli:xd"))).IsSuccess.ShouldBeTrue();
        (await SendAsync<JudgeHypothesisCommand, HypothesisMoveOutcome>(new JudgeHypothesisCommand(id, new ExperimentOptions(), 0, "cli:xd"))).IsSuccess.ShouldBeTrue();
        return id;
    }

    private long _evidence;

    private async Task<long> OpenLessonAsync() =>
        (await WithAsync(p => p.GetRequiredService<ILessonLog>().OpenAsync(new NewLessonRow(null, "open", null, "growing_tag", Cell, "e" + Interlocked.Increment(ref _evidence), "gave back profit grew", null, null, null, null, null, null, null, null, "found", "cli:xd", DateTimeOffset.UnixEpoch), Ct).AsTask())).Id;

    private async Task<long> LessonInAsync(string state, long judged)
    {
        var id = await OpenLessonAsync();
        if (state == "dismissed")
        {
            (await SendAsync<DismissLessonCommand, LessonMoveOutcome>(new DismissLessonCommand(id, "x", "cli:xd"))).IsSuccess.ShouldBeTrue();
        }

        if (state is "linked" or "closed")
        {
            (await SendAsync<LinkLessonCommand, LessonMoveOutcome>(new LinkLessonCommand(id, "hypothesis", judged, "x", "cli:xd"))).IsSuccess.ShouldBeTrue();
        }

        if (state == "closed")
        {
            (await SendAsync<CloseLessonCommand, LessonMoveOutcome>(new CloseLessonCommand(id, "x", "cli:xd"))).IsSuccess.ShouldBeTrue();
        }

        return id;
    }

    private async Task<long[]> CellRowsAsync()
    {
        var candidate = await SendAsync<RecordCellTransitionCommand, ChangelogOutcome>(new RecordCellTransitionCommand(Cell, CellState.Candidate, "test reason", "test", new TransitionFacts(true, true, true, true, 20), ["run:abc"]));
        var paper = await SendAsync<RecordCellTransitionCommand, ChangelogOutcome>(new RecordCellTransitionCommand(Cell, CellState.Paper, "test reason", "test", new TransitionFacts(true, true, true, true, 20), ["run:abc"]));
        return [candidate.Value.Id, paper.Value.Id];
    }

    private int _calls;

    private bool _rotate = true;

    /// <summary>The sign-in limit (10 a minute) is per address and counts every password-checked call: a new session, and with it a new address, every seventh call keeps a long test under it.</summary>
    private async Task<HttpClient> ClientAsync(HttpClient? given)
    {
        if (_rotate && given is null && Interlocked.Increment(ref _calls) % 7 == 0)
        {
            await ResignInAsync();
        }

        return given ?? Client;
    }

    private async Task<(HttpStatusCode Status, JsonElement Body)> PostAsync(string path, object body, HttpClient? client = null)
    {
        using var response = await (await ClientAsync(client)).PostAsJsonAsync(new Uri(path, UriKind.Relative), body, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    private Task<(HttpStatusCode Status, JsonElement Body)> LessonAsync(long id, string verb, string? password, string reason = Secret, object? extra = null) =>
        PostAsync($"/api/lessons/{id}/{verb}", Merge(new Dictionary<string, object?> { ["reason"] = reason, ["password"] = password }, extra));

    private Task<(HttpStatusCode Status, JsonElement Body)> LinkAsync(long id, string kind, long fix, string? password) =>
        PostAsync($"/api/lessons/{id}/link", new { fixKind = kind, fixId = fix, reason = Secret, password });

    private Task<(HttpStatusCode Status, JsonElement Body)> HypothesisAsync(long id, string to, string? password, object? extra = null) =>
        PostAsync($"/api/hypotheses/{id}/move", Merge(new Dictionary<string, object?> { ["to"] = to, ["reason"] = Secret, ["password"] = password }, extra));

    private Task<(HttpStatusCode Status, JsonElement Body)> RevertAsync(long id, string? password) =>
        PostAsync($"/api/changelog/{id}/revert", new { reason = Secret, password });

    private static readonly Uri Settings = new("/api/autonomy/settings", UriKind.Relative);

    private async Task<(HttpStatusCode Status, JsonElement Body)> AutonomyAsync(string? password, bool on = true, long? expected = null, HttpClient? client = null)
    {
        using var response = await (await ClientAsync(client)).PutAsJsonAsync(Settings, new { autoApproveTickets = on, promoteFromReserve = false, minPWin = 0.6, maxAutoPerDay = 2, reason = Secret, password, expectedSettingsId = expected, actor = "owner:evil@example.test" }, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    private static Dictionary<string, object?> Merge(Dictionary<string, object?> body, object? extra)
    {
        foreach (var property in extra?.GetType().GetProperties() ?? [])
        {
            body[property.Name] = property.GetValue(extra);
        }

        return body;
    }

    private Task<long> CountAsync(string table, string where = "true") => ScalarAsync<long>($"SELECT count(*) FROM {table} WHERE {where}");

    private async Task<string> RowsOfAsync() =>
        $"{await CountAsync("research.lessons")}/{await CountAsync("research.hypotheses")}/{await CountAsync("research.learning_changelog")}/{await CountAsync("workflow.autonomy_settings")}/{await CountAsync("accounts.account_events", "event IN ('LessonMoved','HypothesisMoved','ChangelogReverted','AutonomySettingsChanged')")}";

    private static void Code(JsonElement body, string code) => body.GetProperty("code").GetString().ShouldBe(code);

    [Fact]
    public async Task A_fresh_database_has_both_capabilities_off_and_the_six_writes_are_refused_with_the_right_password_and_write_nothing()
    {
        var caps = await Client.GetFromJsonAsync<JsonElement>(new Uri("/api/capabilities", UriKind.Relative), Ct);
        foreach (var name in new[] { "learning_moves_ui", "autonomy_form" })
        {
            caps.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("name").GetString() == name).GetProperty("enabled").GetBoolean().ShouldBeFalse(name);
        }

        var judged = await JudgedAsync();
        var lesson = await OpenLessonAsync();
        var rows = await CellRowsAsync();
        var before = await RowsOfAsync();
        var password = Factory.OwnerPassword;

        var answers = new[]
        {
            await LinkAsync(lesson, "hypothesis", judged, password), await LessonAsync(lesson, "close", password), await LessonAsync(lesson, "dismiss", password),
            await HypothesisAsync(judged, "rejected", password), await RevertAsync(rows[1], password), await AutonomyAsync(password),
        };

        foreach (var (status, body) in answers)
        {
            status.ShouldBe(HttpStatusCode.Forbidden);
            Code(body, "capabilities.off");
        }

        (await RowsOfAsync()).ShouldBe(before);
        using var read = await Client.GetAsync(new Uri($"/api/lessons/{lesson}", UriKind.Relative), Ct);
        read.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await AutonomyAsync(null)).Status.ShouldBe(HttpStatusCode.Forbidden); // off first: a missing password says nothing
    }

    [Fact]
    public async Task One_capability_does_not_open_the_other()
    {
        await OnAsync("learning_moves_ui");
        var lesson = await OpenLessonAsync();

        (await AutonomyAsync(Factory.OwnerPassword)).Status.ShouldBe(HttpStatusCode.Forbidden);
        (await LessonAsync(lesson, "dismiss", Factory.OwnerPassword)).Status.ShouldBe(HttpStatusCode.OK);
        await using (var connection = await OpenAsync())
        {
            await ExecuteAsync(connection, "INSERT INTO platform.capability_changes (capability, enabled, reason, actor) VALUES ('learning_moves_ui', false, 'off', 'owner:test@example.test')");
        }

        (await LessonAsync(await OpenLessonAsync(), "dismiss", Factory.OwnerPassword)).Status.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task With_the_capabilities_on_a_missing_or_wrong_password_is_401_for_all_six_and_writes_nothing()
    {
        await OnAsync("learning_moves_ui", "autonomy_form");
        var judged = await JudgedAsync();
        var lesson = await OpenLessonAsync();
        var rows = await CellRowsAsync();
        var before = await RowsOfAsync();
        var calls = new List<Func<string?, Task<(HttpStatusCode Status, JsonElement Body)>>>
        {
            p => LinkAsync(lesson, "hypothesis", judged, p), p => LessonAsync(lesson, "close", p), p => LessonAsync(lesson, "dismiss", p), p => HypothesisAsync(judged, "rejected", p), p => RevertAsync(rows[1], p), p => AutonomyAsync(p),
        };

        foreach (var call in calls)
        {
            foreach (var password in new string?[] { null, "not-the-password" })
            {
                var (status, body) = await call(password);
                status.ShouldBe(HttpStatusCode.Unauthorized);
                Code(body, "auth.invalid_credentials");
            }

            await Factory.RestoreOwnerAsync();
            await ResignInAsync();
        }

        (await RowsOfAsync()).ShouldBe(before);
    }

    [Fact]
    public async Task The_fifth_wrong_password_locks_and_a_correct_one_is_then_refused_too()
    {
        await OnAsync("learning_moves_ui");
        var lesson = await OpenLessonAsync();
        _rotate = false; // a locked account cannot sign in again
        try
        {
            var statuses = new List<HttpStatusCode>();
            for (var i = 0; i < 6; i++)
            {
                statuses.Add((await LessonAsync(lesson, "dismiss", "wrong-" + i)).Status);
            }

            statuses.ShouldContain(HttpStatusCode.Locked);
            (await LessonAsync(lesson, "dismiss", Factory.OwnerPassword)).Status.ShouldBeOneOf(HttpStatusCode.Locked, HttpStatusCode.Unauthorized);
            (await CountAsync("research.lessons", $"lesson_id = {lesson} AND state = 'dismissed'")).ShouldBe(0);
        }
        finally
        {
            await Factory.RestoreOwnerAsync();
        }
    }

    [Fact]
    public async Task Without_a_session_every_write_is_a_401()
    {
        await OnAsync("learning_moves_ui", "autonomy_form");
        using var anonymous = Factory.CreateAnonymousClient();
        var answers = new[]
        {
            await PostAsync("/api/lessons/1/link", new { fixKind = "hypothesis", fixId = 1, reason = "x", password = Factory.OwnerPassword }, anonymous),
            await PostAsync("/api/lessons/1/close", new { reason = "x", password = Factory.OwnerPassword }, anonymous),
            await PostAsync("/api/lessons/1/dismiss", new { reason = "x", password = Factory.OwnerPassword }, anonymous),
            await PostAsync("/api/hypotheses/1/move", new { to = "rejected", reason = "x", password = Factory.OwnerPassword }, anonymous),
            await PostAsync("/api/changelog/1/revert", new { reason = "x", password = Factory.OwnerPassword }, anonymous),
            await AutonomyAsync(Factory.OwnerPassword, client: anonymous),
        };

        answers.ShouldAllBe(a => a.Status == HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_lesson_walks_link_then_close_as_the_session_owner_the_result_is_the_stored_experiments_and_the_audit_has_no_text()
    {
        await OnAsync("learning_moves_ui");
        var judged = await JudgedAsync();
        var lesson = await OpenLessonAsync();

        var linked = await PostAsync($"/api/lessons/{lesson}/link", new { fixKind = "hypothesis", fixId = judged, reason = Secret, password = Factory.OwnerPassword, actor = "owner:evil@example.test", state = "closed" });
        var closed = await LessonAsync(lesson, "close", Factory.OwnerPassword, extra: new { result = "improved", beforeMeanR = 99.0, afterMeanR = 100.0, actor = "owner:evil@example.test" });

        linked.Status.ShouldBe(HttpStatusCode.OK);
        linked.Body.GetProperty("state").GetString().ShouldBe("linked");
        closed.Status.ShouldBe(HttpStatusCode.OK);
        closed.Body.GetProperty("state").GetString().ShouldBe("closed");
        var detail = await Client.GetFromJsonAsync<JsonElement>(new Uri($"/api/lessons/{lesson}", UriKind.Relative), Ct);
        var experiment = await Client.GetFromJsonAsync<JsonElement>(new Uri($"/api/experiments/{detail.GetProperty("experimentId").GetInt64()}", UriKind.Relative), Ct);
        detail.GetProperty("result").GetString().ShouldBe(experiment.GetProperty("outcome").GetString() == "Better" ? "improved" : "no_measurable_change");
        detail.GetProperty("beforeTotalR").GetDouble().ShouldBe(experiment.GetProperty("baselineTotalR").GetDouble());
        detail.GetProperty("afterTotalR").GetDouble().ShouldNotBe(100.0);
        (await ScalarAsync<long>($"SELECT count(*) FROM research.lessons WHERE lesson_id = {lesson} AND actor = 'owner:{ApiFactory.OwnerEmail}' AND state IN ('linked','closed')")).ShouldBe(2);
        (await CountAsync("research.lessons", "actor LIKE '%evil%'")).ShouldBe(0);

        var feed = await ScalarAsync<string>("SELECT string_agg(event || ' ' || coalesce(detail, '') || ' ' || actor, ' | ' ORDER BY at, id) FROM accounts.account_events WHERE event = 'LessonMoved'");
        feed.ShouldContain($"LessonMoved lesson={lesson} link to=linked");
        feed.ShouldContain($"LessonMoved lesson={lesson} close to=closed");
        feed.ShouldNotContain(Secret);
        feed.ShouldNotContain(Factory.OwnerPassword);
        feed.ShouldNotContain("evil");
    }

    [Fact]
    public async Task Every_lesson_move_pair_is_the_domains_table_and_a_refused_one_says_why_and_writes_nothing()
    {
        await OnAsync("learning_moves_ui");
        var judged = await JudgedAsync();
        var legal = new HashSet<(string From, string Verb)> { ("open", "link"), ("open", "dismiss"), ("linked", "close"), ("linked", "dismiss") };

        foreach (var from in new[] { "open", "linked", "closed", "dismissed" })
        {
            foreach (var verb in new[] { "link", "close", "dismiss" })
            {
                var id = await LessonInAsync(from, judged);
                var rows = await CountAsync("research.lessons", $"lesson_id = {id}");

                var (status, body) = verb == "link" ? await LinkAsync(id, "hypothesis", judged, Factory.OwnerPassword) : await LessonAsync(id, verb, Factory.OwnerPassword);

                if (legal.Contains((from, verb)))
                {
                    status.ShouldBe(HttpStatusCode.OK, $"{from} {verb}");
                    (await CountAsync("research.lessons", $"lesson_id = {id}")).ShouldBe(rows + 1);
                }
                else
                {
                    status.ShouldBe(HttpStatusCode.Conflict, $"{from} {verb}");
                    Code(body, "lessons.move_refused");
                    body.GetProperty("title").GetString().ShouldNotBeNullOrWhiteSpace();
                    (await CountAsync("research.lessons", $"lesson_id = {id}")).ShouldBe(rows);
                }
            }
        }

        var closed = await LessonInAsync("closed", judged);
        (await LessonAsync(closed, "dismiss", Factory.OwnerPassword)).Body.GetProperty("title").GetString()!.ShouldContain("final");
    }

    [Fact]
    public async Task A_lesson_refusal_names_its_reason_an_unknown_lesson_or_fix_is_a_404_and_a_blank_reason_or_unknown_kind_is_a_422()
    {
        await OnAsync("learning_moves_ui");
        var (baseKey, _) = await StoreRunsAsync();
        var unjudged = await ProposedAsync(baseKey);
        var lesson = await OpenLessonAsync();

        var unknown = await LessonAsync(9999, "dismiss", Factory.OwnerPassword);
        var noFix = await LinkAsync(lesson, "hypothesis", 9999, Factory.OwnerPassword);
        var noExperimentFix = await LinkAsync(lesson, "experiment", 9999, Factory.OwnerPassword);
        var badKind = await LinkAsync(lesson, "model", 1, Factory.OwnerPassword);
        var blank = await LessonAsync(lesson, "dismiss", Factory.OwnerPassword, reason: " ");
        var tooLong = await LessonAsync(lesson, "dismiss", Factory.OwnerPassword, reason: new string('x', 501));
        (await LinkAsync(lesson, "hypothesis", unjudged, Factory.OwnerPassword)).Status.ShouldBe(HttpStatusCode.OK);
        var noExperiment = await LessonAsync(lesson, "close", Factory.OwnerPassword);

        unknown.Status.ShouldBe(HttpStatusCode.NotFound);
        Code(unknown.Body, "lessons.not_found");
        noFix.Status.ShouldBe(HttpStatusCode.NotFound);
        Code(noFix.Body, "lessons.fix_not_found");
        Code(noExperimentFix.Body, "lessons.fix_not_found");
        (badKind.Status, blank.Status, tooLong.Status).ShouldBe((HttpStatusCode.UnprocessableEntity, HttpStatusCode.UnprocessableEntity, HttpStatusCode.UnprocessableEntity));
        Code(badKind.Body, "learning.invalid");
        noExperiment.Status.ShouldBe(HttpStatusCode.Conflict);
        Code(noExperiment.Body, "lessons.move_refused");
        noExperiment.Body.GetProperty("title").GetString()!.ShouldContain("no recorded experiment");
    }

    [Fact]
    public async Task A_hypothesis_moves_only_to_the_four_words_by_the_table_and_a_move_of_the_wrong_kind_is_a_422()
    {
        await OnAsync("learning_moves_ui");
        var (baseKey, _) = await StoreRunsAsync();
        var proposed = await ProposedAsync(baseKey);
        var judged = await JudgedAsync();

        foreach (var word in new[] { "proposed", "running", "judged", "nonsense", "" })
        {
            var refused = await HypothesisAsync(proposed, word, Factory.OwnerPassword);
            refused.Status.ShouldBe(HttpStatusCode.UnprocessableEntity, word);
            Code(refused.Body, "learning.invalid");
        }

        var accepted = await HypothesisAsync(judged, "accepted", Factory.OwnerPassword);
        var approved = await HypothesisAsync(proposed, "approved", Factory.OwnerPassword, extra: new { autonomy = "auto", actor = "owner:evil@example.test" });
        var rejected = await HypothesisAsync(judged, "rejected", Factory.OwnerPassword);
        var back = await HypothesisAsync(judged, "approved", Factory.OwnerPassword);
        var unknown = await HypothesisAsync(9999, "rejected", Factory.OwnerPassword);

        accepted.Status.ShouldBe(HttpStatusCode.Conflict);
        Code(accepted.Body, "hypotheses.move_refused");
        accepted.Body.GetProperty("title").GetString()!.ShouldContain("Only a Better experiment can be accepted.");
        approved.Status.ShouldBe(HttpStatusCode.OK);
        approved.Body.GetProperty("state").GetString().ShouldBe("approved");
        rejected.Status.ShouldBe(HttpStatusCode.OK);
        back.Status.ShouldBe(HttpStatusCode.Conflict);
        unknown.Status.ShouldBe(HttpStatusCode.NotFound);
        Code(unknown.Body, "hypotheses.not_found");
        (await CountAsync("research.hypotheses", "actor LIKE '%evil%'")).ShouldBe(0);
        (await CountAsync("research.hypotheses", $"actor = 'owner:{ApiFactory.OwnerEmail}'")).ShouldBe(2);
        (await ScalarAsync<string>("SELECT detail FROM accounts.account_events WHERE event = 'HypothesisMoved' ORDER BY at, id LIMIT 1")).ShouldBe($"hypothesis={proposed} to=approved");
    }

    [Fact]
    public async Task A_revert_is_a_new_row_for_the_latest_transition_only_the_old_rows_are_byte_identical_and_a_refusal_lists_its_reasons()
    {
        await OnAsync("learning_moves_ui");
        var rows = await CellRowsAsync();
        var oldRows = await ScalarAsync<string>("SELECT md5(string_agg(t::text, '|' ORDER BY id)) FROM research.learning_changelog t");

        var older = await RevertAsync(rows[0], Factory.OwnerPassword);
        var unknown = await RevertAsync(9999, Factory.OwnerPassword);
        var latest = await RevertAsync(rows[1], Factory.OwnerPassword);
        var again = await RevertAsync(rows[1], Factory.OwnerPassword);

        older.Status.ShouldBe(HttpStatusCode.Conflict);
        Code(older.Body, "changelog.not_revertible");
        older.Body.GetProperty("title").GetString()!.ShouldContain("latest row");
        unknown.Status.ShouldBe(HttpStatusCode.NotFound);
        Code(unknown.Body, "changelog.not_found");
        latest.Status.ShouldBe(HttpStatusCode.OK);
        latest.Body.GetProperty("to").GetString().ShouldBe("candidate");
        again.Status.ShouldBe(HttpStatusCode.Conflict);
        (await CountAsync("research.learning_changelog")).ShouldBe(3);
        (await ScalarAsync<string>($"SELECT md5(string_agg(t::text, '|' ORDER BY id)) FROM research.learning_changelog t WHERE id <= {rows[1]}")).ShouldBe(oldRows);
        (await ScalarAsync<string>("SELECT kind || ' ' || actor FROM research.learning_changelog ORDER BY id DESC LIMIT 1")).ShouldBe($"revert owner:{ApiFactory.OwnerEmail}");
        var state = await Client.GetFromJsonAsync<JsonElement>(new Uri("/api/changelog/cell-state?cell=" + Uri.EscapeDataString(Cell), UriKind.Relative), Ct);
        state.GetProperty("state").GetString().ShouldBe("candidate");
        (await ScalarAsync<string>("SELECT detail FROM accounts.account_events WHERE event = 'ChangelogReverted'")).ShouldBe($"changelog={await ScalarAsync<long>("SELECT max(id) FROM research.learning_changelog")} reverts={rows[1]} paper->candidate");
    }

    [Fact]
    public async Task Two_moves_at_once_write_one_row_and_the_other_is_refused_or_stale()
    {
        await OnAsync("learning_moves_ui");
        var lesson = await OpenLessonAsync();

        var answers = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => LessonAsync(lesson, "dismiss", Factory.OwnerPassword)));

        answers.Count(a => a.Status == HttpStatusCode.OK).ShouldBe(1);
        answers.Where(a => a.Status != HttpStatusCode.OK).ShouldAllBe(a => a.Status == HttpStatusCode.Conflict);
        (await CountAsync("research.lessons", $"lesson_id = {lesson} AND state = 'dismissed'")).ShouldBe(1);
        (await CountAsync("accounts.account_events", "event = 'LessonMoved'")).ShouldBe(1);
    }

    [Fact]
    public async Task The_autonomy_form_is_a_new_row_a_stale_form_writes_nothing_and_the_audit_names_the_switches_only()
    {
        await OnAsync("autonomy_form");

        var first = await AutonomyAsync(Factory.OwnerPassword, on: true, expected: 0);
        var staleNone = await AutonomyAsync(Factory.OwnerPassword, on: false, expected: 0);
        var id = first.Body.GetProperty("id").GetInt64();
        var staleOld = await AutonomyAsync(Factory.OwnerPassword, on: false, expected: id + 1000);
        var second = await AutonomyAsync(Factory.OwnerPassword, on: false, expected: id);
        var noField = await AutonomyAsync(Factory.OwnerPassword, on: true);

        first.Status.ShouldBe(HttpStatusCode.OK);
        (staleNone.Status, staleOld.Status).ShouldBe((HttpStatusCode.Conflict, HttpStatusCode.Conflict));
        Code(staleNone.Body, "autonomy.stale");
        second.Status.ShouldBe(HttpStatusCode.OK);
        noField.Status.ShouldBe(HttpStatusCode.OK);
        (await CountAsync("workflow.autonomy_settings")).ShouldBe(3);
        (await CountAsync("workflow.autonomy_settings", "actor LIKE '%evil%'")).ShouldBe(0);
        var feed = await ScalarAsync<string>("SELECT string_agg(detail, ' | ' ORDER BY at, id) FROM accounts.account_events WHERE event = 'AutonomySettingsChanged'");
        feed.ShouldBe($"settings={id} auto_approve=on reserve=off | settings={second.Body.GetProperty("id").GetInt64()} auto_approve=off reserve=off | settings={noField.Body.GetProperty("id").GetInt64()} auto_approve=on reserve=off");
        (await Client.GetFromJsonAsync<JsonElement>(Settings, Ct)).GetProperty("notice").GetString().ShouldNotBeNullOrWhiteSpace();
        var bad = await PostAsync("/api/autonomy/settings", new { }); // a POST is not a route
        bad.Status.ShouldBeOneOf(HttpStatusCode.MethodNotAllowed, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_bad_autonomy_value_or_a_blank_reason_with_everything_right_is_a_400_and_writes_nothing()
    {
        await OnAsync("autonomy_form");
        using var bad = await Client.PutAsJsonAsync(Settings, new { autoApproveTickets = true, promoteFromReserve = false, minPWin = 0.1, maxAutoPerDay = 2, reason = "x", password = Factory.OwnerPassword }, Ct);
        using var noReason = await Client.PutAsJsonAsync(Settings, new { autoApproveTickets = true, promoteFromReserve = false, minPWin = 0.6, maxAutoPerDay = 2, reason = " ", password = Factory.OwnerPassword }, Ct);

        (bad.StatusCode, noReason.StatusCode).ShouldBe((HttpStatusCode.BadRequest, HttpStatusCode.BadRequest));
        (await CountAsync("workflow.autonomy_settings")).ShouldBe(0);
        (await CountAsync("accounts.account_events", "event = 'AutonomySettingsChanged'")).ShouldBe(0);
    }

    [Fact]
    public async Task A_move_neither_opens_extends_nor_uses_up_the_step_up_window_and_is_never_cached()
    {
        await OnAsync("learning_moves_ui");
        var elevated = await Factory.SignInElevatedAsync(Ct);
        var before = await ScalarAsync<DateTime>("SELECT elevated_until FROM auth.sessions WHERE elevated_until IS NOT NULL ORDER BY elevated_until DESC LIMIT 1");
        var lesson = await OpenLessonAsync();

        using var withoutPassword = await elevated.Client.PostAsJsonAsync(new Uri($"/api/lessons/{lesson}/dismiss", UriKind.Relative), new { reason = "x" }, Ct);
        using var done = await elevated.Client.PostAsJsonAsync(new Uri($"/api/lessons/{lesson}/dismiss", UriKind.Relative), new { reason = "x", password = Factory.OwnerPassword }, Ct);

        withoutPassword.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        done.StatusCode.ShouldBe(HttpStatusCode.OK);
        done.Headers.CacheControl?.NoStore.ShouldBeTrue();
        (await ScalarAsync<DateTime>("SELECT elevated_until FROM auth.sessions WHERE elevated_until IS NOT NULL ORDER BY elevated_until DESC LIMIT 1")).ShouldBe(before);
    }

    [Fact]
    public async Task All_six_writes_change_only_their_own_table_and_the_event_feed_nothing_that_configures_anything_moves()
    {
        await OnAsync("learning_moves_ui", "autonomy_form");
        var judged = await JudgedAsync();
        var lesson = await OpenLessonAsync();
        var rows = await CellRowsAsync();
        var sums = new Dictionary<string, string>();
        foreach (var table in UntouchedTables)
        {
            sums[table] = await ScalarAsync<string>($"SELECT coalesce(md5(string_agg(t::text, '|' ORDER BY t::text)), 'empty') FROM {table} t");
        }

        (await LinkAsync(lesson, "hypothesis", judged, Factory.OwnerPassword)).Status.ShouldBe(HttpStatusCode.OK);
        (await LessonAsync(lesson, "close", Factory.OwnerPassword)).Status.ShouldBe(HttpStatusCode.OK);
        (await HypothesisAsync(judged, "rejected", Factory.OwnerPassword)).Status.ShouldBe(HttpStatusCode.OK);
        (await RevertAsync(rows[1], Factory.OwnerPassword)).Status.ShouldBe(HttpStatusCode.OK);
        (await AutonomyAsync(Factory.OwnerPassword, on: true)).Status.ShouldBe(HttpStatusCode.OK);
        (await LessonAsync(await OpenLessonAsync(), "dismiss", Factory.OwnerPassword)).Status.ShouldBe(HttpStatusCode.OK);

        foreach (var table in UntouchedTables)
        {
            (await ScalarAsync<string>($"SELECT coalesce(md5(string_agg(t::text, '|' ORDER BY t::text)), 'empty') FROM {table} t")).ShouldBe(sums[table], table);
        }

        (await CountAsync("accounts.account_events", "event = 'LessonMoved'")).ShouldBe(3);
        (await CountAsync("accounts.account_events", "event IN ('HypothesisMoved','ChangelogReverted','AutonomySettingsChanged')")).ShouldBe(3);
        (await CountAsync("workflow.approvals")).ShouldBe(0);
        Events.Length.ShouldBe(4);
    }
}
