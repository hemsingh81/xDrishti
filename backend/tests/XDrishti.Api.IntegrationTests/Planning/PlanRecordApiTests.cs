using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Planning;
using XDrishti.Domain.Common;
using XDrishti.Domain.Planning;

namespace XDrishti.Api.IntegrationTests.Planning;

/// <summary>Spec P7-03 on a real database: a plan is stored once, its state is the newest row of an insert-only trail, concurrent moves have one winner, the owner is the only kind of approval, the cutoff lapses, the database refuses what the tables refuse, and the endpoints read it.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class PlanRecordApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly Guid AccountId = Guid.Parse("0b6f2a9e-7a52-4d7e-9c3a-5d0a7e0b7a01");

    private static readonly DateOnly Session = new(2026, 10, 13);

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await using var connection = await OpenAsync();
        await ExecuteAsync(
            connection,
            """
            INSERT INTO accounts.broker_accounts (id, slug, name, broker, client_id, api_key_issued_on, color, created_at, updated_at, data_enabled, data_primary, data_standby, live_feed_budget)
            VALUES (@id, 'probe-plans', 'Probe', 'Dhan', '1000000077', current_date, '#1A2B3C', now(), now(), false, false, false, 200)
            """,
            ("id", AccountId));
    }

    private static NewPlanItem Item(int rank, TicketGroup group, bool allocate = true) =>
        new(
            rank, group, "NSE:RELIANCE", "BUY", "ORB", new string('a', 64), "close above 15m high", "0.8 ATR below entry", "2R", "09:30-11:30",
            group == TicketGroup.Primary ? [] : ["rank_below_cut"],
            new PlanEvidence("ORB|15m|BUY|intraday|FIXED_RR|*", 41, "about 2.1 years, replayed history, modelled fills, no hold-out", "cell statistics only", 0.52, 0.12, ["results_within_3_days"], null),
            allocate ? [new NewAllocation(AccountId, "paper", 10)] : []);

    private static NewPlan Plan(string key = "plan-2026-10-13-v1", int version = 1, params NewPlanItem[] items) =>
        new(key, Session, version, new string('c', 64), "cell statistics only", true, "test", DateTimeOffset.UnixEpoch, items.Length == 0 ? [Item(1, TicketGroup.Primary), Item(2, TicketGroup.Reserve), Item(3, TicketGroup.Rejected, allocate: false)] : items);

    private async Task<TResult> WithAsync<TResult>(Func<IServiceProvider, Task<TResult>> work)
    {
        using var scope = Factory.Services.CreateScope();
        return await work(scope.ServiceProvider);
    }

    private Task<PlanPublished> PublishAsync(NewPlan plan) => WithAsync(p => p.GetRequiredService<IPlanStore>().PublishAsync(plan, Ct).AsTask());

    private Task<Result<TicketMoveOutcome>> MoveAsync(MoveTicketCommand command) =>
        WithAsync(p => p.GetRequiredService<ICommandHandler<MoveTicketCommand, TicketMoveOutcome>>().HandleAsync(command, Ct).AsTask());

    private Task<IReadOnlyList<PlanItemRow>> ItemsAsync(long planId) => WithAsync(p => p.GetRequiredService<IPlanStore>().ItemsAsync(planId, Ct).AsTask());

    private async Task<(long Plan, long Primary, long Reserve, long Rejected)> StoredAsync()
    {
        var published = await PublishAsync(Plan());
        var items = await ItemsAsync(published.Id);
        return (published.Id, items[0].Id, items[1].Id, items[2].Id);
    }

    [Fact]
    public async Task Before_any_plan_the_list_is_empty_one_is_not_found_and_a_bad_limit_is_refused()
    {
        using var list = await Client.GetAsync(new Uri("/api/plans", UriKind.Relative), Ct);
        using var one = await Client.GetAsync(new Uri("/api/plans/1", UriKind.Relative), Ct);
        using var bad = await Client.GetAsync(new Uri("/api/plans?limit=0", UriKind.Relative), Ct);

        (await list.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetArrayLength().ShouldBe(0);
        one.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        bad.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_published_plan_is_read_with_its_tickets_evidence_sizes_and_the_no_order_notice()
    {
        var (plan, _, _, _) = await StoredAsync();

        var view = await Client.GetFromJsonAsync<JsonElement>(new Uri($"/api/plans/{plan}", UriKind.Relative), Ct);

        view.GetProperty("state").GetString().ShouldBe("published");
        view.GetProperty("notice").GetString().ShouldBe(PlanWording.NoOrder);
        view.GetProperty("provisional").GetBoolean().ShouldBeTrue();
        view.GetProperty("nextStates").EnumerateArray().Select(e => e.GetString()).ShouldBe(["inreview", "approved", "rejected", "lapsed"]);
        var items = view.GetProperty("items").EnumerateArray().ToList();
        items.Select(i => i.GetProperty("group").GetString()).ShouldBe(["primary", "reserve", "rejected"]);
        items[0].GetProperty("state").GetString().ShouldBe("proposed");
        items[0].GetProperty("evidence").GetProperty("trades").GetInt32().ShouldBe(41);
        items[0].GetProperty("evidence").GetProperty("unavailable").EnumerateArray().Select(e => e.GetString()).ShouldBe(["results_within_3_days"]);
        items[0].GetProperty("allocations")[0].GetProperty("mode").GetString().ShouldBe("paper");
        items[2].GetProperty("state").ValueKind.ShouldBe(JsonValueKind.Null);
        items[2].GetProperty("reasons")[0].GetString().ShouldBe("rank_below_cut");
        view.GetProperty("history").GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task The_list_filters_by_date_and_by_account()
    {
        await StoredAsync();
        var other = await PublishAsync(Plan("plan-other", 2, Item(1, TicketGroup.Primary, allocate: false)));

        async Task<JsonElement> GetAsync(string query) => await Client.GetFromJsonAsync<JsonElement>(new Uri("/api/plans" + query, UriKind.Relative), Ct);

        (await GetAsync(string.Empty)).GetArrayLength().ShouldBe(2);
        (await GetAsync("?date=2026-10-13")).GetArrayLength().ShouldBe(2);
        (await GetAsync("?date=2026-10-14")).GetArrayLength().ShouldBe(0);
        var byAccount = await GetAsync($"?account={AccountId}");
        byAccount.GetArrayLength().ShouldBe(1);
        byAccount[0].GetProperty("id").GetInt64().ShouldNotBe(other.Id);
        byAccount[0].GetProperty("primary").GetInt32().ShouldBe(1);
        byAccount[0].GetProperty("rejected").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task The_same_plan_key_published_at_once_stores_one_plan()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => PublishAsync(Plan())));

        results.Count(r => r.Created).ShouldBe(1);
        results.Select(r => r.Id).Distinct().Count().ShouldBe(1);
        (await ScalarAsync<long>("SELECT count(*) FROM trading.plan_items")).ShouldBe(3);
    }

    [Fact]
    public async Task Of_two_concurrent_decisions_from_the_same_state_one_wins_and_the_other_is_stale()
    {
        var (_, primary, _, _) = await StoredAsync();

        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(i =>
            MoveAsync(i % 2 == 0 ? new MoveTicketCommand(primary, TicketState.Approved, "owner:me", "yes") : new MoveTicketCommand(primary, TicketState.Rejected, "owner:me", "no"))));

        results.Count(r => r.IsSuccess).ShouldBe(1);
        results.Where(r => r.IsFailure).ShouldAllBe(r => r.Error.Code == PlanErrors.StaleCode || r.Error.Code == PlanErrors.MoveRefusedCode);
        (await ScalarAsync<long>("SELECT count(*) FROM trading.plan_events WHERE item_id = @i", ("i", primary))).ShouldBe(1);
        (await ScalarAsync<long>("SELECT count(*) FROM workflow.approvals WHERE item_id = @i", ("i", primary))).ShouldBe(1);
    }

    [Fact]
    public async Task The_owner_decision_writes_an_owner_approval_row_that_the_trail_points_at_and_a_service_cannot_approve()
    {
        var (plan, primary, reserve, _) = await StoredAsync();

        var service = await MoveAsync(new MoveTicketCommand(primary, TicketState.Approved, "system:nightly", "auto"));
        var approved = await MoveAsync(new MoveTicketCommand(primary, TicketState.Approved, "owner:me", "looks fine", ConfirmReason: "results date unknown", Cutoff: Now.AddHours(1)));
        var modified = await MoveAsync(new MoveTicketCommand(reserve, TicketState.Modified, "owner:me", "smaller", Modifications: "quantity 10 to 5"));

        service.Error.Code.ShouldBe(PlanErrors.MoveRefusedCode);
        approved.IsSuccess.ShouldBeTrue();
        modified.IsSuccess.ShouldBeTrue();
        (await ScalarAsync<string>("SELECT kind || '/' || decision || '/' || actor FROM workflow.approvals WHERE item_id = @i", ("i", primary))).ShouldBe("owner/approve/owner:me");
        (await ScalarAsync<string>("SELECT modifications FROM workflow.approvals WHERE item_id = @i", ("i", reserve))).ShouldBe("quantity 10 to 5");
        var view = await Client.GetFromJsonAsync<JsonElement>(new Uri($"/api/plans/{plan}", UriKind.Relative), Ct);
        view.GetProperty("items")[0].GetProperty("state").GetString().ShouldBe("approved");
        view.GetProperty("items")[1].GetProperty("state").GetString().ShouldBe("modified");
        view.GetProperty("history").EnumerateArray().Count(h => h.GetProperty("approvalId").ValueKind == JsonValueKind.Number).ShouldBe(2);
    }

    [Fact]
    public async Task A_ticket_follows_the_table_to_closed_and_a_rejected_candidate_never_moves()
    {
        var (plan, primary, _, rejected) = await StoredAsync();

        (await MoveAsync(new MoveTicketCommand(primary, TicketState.Approved, "owner:me", "ok"))).IsSuccess.ShouldBeTrue();
        (await MoveAsync(new MoveTicketCommand(primary, TicketState.Armed, "system:preopen", "gap within limit"))).IsSuccess.ShouldBeTrue();
        (await MoveAsync(new MoveTicketCommand(primary, TicketState.Triggered, "system:live", "trigger met"))).IsSuccess.ShouldBeTrue();
        (await MoveAsync(new MoveTicketCommand(primary, TicketState.Closed, "system:live", "square off"))).IsSuccess.ShouldBeTrue();
        var again = await MoveAsync(new MoveTicketCommand(primary, TicketState.Approved, "owner:me", "again"));
        var candidate = await MoveAsync(new MoveTicketCommand(rejected, TicketState.Approved, "owner:me", "try"));

        again.Error.Code.ShouldBe(PlanErrors.MoveRefusedCode);
        candidate.Error.Code.ShouldBe(PlanErrors.MoveRefusedCode);
        var states = (await WithAsync(p => p.GetRequiredService<IPlanStore>().HistoryAsync(plan, Ct).AsTask())).Select(h => h.To).ToList();
        states.ShouldBe(["published", "approved", "armed", "triggered", "closed"]);
    }

    [Fact]
    public async Task The_store_refuses_a_state_row_for_a_rejected_candidate_even_when_asked_directly()
    {
        var (_, _, _, rejected) = await StoredAsync();

        var status = await WithAsync(p => p.GetRequiredService<IPlanStore>().MoveItemAsync(new ItemMove(rejected, TicketState.Proposed, TicketState.Lapsed, "system:cutoff", "x", Now, null), Ct).AsTask());

        status.ShouldBe(MoveStatus.Stale);
        (await ScalarAsync<long>("SELECT count(*) FROM trading.plan_events WHERE item_id = @i", ("i", rejected))).ShouldBe(0);
    }

    [Fact]
    public async Task The_cutoff_lapses_what_nobody_decided_and_never_approves_and_the_plan_lapses()
    {
        var (plan, primary, reserve, _) = await StoredAsync();
        var cutoff = Now.AddHours(-1);

        var result = await WithAsync(p => p.GetRequiredService<ICommandHandler<ApplyReviewCutoffCommand, PlanMoveOutcome>>().HandleAsync(new ApplyReviewCutoffCommand(plan, cutoff, "system:cutoff"), Ct).AsTask());

        result.Value.ShouldBe(new PlanMoveOutcome(plan, "lapsed", 2));
        (await ScalarAsync<long>("SELECT count(*) FROM trading.plan_events WHERE to_state = 'lapsed' AND item_id IN (@a, @b)", ("a", primary), ("b", reserve))).ShouldBe(2);
        (await ScalarAsync<long>("SELECT count(*) FROM workflow.approvals")).ShouldBe(0);
        (await ScalarAsync<long>("SELECT count(*) FROM trading.plan_events WHERE to_state IN ('approved', 'armed')")).ShouldBe(0);
    }

    [Fact]
    public async Task The_cutoff_keeps_an_owner_approval_and_the_plan_is_approved()
    {
        var (plan, primary, _, _) = await StoredAsync();
        (await MoveAsync(new MoveTicketCommand(primary, TicketState.Approved, "owner:me", "ok"))).IsSuccess.ShouldBeTrue();

        var result = await WithAsync(p => p.GetRequiredService<ICommandHandler<ApplyReviewCutoffCommand, PlanMoveOutcome>>().HandleAsync(new ApplyReviewCutoffCommand(plan, Now.AddHours(-1), "system:cutoff"), Ct).AsTask());

        result.Value.ShouldBe(new PlanMoveOutcome(plan, "approved", 1));
        (await ItemsAsync(plan)).Select(i => i.State).ShouldBe([TicketState.Approved, TicketState.Lapsed, TicketState.Proposed]);
    }

    [Fact]
    public async Task The_database_agrees_with_both_transition_tables_on_every_pair()
    {
        var (plan, primary, _, _) = await StoredAsync();
        await using var connection = await OpenAsync();

        async Task<bool> InsertAsync(long? item, string from, string to)
        {
            await using var command = new NpgsqlCommand("INSERT INTO trading.plan_events (plan_id, item_id, from_state, to_state, actor, reason, created_at) VALUES (@p, @i, @f, @t, 'a', 'r', now())", connection);
            command.Parameters.AddWithValue("p", plan);
            command.Parameters.AddWithValue("i", (object?)item ?? DBNull.Value);
            command.Parameters.AddWithValue("f", from);
            command.Parameters.AddWithValue("t", to);
            try
            {
                await command.ExecuteNonQueryAsync(Ct);
                return true;
            }
            catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.CheckViolation)
            {
                return false;
            }
        }

        foreach (var from in Enum.GetValues<TicketState>())
        {
            foreach (var to in Enum.GetValues<TicketState>())
            {
                (await InsertAsync(primary, from.Code(), to.Code())).ShouldBe(TicketLifecycle.IsLegal(from, to), $"ticket {from} to {to}");
            }
        }

        foreach (var from in Enum.GetValues<PlanState>())
        {
            foreach (var to in Enum.GetValues<PlanState>())
            {
                (await InsertAsync(null, from.Code(), to.Code())).ShouldBe(PlanLifecycle.IsLegal(from, to), $"plan {from} to {to}");
            }
        }
    }

    [Fact]
    public async Task The_database_refuses_an_approval_that_is_not_the_owners_a_manual_live_allocation_and_malformed_runs()
    {
        var (plan, primary, _, _) = await StoredAsync();
        await using var connection = await OpenAsync();

        async Task<string?> StateAsync(string sql)
        {
            try
            {
                await ExecuteAsync(connection, sql, ("p", plan), ("i", primary));
                return null;
            }
            catch (PostgresException e)
            {
                return e.SqlState;
            }
        }

        (await StateAsync("INSERT INTO workflow.approvals (plan_id, item_id, kind, decision, actor, reason, decided_at) VALUES (@p, @i, 'system', 'approve', 'a', 'r', now())")).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await StateAsync("INSERT INTO workflow.approvals (plan_id, item_id, kind, decision, actor, reason, decided_at) VALUES (@p, @i, 'rule', 'approve', 'a', 'r', now())")).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await StateAsync("INSERT INTO workflow.approvals (plan_id, item_id, kind, decision, actor, reason, decided_at) VALUES (@p, @i, 'owner', 'modify', 'a', 'r', now())")).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await StateAsync("INSERT INTO workflow.approvals (plan_id, item_id, kind, decision, actor, reason, decided_at) VALUES (@p, @i, 'owner', 'approve', 'a', 'r', now())")).ShouldBeNull();
        (await StateAsync("INSERT INTO trading.plan_item_allocations (item_id, account_id, mode, quantity) VALUES (@i, '" + Guid.NewGuid() + "', 'paper', 1)")).ShouldBe(PostgresErrorCodes.ForeignKeyViolation);
        (await StateAsync($"INSERT INTO trading.plan_item_allocations (item_id, account_id, mode, quantity) VALUES (@i, '{AccountId}', 'manual_live', 1)")).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await StateAsync("INSERT INTO trading.plan_runs (for_date, outcome, reason, manual, started_at, finished_at) VALUES ('2026-10-13', 'published', 'r', false, now(), now())")).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await StateAsync("INSERT INTO trading.plan_runs (for_date, outcome, reason, manual, started_at, finished_at) VALUES ('2026-10-13', 'failed_closed', 'r', false, now(), now())")).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await StateAsync("INSERT INTO trading.plan_items (plan_id, item_rank, item_group, symbol, side, strategy_id, strategy_version_hash, trigger_text, stop_text, target_text, validity_text, reasons, cell_key, trades, data_range, p_win_basis, unavailable) VALUES (@p, 9, 'reserve', 's', 'BUY', 'x', 'h', 't', 's', 't', 'v', '{}', 'c', 1, 'd', 'b', '{}')")).ShouldBe(PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task Pipeline_attempts_are_recorded_published_failed_closed_and_dry_run()
    {
        var (plan, _, _, _) = await StoredAsync();
        var at = new DateTimeOffset(2026, 10, 12, 13, 0, 0, TimeSpan.Zero);

        await WithAsync(async p =>
        {
            var store = p.GetRequiredService<IPlanStore>();
            await store.RecordRunAsync(new NewPlanRun(Session, "plan-2026-10-13-v1", "published", null, "all gates passed", plan, false, at, at.AddMinutes(3)), Ct);
            await store.RecordRunAsync(new NewPlanRun(Session, null, "failed_closed", "eod_incomplete", "EOD run did not finish", null, false, at, at.AddMinutes(1)), Ct);
            await store.RecordRunAsync(new NewPlanRun(Session, null, "dry_run", null, "dry run", null, true, at, at.AddMinutes(1)), Ct);
            return 0;
        });

        (await ScalarAsync<long>("SELECT count(*) FROM trading.plan_runs")).ShouldBe(3);
        (await ScalarAsync<string>("SELECT failed_gate FROM trading.plan_runs WHERE outcome = 'failed_closed'")).ShouldBe("eod_incomplete");
    }
}
