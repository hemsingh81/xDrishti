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

    private static readonly DateTimeOffset ReviewCutoff = new(2026, 10, 13, 3, 35, 0, TimeSpan.Zero);

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
        new(key, Session, ReviewCutoff, version, new string('c', 64), "cell statistics only", true, "test", DateTimeOffset.UnixEpoch, items.Length == 0 ? [Item(1, TicketGroup.Primary), Item(2, TicketGroup.Reserve), Item(3, TicketGroup.Rejected, allocate: false)] : items);

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
        var approved = await MoveAsync(new MoveTicketCommand(primary, TicketState.Approved, "owner:me", "looks fine", ConfirmReason: "results date unknown"));
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
        Factory.Clock.Pin(ReviewCutoff.AddMinutes(1));

        var result = await WithAsync(p => p.GetRequiredService<ICommandHandler<ApplyReviewCutoffCommand, PlanMoveOutcome>>().HandleAsync(new ApplyReviewCutoffCommand(plan, "system:cutoff"), Ct).AsTask());

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

        Factory.Clock.Pin(ReviewCutoff.AddMinutes(1));

        var result = await WithAsync(p => p.GetRequiredService<ICommandHandler<ApplyReviewCutoffCommand, PlanMoveOutcome>>().HandleAsync(new ApplyReviewCutoffCommand(plan, "system:cutoff"), Ct).AsTask());

        result.Value.ShouldBe(new PlanMoveOutcome(plan, "approved", 1));
        (await ItemsAsync(plan)).Select(i => i.State).ShouldBe([TicketState.Approved, TicketState.Lapsed, TicketState.Proposed]);
    }

    [Fact]
    public async Task The_database_agrees_with_both_transition_tables_on_every_pair()
    {
        var (plan, primary, _, _) = await StoredAsync();
        await using var connection = await OpenAsync();

        // Each pair is tried in its own transaction that is rolled back, so the continuity index never sees two rows; owner decisions come with a matching approval row of the same plan and item.
        async Task<bool> TryAsync(long? item, string from, string to)
        {
            await using var transaction = await connection.BeginTransactionAsync(Ct);
            try
            {
                object approval = DBNull.Value;
                if (item is not null && to is "approved" or "modified" or "rejected")
                {
                    var decision = to == "approved" ? "approve" : to == "modified" ? "modify" : "reject";
                    await using var insertApproval = new NpgsqlCommand($"INSERT INTO workflow.approvals (plan_id, item_id, kind, decision, actor, reason, modifications, cutoff, decided_at) VALUES (@p, @i, 'owner', '{decision}', 'owner:t', 'r', {(decision == "modify" ? "'m'" : "NULL")}, now() + interval '1 hour', now()) RETURNING id", connection, transaction);
                    insertApproval.Parameters.AddWithValue("p", plan);
                    insertApproval.Parameters.AddWithValue("i", item.Value);
                    approval = (await insertApproval.ExecuteScalarAsync(Ct))!;
                }

                await using var command = new NpgsqlCommand("INSERT INTO trading.plan_events (plan_id, item_id, from_state, to_state, actor, reason, approval_id, created_at) VALUES (@p, @i, @f, @t, 'a', 'r', @a, now())", connection, transaction);
                command.Parameters.AddWithValue("p", plan);
                command.Parameters.AddWithValue("i", (object?)item ?? DBNull.Value);
                command.Parameters.AddWithValue("f", from);
                command.Parameters.AddWithValue("t", to);
                command.Parameters.AddWithValue("a", approval);
                await command.ExecuteNonQueryAsync(Ct);
                return true;
            }
            catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.CheckViolation)
            {
                return false;
            }
            catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                return true; // the checks passed; the published row of this plan already left draft
            }
            finally
            {
                await transaction.RollbackAsync(Ct);
            }
        }

        foreach (var from in Enum.GetValues<TicketState>())
        {
            foreach (var to in Enum.GetValues<TicketState>())
            {
                (await TryAsync(primary, from.Code(), to.Code())).ShouldBe(TicketLifecycle.IsLegal(from, to), $"ticket {from} to {to}");
            }
        }

        foreach (var from in Enum.GetValues<PlanState>())
        {
            foreach (var to in Enum.GetValues<PlanState>())
            {
                (await TryAsync(null, from.Code(), to.Code())).ShouldBe(PlanLifecycle.IsLegal(from, to), $"plan {from} to {to}");
            }
        }
    }

    private async Task<string?> StateOfAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = await OpenAsync();
        try
        {
            await ExecuteAsync(connection, sql, parameters);
            return null;
        }
        catch (PostgresException e)
        {
            return e.SqlState;
        }
    }

    [Fact]
    public async Task The_database_itself_refuses_an_owner_decision_event_with_no_approval_and_an_approval_of_another_item_or_plan()
    {
        var (plan, primary, reserve, _) = await StoredAsync();
        var other = await PublishAsync(Plan("plan-other-approval", 2, Item(1, TicketGroup.Primary, allocate: false)));
        var otherItem = (await ItemsAsync(other.Id))[0].Id;
        const string Approval = "INSERT INTO workflow.approvals (plan_id, item_id, kind, decision, actor, reason, cutoff, decided_at) VALUES (@p, @i, 'owner', 'approve', 'owner:t', 'r', now() + interval '1 hour', now()) RETURNING id";
        var approvalOfReserve = await ScalarAsync<long>(Approval, ("p", plan), ("i", reserve));
        const string Event = "INSERT INTO trading.plan_events (plan_id, item_id, from_state, to_state, actor, reason, approval_id, created_at) VALUES (@p, @i, @f, @t, 'a', 'r', @a, now())";

        // No approval on a decision, an approval on a service move, an approval naming another item, and an item that belongs to another plan.
        (await StateOfAsync(Event, ("p", plan), ("i", primary), ("f", "proposed"), ("t", "approved"), ("a", DBNull.Value))).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await StateOfAsync(Event, ("p", plan), ("i", primary), ("f", "proposed"), ("t", "lapsed"), ("a", approvalOfReserve))).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await StateOfAsync(Event, ("p", plan), ("i", primary), ("f", "proposed"), ("t", "approved"), ("a", approvalOfReserve))).ShouldBe(PostgresErrorCodes.ForeignKeyViolation);
        (await StateOfAsync(Event, ("p", plan), ("i", otherItem), ("f", "proposed"), ("t", "lapsed"), ("a", DBNull.Value))).ShouldBe(PostgresErrorCodes.ForeignKeyViolation);
        (await StateOfAsync(Approval, ("p", plan), ("i", otherItem))).ShouldBe(PostgresErrorCodes.ForeignKeyViolation);
        (await StateOfAsync(Event, ("p", plan), ("i", reserve), ("f", "proposed"), ("t", "approved"), ("a", approvalOfReserve))).ShouldBeNull();
        (await StateOfAsync(Event, ("p", plan), ("i", reserve), ("f", "proposed"), ("t", "lapsed"), ("a", DBNull.Value))).ShouldBe(PostgresErrorCodes.UniqueViolation, "a state is left only once");
    }

    [Fact]
    public async Task The_database_refuses_an_approval_that_is_not_the_owners_after_the_cutoff_a_manual_live_allocation_and_malformed_runs()
    {
        var (plan, primary, _, _) = await StoredAsync();
        const string Insert = "INSERT INTO workflow.approvals (plan_id, item_id, kind, decision, actor, reason, cutoff, decided_at) VALUES (@p, @i, @kind, @decision, @actor, 'r', now() + interval '1 hour', @at)";

        async Task<string?> ApprovalAsync(string kind, string decision, string actor, string at = "now()") =>
            await StateOfAsync(Insert.Replace("@at", at, StringComparison.Ordinal), ("p", plan), ("i", primary), ("kind", kind), ("decision", decision), ("actor", actor));

        (await ApprovalAsync("system", "approve", "owner:t")).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await ApprovalAsync("rule", "approve", "owner:t")).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await ApprovalAsync("owner", "approve", "system:nightly")).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await ApprovalAsync("owner", "approve", "cli:xd")).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await ApprovalAsync("owner", "modify", "owner:t")).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await ApprovalAsync("owner", "approve", "owner:t", "now() + interval '2 hours'")).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await ApprovalAsync("owner", "reject", "owner:t", "now() + interval '2 hours'")).ShouldBeNull();
        (await ApprovalAsync("owner", "approve", "owner:t")).ShouldBeNull();
        (await StateOfAsync("INSERT INTO trading.plan_item_allocations (item_id, account_id, mode, quantity) VALUES (@i, '" + Guid.NewGuid() + "', 'paper', 1)", ("i", primary))).ShouldBe(PostgresErrorCodes.ForeignKeyViolation);
        (await StateOfAsync($"INSERT INTO trading.plan_item_allocations (item_id, account_id, mode, quantity) VALUES (@i, '{AccountId}', 'manual_live', 1)", ("i", primary))).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await StateOfAsync("INSERT INTO trading.plan_runs (for_date, outcome, reason, manual, started_at, finished_at) VALUES ('2026-10-13', 'published', 'r', false, now(), now())")).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await StateOfAsync("INSERT INTO trading.plan_runs (for_date, outcome, reason, manual, started_at, finished_at) VALUES ('2026-10-13', 'failed_closed', 'r', false, now(), now())")).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await StateOfAsync("INSERT INTO trading.plan_items (plan_id, item_rank, item_group, symbol, side, strategy_id, strategy_version_hash, trigger_text, stop_text, target_text, validity_text, reasons, cell_key, trades, data_range, p_win_basis, unavailable) VALUES (@p, 9, 'reserve', 's', 'BUY', 'x', 'h', 't', 's', 't', 'v', '{}', 'c', 1, 'd', 'b', '{}')", ("p", plan))).ShouldBe(PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task An_owner_approval_or_modification_at_or_after_the_review_cutoff_is_refused_and_a_rejection_is_not()
    {
        var (_, primary, reserve, _) = await StoredAsync();
        Factory.Clock.Pin(ReviewCutoff);

        var approve = await MoveAsync(new MoveTicketCommand(primary, TicketState.Approved, "owner:me", "late"));
        var modify = await MoveAsync(new MoveTicketCommand(primary, TicketState.Modified, "owner:me", "late", Modifications: "smaller"));
        var reject = await MoveAsync(new MoveTicketCommand(reserve, TicketState.Rejected, "owner:me", "no"));

        approve.Error.Code.ShouldBe(PlanErrors.MoveRefusedCode);
        approve.Error.Message.ShouldContain("cutoff");
        modify.Error.Code.ShouldBe(PlanErrors.MoveRefusedCode);
        reject.IsSuccess.ShouldBeTrue();
        (await ScalarAsync<long>("SELECT count(*) FROM workflow.approvals WHERE decision IN ('approve', 'modify')")).ShouldBe(0);
        Factory.Clock.Pin(ReviewCutoff.AddSeconds(-1));
        (await MoveAsync(new MoveTicketCommand(primary, TicketState.Approved, "owner:me", "in time"))).IsSuccess.ShouldBeTrue();
        (await ScalarAsync<bool>("SELECT cutoff = @c FROM workflow.approvals WHERE decision = 'approve'", ("c", ReviewCutoff))).ShouldBeTrue();
    }

    [Fact]
    public async Task A_plan_is_never_approved_after_its_last_approved_ticket_was_invalidated_even_when_both_race()
    {
        for (var n = 0; n < 8; n++)
        {
            var published = await PublishAsync(Plan($"race-{n}", 10 + n, Item(1, TicketGroup.Primary), Item(2, TicketGroup.Reserve)));
            var items = await ItemsAsync(published.Id);
            await WithAsync(async p =>
            {
                var s = p.GetRequiredService<IPlanStore>();
                await s.MoveItemAsync(new ItemMove(items[0].Id, TicketState.Proposed, TicketState.Approved, "owner:me", "ok", Now, new ApprovalInput(ApprovalDecision.Approve, null, null)), Ct);
                await s.MoveItemAsync(new ItemMove(items[1].Id, TicketState.Proposed, TicketState.Rejected, "owner:me", "no", Now, new ApprovalInput(ApprovalDecision.Reject, null, null)), Ct);
                (await s.MovePlanAsync(new PlanMove(published.Id, PlanState.Published, PlanState.InReview, "system:review", "decided", Now), Ct)).ShouldBe(MoveStatus.Created);
                return 0;
            });

            await Task.WhenAll(
                WithAsync(p => p.GetRequiredService<IPlanStore>().MovePlanAsync(new PlanMove(published.Id, PlanState.InReview, PlanState.Approved, "system:review", "done", Now), Ct).AsTask()),
                WithAsync(p => p.GetRequiredService<IPlanStore>().MoveItemAsync(new ItemMove(items[0].Id, TicketState.Approved, TicketState.Invalidated, "system:preopen", "gap", Now, null), Ct).AsTask()));

            (await ScalarAsync<long>("SELECT count(*) FROM trading.plan_events a JOIN trading.plan_events b ON b.plan_id = a.plan_id AND b.to_state = 'invalidated' AND b.id < a.id WHERE a.plan_id = @p AND a.item_id IS NULL AND a.to_state = 'approved'", ("p", published.Id))).ShouldBe(0, $"round {n}");
        }

    }

    [Fact]
    public async Task The_store_throws_when_an_approval_does_not_match_the_move()
    {
        var (_, primary, _, _) = await StoredAsync();

        async Task Move(TicketState to, ApprovalInput? approval) =>
            await WithAsync(p => p.GetRequiredService<IPlanStore>().MoveItemAsync(new ItemMove(primary, TicketState.Proposed, to, "owner:me", "x", Now, approval), Ct).AsTask());

        await Should.ThrowAsync<ArgumentException>(() => Move(TicketState.Approved, null));
        await Should.ThrowAsync<ArgumentException>(() => Move(TicketState.Lapsed, new ApprovalInput(ApprovalDecision.Approve, null, null)));
        await Should.ThrowAsync<ArgumentException>(() => Move(TicketState.Approved, new ApprovalInput(ApprovalDecision.Reject, null, null)));
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
