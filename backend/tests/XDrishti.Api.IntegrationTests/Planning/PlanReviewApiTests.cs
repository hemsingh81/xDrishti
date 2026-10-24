using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Autonomy;
using XDrishti.Application.Planning;
using XDrishti.Domain.Planning;

namespace XDrishti.Api.IntegrationTests.Planning;

/// <summary>
/// Spec P7-10 (ADR 0095) over HTTP and a real database: the owner's approve / modify / reject / approve-all need the session and the CSRF token and are written as the signed-in owner whatever the body says; a check that is not a pass blocks an approval
/// unless the owner names it for the account; nothing is approved after the cutoff; the review job lapses what nobody decided and approves nothing; with the autonomy setting OFF (the default) no automatic row ever exists.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class PlanReviewApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly Guid AccountId = Guid.Parse("0b6f2a9e-7a52-4d7e-9c3a-5d0a7e0b7a10");

    private static readonly Guid OtherAccountId = Guid.Parse("0b6f2a9e-7a52-4d7e-9c3a-5d0a7e0b7a11");

    private static readonly DateOnly Session = new(2099, 1, 5);

    private static readonly DateTimeOffset ReviewCutoff = new(2099, 1, 5, 3, 35, 0, TimeSpan.Zero);

    private static readonly string OwnerActor = "owner:" + ApiFactory.OwnerEmail;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await using var connection = await OpenAsync();
        foreach (var (id, slug, client) in new[] { (AccountId, "probe-review", "1000000090"), (OtherAccountId, "probe-review-2", "1000000091") })
        {
            await ExecuteAsync(
                connection,
                """
                INSERT INTO accounts.broker_accounts (id, slug, name, broker, client_id, api_key_issued_on, color, created_at, updated_at, data_enabled, data_primary, data_standby, live_feed_budget)
                VALUES (@id, @slug, 'Probe', 'Dhan', @client, current_date, '#1A2B3C', now(), now(), false, false, false, 200)
                """,
                ("id", id), ("slug", slug), ("client", client));
        }
    }

    private static NewPlanItem Item(int rank, TicketGroup group = TicketGroup.Primary, string[]? standings = null, bool wired = true, string symbol = "NSE:RELIANCE") =>
        new(
            rank, group, symbol, "BUY", "ORB", new string('a', 64), "close above 15m high", "0.8 ATR below entry", "2R", "09:30-11:30",
            group == TicketGroup.Primary ? [] : ["rank_below_cut"],
            new PlanEvidence("ORB|15m|BUY|intraday|FIXED_RR|*", 41, "about 2.1 years, replayed history, modelled fills, no hold-out", "shrunk posterior win rate of the cell", 0.72, 0.12, [.. standings ?? [], .. wired ? ["allocation_entry_is_indicative"] : (string[])[]], null, 0.7),
            [new NewAllocation(AccountId, "paper", 10)]);

    private async Task<TResult> WithAsync<TResult>(Func<IServiceProvider, Task<TResult>> work)
    {
        using var scope = Factory.Services.CreateScope();
        return await work(scope.ServiceProvider);
    }

    private async Task<(long Plan, long[] Items)> StoredAsync(params NewPlanItem[] items)
    {
        var plan = new NewPlan("review-2099-01-05", Session, ReviewCutoff, 1, new string('c', 64), "cell statistics only", true, "test", DateTimeOffset.UnixEpoch, items);
        var published = await WithAsync(p => p.GetRequiredService<IPlanStore>().PublishAsync(plan, Ct).AsTask());
        var rows = await WithAsync(p => p.GetRequiredService<IPlanStore>().ItemsAsync(published.Id, Ct).AsTask());
        return (published.Id, [.. rows.Select(r => r.Id)]);
    }

    private Task<HttpResponseMessage> PostAsync(string path, object? body = null, HttpClient? client = null) =>
        (client ?? Client).PostAsJsonAsync(new Uri(path, UriKind.Relative), body ?? new { }, Ct);

    private static object Confirm(Guid account, string code, string reason) => new { confirmations = new[] { new { accountId = account, codes = new[] { code }, reason } } };

    private static async Task<string?> CodeAsync(HttpResponseMessage response) => (await response.ReadProblemAsync(Ct)).Code;

    private Task<long> ApprovalsAsync(long item, string kind = "owner") =>
        ScalarAsync<long>("SELECT count(*) FROM workflow.approvals WHERE item_id = @i AND kind = @k", ("i", item), ("k", kind));

    private Task<string> StateAsync(long item) =>
        ScalarAsync<string>("SELECT COALESCE((SELECT to_state FROM trading.plan_events WHERE item_id = @i ORDER BY id DESC LIMIT 1), 'proposed')", ("i", item));

    private Task<string> PlanStateAsync(long plan) =>
        ScalarAsync<string>("SELECT COALESCE((SELECT to_state FROM trading.plan_events WHERE plan_id = @p AND item_id IS NULL ORDER BY id DESC LIMIT 1), 'draft')", ("p", plan));

    [Fact]
    public async Task Without_a_session_every_write_is_401_and_without_the_csrf_token_403_and_nothing_is_written()
    {
        var (plan, items) = await StoredAsync(Item(1));
        var anonymous = Factory.CreateAnonymousClient();
        var noCsrf = Factory.CreateAnonymousClient();
        noCsrf.DefaultRequestHeaders.Add("Cookie", Owner.CookieHeader);
        var paths = new[] { $"/api/plans/{plan}/items/{items[0]}/approve", $"/api/plans/{plan}/items/{items[0]}/modify", $"/api/plans/{plan}/items/{items[0]}/reject", $"/api/plans/{plan}/approve-all" };

        foreach (var path in paths)
        {
            using var unauthenticated = await PostAsync(path, client: anonymous);
            using var forged = await PostAsync(path, client: noCsrf);

            unauthenticated.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, path);
            forged.StatusCode.ShouldBe(HttpStatusCode.Forbidden, path);
        }

        (await ScalarAsync<long>("SELECT count(*) FROM workflow.approvals")).ShouldBe(0);
        (await StateAsync(items[0])).ShouldBe("proposed");
    }

    [Fact]
    public async Task The_actor_is_the_signed_in_owner_whatever_the_body_says()
    {
        var (_, items) = await StoredAsync(Item(1));
        var plan = await ScalarAsync<long>("SELECT id FROM trading.plans");

        using var response = await PostAsync($"/api/plans/{plan}/items/{items[0]}/approve", new { actor = "rule:autonomy", kind = "auto", reason = "x", settingsId = 1 });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ScalarAsync<string>("SELECT actor FROM workflow.approvals WHERE item_id = @i", ("i", items[0]))).ShouldBe(OwnerActor);
        (await ScalarAsync<string>("SELECT kind FROM workflow.approvals WHERE item_id = @i", ("i", items[0]))).ShouldBe("owner");
        (await ScalarAsync<string>("SELECT actor FROM trading.plan_events WHERE item_id = @i", ("i", items[0]))).ShouldBe(OwnerActor);
        (await ScalarAsync<long>("SELECT count(*) FROM workflow.approvals WHERE actor LIKE 'rule:%' OR kind <> 'owner'")).ShouldBe(0);
    }

    [Fact]
    public async Task Approving_a_ticket_with_every_check_passed_writes_one_owner_row_and_moves_the_plan_into_review()
    {
        var (plan, items) = await StoredAsync(Item(1));

        using var response = await PostAsync($"/api/plans/{plan}/items/{items[0]}/approve", new { reason = "looks right" });

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        body.GetProperty("state").GetString().ShouldBe("approved");
        body.GetProperty("notice").GetString().ShouldBe("Approved for watching: no order is placed and none exists.");
        (await StateAsync(items[0])).ShouldBe("approved");
        (await PlanStateAsync(plan)).ShouldBe("inreview");
        (await ApprovalsAsync(items[0])).ShouldBe(1);
        (await ScalarAsync<object>("SELECT confirm_reason FROM workflow.approvals WHERE item_id = @i", ("i", items[0]))).ShouldBe(DBNull.Value);
    }

    [Fact]
    public async Task A_second_approval_of_the_same_ticket_is_a_409_and_never_a_second_row_also_when_sent_at_once()
    {
        var (plan, items) = await StoredAsync(Item(1));

        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => PostAsync($"/api/plans/{plan}/items/{items[0]}/approve")));
        using var later = await PostAsync($"/api/plans/{plan}/items/{items[0]}/approve");

        results.Count(r => r.StatusCode == HttpStatusCode.OK).ShouldBe(1);
        results.Where(r => r.StatusCode != HttpStatusCode.OK).ShouldAllBe(r => r.StatusCode == HttpStatusCode.Conflict);
        later.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ApprovalsAsync(items[0])).ShouldBe(1);
        (await ScalarAsync<long>("SELECT count(*) FROM trading.plan_events WHERE item_id = @i", ("i", items[0]))).ShouldBe(1);
    }

    [Fact]
    public async Task An_unavailable_check_refuses_the_approval_until_the_owner_names_its_code_for_the_account_with_a_reason()
    {
        var (plan, items) = await StoredAsync(Item(1, standings: ["checklist:data_quality:unavailable"]));
        var path = $"/api/plans/{plan}/items/{items[0]}/approve";

        using var plain = await PostAsync(path);
        using var wrongAccount = await PostAsync(path, Confirm(OtherAccountId, "data_quality", "elsewhere"));
        using var wrongCode = await PostAsync(path, Confirm(AccountId, "fno_ban", "wrong check"));
        using var noReason = await PostAsync(path, Confirm(AccountId, "data_quality", " "));
        using var gate = await PostAsync(path, Confirm(AccountId, "account_paper", "override"));

        plain.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeAsync(plain)).ShouldBe("plans.checklist_blocked");
        (await CodeAsync(wrongAccount)).ShouldBe("plans.checklist_blocked");
        (await CodeAsync(wrongCode)).ShouldBe("plans.checklist_blocked");
        noReason.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CodeAsync(noReason)).ShouldBe("checklist.reason_required");
        (await CodeAsync(gate)).ShouldBe("checklist.codes_invalid");
        (await ApprovalsAsync(items[0])).ShouldBe(0);

        using var named = await PostAsync(path, Confirm(AccountId, "data_quality", "feed checked by hand"));

        named.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ApprovalsAsync(items[0])).ShouldBe(1);
        (await ScalarAsync<string>("SELECT confirm_reason FROM workflow.approvals WHERE item_id = @i", ("i", items[0]))).ShouldBe($"[{AccountId}] overrode data_quality: feed checked by hand");
    }

    [Fact]
    public async Task A_ticket_without_a_stored_checklist_cannot_be_approved_even_with_a_confirmation()
    {
        var (plan, items) = await StoredAsync(Item(1, wired: false));

        using var response = await PostAsync($"/api/plans/{plan}/items/{items[0]}/approve", Confirm(AccountId, "data_quality", "trust me"));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeAsync(response)).ShouldBe("plans.checklist_blocked");
        (await ApprovalsAsync(items[0])).ShouldBe(0);
    }

    [Fact]
    public async Task A_live_allocation_cannot_be_stored_so_a_live_account_never_receives_a_ticket_to_approve()
    {
        var live = Item(1) with { Allocations = [new NewAllocation(AccountId, "live", 10)] };

        var publish = () => StoredAsync(live);

        await Should.ThrowAsync<Exception>(publish);
    }

    [Fact]
    public async Task Rejecting_works_and_a_rejected_ticket_cannot_be_approved()
    {
        var (plan, items) = await StoredAsync(Item(1));

        using var rejected = await PostAsync($"/api/plans/{plan}/items/{items[0]}/reject", new { reason = "not today" });
        using var approve = await PostAsync($"/api/plans/{plan}/items/{items[0]}/approve");
        using var blank = await PostAsync($"/api/plans/{plan}/items/{items[0]}/reject", new { reason = "" });

        rejected.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await StateAsync(items[0])).ShouldBe("rejected");
        approve.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        blank.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await ApprovalsAsync(items[0])).ShouldBe(1);
    }

    [Fact]
    public async Task After_the_cutoff_an_approval_and_a_modification_are_refused_and_a_rejection_is_still_allowed()
    {
        var (plan, items) = await StoredAsync(Item(1), Item(2), Item(3));
        Factory.Clock.Pin(ReviewCutoff.AddMinutes(1));
        await ResignInAsync();

        using var approve = await PostAsync($"/api/plans/{plan}/items/{items[0]}/approve");
        using var modify = await PostAsync($"/api/plans/{plan}/items/{items[1]}/modify", new { reason = "less", quantities = new[] { new { accountId = AccountId, quantity = 4 } } });
        using var all = await PostAsync($"/api/plans/{plan}/approve-all");
        using var reject = await PostAsync($"/api/plans/{plan}/items/{items[2]}/reject", new { reason = "too late to take" });

        (await CodeAsync(approve)).ShouldBe("plans.closed");
        (await CodeAsync(modify)).ShouldBe("plans.closed");
        (await CodeAsync(all)).ShouldBe("plans.closed");
        reject.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ScalarAsync<long>("SELECT count(*) FROM workflow.approvals WHERE decision IN ('approve', 'modify')")).ShouldBe(0);
        (await StateAsync(items[0])).ShouldBe("proposed");
    }

    [Fact]
    public async Task A_modification_can_only_lower_a_size_and_the_modified_ticket_is_approved_through_the_same_checklist()
    {
        var (plan, items) = await StoredAsync(Item(1, standings: ["checklist:data_quality:unavailable"]));
        var modify = $"/api/plans/{plan}/items/{items[0]}/modify";

        using var higher = await PostAsync(modify, new { reason = "more", quantities = new[] { new { accountId = AccountId, quantity = 11 } } });
        using var otherAccount = await PostAsync(modify, new { reason = "add", quantities = new[] { new { accountId = OtherAccountId, quantity = 1 } } });
        using var none = await PostAsync(modify, new { reason = "nothing", quantities = new List<object>() });
        using var lower = await PostAsync(modify, new { reason = "smaller", quantities = new[] { new { accountId = AccountId, quantity = 4 } } });
        using var again = await PostAsync(modify, new { reason = "smaller still", quantities = new[] { new { accountId = AccountId, quantity = 3 } } });
        using var blocked = await PostAsync($"/api/plans/{plan}/items/{items[0]}/approve");
        using var approved = await PostAsync($"/api/plans/{plan}/items/{items[0]}/approve", Confirm(AccountId, "data_quality", "checked"));

        higher.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CodeAsync(higher)).ShouldBe("plans.modification_invalid");
        (await CodeAsync(otherAccount)).ShouldBe("plans.modification_invalid");
        (await CodeAsync(none)).ShouldBe("plans.modification_invalid");
        lower.StatusCode.ShouldBe(HttpStatusCode.OK);
        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeAsync(blocked)).ShouldBe("plans.checklist_blocked");
        approved.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ScalarAsync<string>("SELECT modifications FROM workflow.approvals WHERE decision = 'modify'")).ShouldBe($"qty:{AccountId}=4");
        (await ScalarAsync<long>("SELECT count(*) FROM workflow.approvals WHERE item_id = @i", ("i", items[0]))).ShouldBe(2);
        (await StateAsync(items[0])).ShouldBe("approved");

        var review = await Client.GetFromJsonAsync<JsonElement>(new Uri($"/api/plans/{plan}/review", UriKind.Relative), Ct);
        review.GetProperty("tickets")[0].GetProperty("accounts")[0].GetProperty("effectiveQuantity").GetInt32().ShouldBe(4);
    }

    [Fact]
    public async Task A_stored_modification_that_no_longer_holds_stops_the_approval_of_a_modified_ticket()
    {
        var (plan, items) = await StoredAsync(Item(1));
        using var modified = await PostAsync($"/api/plans/{plan}/items/{items[0]}/modify", new { reason = "smaller", quantities = new[] { new { accountId = AccountId, quantity = 4 } } });
        await using (var connection = await OpenAsync())
        {
            await ExecuteAsync(connection, "INSERT INTO workflow.approvals (plan_id, item_id, kind, decision, actor, reason, modifications, cutoff, decided_at) SELECT plan_id, id, 'owner', 'modify', 'owner:probe', 'forced', @m, (SELECT review_cutoff FROM trading.plans WHERE id = plan_id), now() FROM trading.plan_items WHERE id = @i", ("m", $"qty:{AccountId}=99"), ("i", items[0]));
        }

        using var approve = await PostAsync($"/api/plans/{plan}/items/{items[0]}/approve");

        modified.StatusCode.ShouldBe(HttpStatusCode.OK);
        approve.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CodeAsync(approve)).ShouldBe("plans.modification_invalid");
        (await StateAsync(items[0])).ShouldBe("modified");
    }

    [Fact]
    public async Task Approve_all_takes_the_primary_tickets_with_every_check_passed_and_lists_the_others_and_never_touches_the_reserve()
    {
        var (plan, items) = await StoredAsync(
            Item(1),
            Item(2, standings: ["checklist:data_quality:unavailable"]),
            Item(3, standings: ["checklist:holdings_conflict:fail"]),
            Item(4, TicketGroup.Reserve),
            Item(5, wired: false));

        using var response = await PostAsync($"/api/plans/{plan}/approve-all");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        body.GetProperty("approved").EnumerateArray().Select(e => e.GetInt64()).ShouldBe([items[0]]);
        body.GetProperty("skipped").EnumerateArray().Select(e => e.GetProperty("itemId").GetInt64()).ShouldBe([items[1], items[2], items[4]]);
        body.GetProperty("notice").GetString().ShouldBe("Approved for watching: no order is placed and none exists.");
        (await StateAsync(items[0])).ShouldBe("approved");
        (await StateAsync(items[1])).ShouldBe("proposed");
        (await StateAsync(items[2])).ShouldBe("proposed");
        (await StateAsync(items[3])).ShouldBe("proposed");
        (await ScalarAsync<long>("SELECT count(*) FROM workflow.approvals")).ShouldBe(1);
        (await ScalarAsync<object>("SELECT confirm_reason FROM workflow.approvals WHERE item_id = @i", ("i", items[0]))).ShouldBe(DBNull.Value);
    }

    [Fact]
    public async Task The_review_shows_the_stored_checks_per_account_and_what_needs_a_confirmation()
    {
        var (plan, items) = await StoredAsync(Item(1, standings: ["checklist:data_quality:unavailable"]), Item(2, wired: false));

        var review = await Client.GetFromJsonAsync<JsonElement>(new Uri($"/api/plans/{plan}/review", UriKind.Relative), Ct);
        using var missing = await Client.GetAsync(new Uri("/api/plans/999999/review", UriKind.Relative), Ct);

        review.GetProperty("open").GetBoolean().ShouldBeTrue();
        review.GetProperty("notice").GetString().ShouldBe("Approved for watching: no order is placed and none exists.");
        var first = review.GetProperty("tickets")[0];
        first.GetProperty("itemId").GetInt64().ShouldBe(items[0]);
        first.GetProperty("canApprove").GetBoolean().ShouldBeFalse();
        first.GetProperty("needsConfirmation").EnumerateArray().Select(e => e.GetString()).ShouldBe([$"{AccountId}:data_quality"]);
        first.GetProperty("accounts")[0].GetProperty("checks").GetArrayLength().ShouldBe(14);
        first.GetProperty("accounts")[0].GetProperty("checks").EnumerateArray().Single(c => c.GetProperty("code").GetString() == "data_quality").GetProperty("standing").GetString().ShouldBe("needsconfirmation");
        review.GetProperty("tickets")[1].GetProperty("checklistRecorded").GetBoolean().ShouldBeFalse();
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_review_job_lapses_what_nobody_decided_at_the_cutoff_and_approves_nothing()
    {
        var (plan, items) = await StoredAsync(Item(1), Item(2), Item(3, TicketGroup.Reserve));
        using var approved = await PostAsync($"/api/plans/{plan}/items/{items[0]}/approve");
        Factory.Clock.Pin(ReviewCutoff);

        var result = await WithAsync(p => p.GetRequiredService<ICommandHandler<RunReviewCycleCommand, ReviewCycleOutcome>>().HandleAsync(new RunReviewCycleCommand(), Ct).AsTask());

        approved.StatusCode.ShouldBe(HttpStatusCode.OK);
        result.Value.Lapsed.ShouldBe(2);
        (await StateAsync(items[0])).ShouldBe("approved");
        (await StateAsync(items[1])).ShouldBe("lapsed");
        (await StateAsync(items[2])).ShouldBe("lapsed");
        (await PlanStateAsync(plan)).ShouldBe("approved");
        (await ScalarAsync<long>("SELECT count(*) FROM workflow.approvals")).ShouldBe(1);
        (await ScalarAsync<string>("SELECT actor FROM trading.plan_events WHERE item_id = @i AND to_state = 'lapsed'", ("i", items[1]))).ShouldBe("system:review-cutoff");

        var again = await WithAsync(p => p.GetRequiredService<ICommandHandler<RunReviewCycleCommand, ReviewCycleOutcome>>().HandleAsync(new RunReviewCycleCommand(), Ct).AsTask());
        again.Value.Lapsed.ShouldBe(0);
    }

    [Fact]
    public async Task At_the_cutoff_an_approval_racing_the_lapse_cannot_win_so_the_ticket_lapses_with_no_approval_row()
    {
        var (plan, items) = await StoredAsync(Item(1));
        Factory.Clock.Pin(ReviewCutoff);
        await ResignInAsync();

        var approve = PostAsync($"/api/plans/{plan}/items/{items[0]}/approve");
        var lapse = WithAsync(p => p.GetRequiredService<ICommandHandler<RunReviewCycleCommand, ReviewCycleOutcome>>().HandleAsync(new RunReviewCycleCommand(), Ct).AsTask());
        await Task.WhenAll(approve, lapse);

        (await approve).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await StateAsync(items[0])).ShouldBe("lapsed");
        (await ScalarAsync<long>("SELECT count(*) FROM workflow.approvals")).ShouldBe(0);
        (await PlanStateAsync(plan)).ShouldBe("lapsed");
    }

    [Fact]
    public async Task With_the_autonomy_setting_off_the_job_writes_no_automatic_row_even_in_the_last_minutes_before_the_cutoff()
    {
        var (_, items) = await StoredAsync(Item(1), Item(2));
        Factory.Clock.Pin(ReviewCutoff.AddMinutes(-1));

        var result = await WithAsync(p => p.GetRequiredService<ICommandHandler<RunReviewCycleCommand, ReviewCycleOutcome>>().HandleAsync(new RunReviewCycleCommand(), Ct).AsTask());

        result.Value.AutoApproved.ShouldBe(0);
        (await ScalarAsync<long>("SELECT count(*) FROM workflow.approvals")).ShouldBe(0);
        (await ScalarAsync<long>("SELECT count(*) FROM workflow.autonomy_settings")).ShouldBe(0);
        (await StateAsync(items[0])).ShouldBe("proposed");
    }

    [Fact]
    public async Task With_the_setting_on_the_rule_still_declines_every_ticket_the_stored_record_marks_and_the_lapse_follows()
    {
        var (_, items) = await StoredAsync(Item(1), Item(2, standings: ["checklist:data_quality:unavailable"]));
        var changed = await WithAsync(p => p.GetRequiredService<ICommandHandler<ChangeAutonomySettingsCommand, AutonomySettingsView>>().HandleAsync(new ChangeAutonomySettingsCommand(OwnerActor, "test", true, false, 0.58, 3), Ct).AsTask());
        Factory.Clock.Pin(ReviewCutoff.AddMinutes(-1));

        var before = await WithAsync(p => p.GetRequiredService<ICommandHandler<RunReviewCycleCommand, ReviewCycleOutcome>>().HandleAsync(new RunReviewCycleCommand(), Ct).AsTask());
        Factory.Clock.Pin(ReviewCutoff);
        var at = await WithAsync(p => p.GetRequiredService<ICommandHandler<RunReviewCycleCommand, ReviewCycleOutcome>>().HandleAsync(new RunReviewCycleCommand(), Ct).AsTask());

        changed.IsSuccess.ShouldBeTrue();
        before.Value.AutoApproved.ShouldBe(0);
        at.Value.Lapsed.ShouldBe(2);
        (await ScalarAsync<long>("SELECT count(*) FROM workflow.approvals")).ShouldBe(0);
        (await StateAsync(items[0])).ShouldBe("lapsed");
    }

    [Fact]
    public async Task The_suggestions_inbox_lists_a_plans_suggestions_and_the_seen_mark_is_one_insert_only_row()
    {
        var (plan, _) = await StoredAsync(Item(1));
        await WithAsync(p => p.GetRequiredService<ISuggestionStore>().InsertAsync(plan, [new NewSuggestion("no_primary_tickets", "plan", "Nothing is primary.")], DateTimeOffset.UnixEpoch, Ct).AsTask());
        var id = await ScalarAsync<long>("SELECT id FROM workflow.suggestions");

        var before = await Client.GetFromJsonAsync<JsonElement>(new Uri($"/api/plans/{plan}/suggestions", UriKind.Relative), Ct);
        using var seen = await PostAsync($"/api/plans/{plan}/suggestions/{id}/seen");
        using var twice = await PostAsync($"/api/plans/{plan}/suggestions/{id}/seen");
        using var unknown = await PostAsync($"/api/plans/{plan}/suggestions/{id + 100}/seen");
        using var anonymous = await PostAsync($"/api/plans/{plan}/suggestions/{id}/seen", client: Factory.CreateAnonymousClient());
        var after = await Client.GetFromJsonAsync<JsonElement>(new Uri($"/api/plans/{plan}/suggestions", UriKind.Relative), Ct);

        before[0].GetProperty("seen").GetBoolean().ShouldBeFalse();
        seen.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        twice.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        after[0].GetProperty("seen").GetBoolean().ShouldBeTrue();
        (await ScalarAsync<long>("SELECT count(*) FROM workflow.suggestion_seen")).ShouldBe(1);
        (await ScalarAsync<string>("SELECT actor FROM workflow.suggestion_seen")).ShouldBe(OwnerActor);
    }

    [Fact]
    public void Every_write_route_of_the_review_carries_the_decision_rate_limit()
    {
        var endpoints = Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText!.StartsWith("/api/plans", StringComparison.Ordinal) && e.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains("POST"))
            .ToList();

        endpoints.Count.ShouldBe(5);
        endpoints.ShouldAllBe(e => e.Metadata.GetMetadata<EnableRateLimitingAttribute>()!.PolicyName == "plan-decisions");
    }
}
