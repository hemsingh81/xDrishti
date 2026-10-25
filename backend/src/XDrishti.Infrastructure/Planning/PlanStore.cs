using System.Data.Common;
using System.Globalization;
using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Autonomy;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Domain.Autonomy;
using XDrishti.Domain.Planning;

namespace XDrishti.Infrastructure.Planning;

/// <summary>
/// The plan record behind <see cref="IPlanStore"/> (spec P7-03, ADR 0083): raw SQL, insert and select only (the runtime role has no more). A state change is a new row in <c>trading.plan_events</c>, written under a transaction advisory lock
/// per plan or item after checking that the newest state is still the one the caller saw. Nothing here talks to a broker.
/// </summary>
internal sealed class PlanStore(NpgsqlDataSource dataSource, TimeProvider time) : IPlanStore, IAutoApprovalStore, IArmedTicketReader
{
    private const string PlanStateSql = "COALESCE((SELECT e.to_state FROM trading.plan_events e WHERE e.plan_id = p.id AND e.item_id IS NULL ORDER BY e.id DESC LIMIT 1), 'draft')";

    private const string ItemStateSql = "COALESCE((SELECT e.to_state FROM trading.plan_events e WHERE e.item_id = i.id ORDER BY e.id DESC LIMIT 1), 'proposed')";

    private const string PlanColumns = $"""
        p.id, p.plan_key, p.for_date, p.version, p.input_hash, p.basis, p.provisional, p.notice, p.limits, p.created_by, p.created_at, p.review_cutoff, {PlanStateSql},
        (SELECT count(*) FROM trading.plan_items x WHERE x.plan_id = p.id AND x.item_group = 'primary'),
        (SELECT count(*) FROM trading.plan_items x WHERE x.plan_id = p.id AND x.item_group = 'reserve'),
        (SELECT count(*) FROM trading.plan_items x WHERE x.plan_id = p.id AND x.item_group = 'rejected')
        """;

    private const string ItemColumns = $"""
        i.id, i.plan_id, i.item_rank, i.item_group, i.symbol, i.side, i.strategy_id, i.strategy_version_hash, i.trigger_text, i.stop_text, i.target_text, i.validity_text, i.reasons,
        i.cell_key, i.trades, i.data_range, i.p_win_basis, i.p_win, i.conservative_expectancy_r, i.unavailable, i.advisory_note, {ItemStateSql}, i.p_win_lower,
        i.entry_ref_price, i.stop_level, i.stop_anchor, i.target_level, i.reward_risk, i.levels_session
        """;

    public ValueTask<PlanPublished> PublishAsync(NewPlan plan, CancellationToken cancellationToken) => PublishCoreAsync(plan, null, cancellationToken);

    public ValueTask<PlanPublished> PublishWithRunAsync(NewPlan plan, PublishRun run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        return PublishCoreAsync(plan, run, cancellationToken);
    }

    private async ValueTask<PlanPublished> PublishCoreAsync(NewPlan plan, PublishRun? run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await LockAsync(connection, transaction, "trading.plans.publish:" + plan.PlanKey, cancellationToken);

        await using (var existing = new NpgsqlCommand("SELECT id FROM trading.plans WHERE plan_key = @k", connection, transaction))
        {
            existing.Parameters.AddWithValue("k", NpgsqlDbType.Varchar, plan.PlanKey);
            if (await existing.ExecuteScalarAsync(cancellationToken) is long known)
            {
                // A plan with this key is stored. With a run to record: if no published run names it, record it now, in this transaction.
                var recorded = false;
                if (run is not null)
                {
                    await using var has = new NpgsqlCommand("SELECT 1 FROM trading.plan_runs WHERE plan_id = @p AND outcome = 'published'", connection, transaction);
                    has.Parameters.AddWithValue("p", NpgsqlDbType.Bigint, known);
                    if (await has.ExecuteScalarAsync(cancellationToken) is null)
                    {
                        await InsertPublishedRunAsync(connection, transaction, plan, known, run, "A plan with this key was already stored; its run is recorded now. " + run.Reason, cancellationToken);
                        recorded = true;
                    }
                }

                await transaction.CommitAsync(cancellationToken);
                return new PlanPublished(known, false, recorded);
            }
        }

        long planId;
        await using (var insert = new NpgsqlCommand("""
            INSERT INTO trading.plans (plan_key, for_date, review_cutoff, version, input_hash, basis, provisional, notice, limits, created_by, created_at)
            VALUES (@key, @date, @cutoff, @version, @hash, @basis, @prov, @notice, @limits, @by, @at) RETURNING id
            """, connection, transaction))
        {
            var p = insert.Parameters;
            p.AddWithValue("key", NpgsqlDbType.Varchar, plan.PlanKey);
            p.AddWithValue("date", NpgsqlDbType.Date, plan.ForDate);
            p.AddWithValue("cutoff", NpgsqlDbType.TimestampTz, plan.ReviewCutoff);
            p.AddWithValue("version", NpgsqlDbType.Integer, plan.Version);
            p.AddWithValue("hash", NpgsqlDbType.Varchar, plan.InputHash);
            p.AddWithValue("basis", NpgsqlDbType.Varchar, plan.Basis);
            p.AddWithValue("prov", NpgsqlDbType.Boolean, plan.Provisional);
            p.AddWithValue("notice", NpgsqlDbType.Varchar, plan.Notice);
            p.AddWithValue("limits", NpgsqlDbType.Varchar, plan.Limits);
            p.AddWithValue("by", NpgsqlDbType.Varchar, plan.CreatedBy);
            p.AddWithValue("at", NpgsqlDbType.TimestampTz, plan.CreatedAt);
            planId = (long)(await insert.ExecuteScalarAsync(cancellationToken))!;
        }

        foreach (var item in plan.Items)
        {
            await InsertItemAsync(connection, transaction, planId, item, cancellationToken);
        }

        await using (var published = new NpgsqlCommand("""
            INSERT INTO trading.plan_events (plan_id, item_id, from_state, to_state, actor, reason, approval_id, created_at)
            VALUES (@plan, NULL, 'draft', 'published', @by, 'Published.', NULL, @at)
            """, connection, transaction))
        {
            published.Parameters.AddWithValue("plan", NpgsqlDbType.Bigint, planId);
            published.Parameters.AddWithValue("by", NpgsqlDbType.Varchar, plan.CreatedBy);
            published.Parameters.AddWithValue("at", NpgsqlDbType.TimestampTz, plan.CreatedAt);
            await published.ExecuteNonQueryAsync(cancellationToken);
        }

        if (run is not null)
        {
            await InsertPublishedRunAsync(connection, transaction, plan, planId, run, run.Reason, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new PlanPublished(planId, true, run is not null);
    }

    private static async Task InsertPublishedRunAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, NewPlan plan, long planId, PublishRun run, string reason, CancellationToken cancellationToken)
    {
        await using var insert = new NpgsqlCommand("""
            INSERT INTO trading.plan_runs (for_date, plan_key, outcome, failed_gate, reason, plan_id, manual, started_at, finished_at)
            VALUES (@date, @key, 'published', NULL, @reason, @plan, @manual, @start, @end)
            """, connection, transaction);
        var p = insert.Parameters;
        p.AddWithValue("date", NpgsqlDbType.Date, plan.ForDate);
        p.AddWithValue("key", NpgsqlDbType.Varchar, plan.PlanKey);
        p.AddWithValue("reason", NpgsqlDbType.Varchar, reason.Length <= 500 ? reason : reason[..499] + "~");
        p.AddWithValue("plan", NpgsqlDbType.Bigint, planId);
        p.AddWithValue("manual", NpgsqlDbType.Boolean, run.Manual);
        p.AddWithValue("start", NpgsqlDbType.TimestampTz, run.StartedAt);
        p.AddWithValue("end", NpgsqlDbType.TimestampTz, run.FinishedAt);
        await insert.ExecuteNonQueryAsync(cancellationToken);
    }

    public async ValueTask<PlanRow?> FindAsync(long planId, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand($"SELECT {PlanColumns} FROM trading.plans p WHERE p.id = @id");
        select.Parameters.AddWithValue("id", NpgsqlDbType.Bigint, planId);
        var rows = await ReadPlansAsync(select, cancellationToken);
        return rows.Count == 0 ? null : rows[0];
    }

    public async ValueTask<IReadOnlyList<PlanRow>> ListAsync(DateOnly? date, Guid? accountId, int limit, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand($"""
            SELECT {PlanColumns} FROM trading.plans p
            WHERE (@d::date IS NULL OR p.for_date = @d::date)
              AND (@a::uuid IS NULL OR EXISTS (SELECT 1 FROM trading.plan_item_allocations al JOIN trading.plan_items it ON it.id = al.item_id WHERE it.plan_id = p.id AND al.account_id = @a::uuid))
            ORDER BY p.for_date DESC, p.version DESC, p.id DESC LIMIT @limit
            """);
        select.Parameters.Add(new NpgsqlParameter("d", NpgsqlDbType.Date) { Value = date is { } d ? d : DBNull.Value });
        select.Parameters.Add(new NpgsqlParameter("a", NpgsqlDbType.Uuid) { Value = accountId is { } a ? a : DBNull.Value });
        select.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, limit);
        return await ReadPlansAsync(select, cancellationToken);
    }

    public async ValueTask<IReadOnlyList<PlanItemRow>> ItemsAsync(long planId, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand($"SELECT {ItemColumns} FROM trading.plan_items i WHERE i.plan_id = @id ORDER BY i.item_rank");
        select.Parameters.AddWithValue("id", NpgsqlDbType.Bigint, planId);
        return await ReadItemsAsync(select, cancellationToken);
    }

    public async ValueTask<PlanItemRow?> FindItemAsync(long itemId, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand($"SELECT {ItemColumns} FROM trading.plan_items i WHERE i.id = @id");
        select.Parameters.AddWithValue("id", NpgsqlDbType.Bigint, itemId);
        var rows = await ReadItemsAsync(select, cancellationToken);
        return rows.Count == 0 ? null : rows[0];
    }

    public async ValueTask<IReadOnlyList<PlanEventRow>> HistoryAsync(long planId, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand("SELECT id, item_id, from_state, to_state, actor, reason, approval_id, created_at FROM trading.plan_events WHERE plan_id = @id ORDER BY id");
        select.Parameters.AddWithValue("id", NpgsqlDbType.Bigint, planId);
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        var rows = new List<PlanEventRow>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new PlanEventRow(reader.GetInt64(0), Whole(reader, 1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), Whole(reader, 6), reader.GetFieldValue<DateTimeOffset>(7)));
        }

        return rows;
    }

    public async ValueTask<IReadOnlyList<ApprovalRow>> ApprovalsAsync(long planId, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand("SELECT id, item_id, kind, decision, actor, reason, confirm_reason, modifications, decided_at FROM workflow.approvals WHERE plan_id = @id AND item_id IS NOT NULL ORDER BY id");
        select.Parameters.AddWithValue("id", NpgsqlDbType.Bigint, planId);
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        var rows = new List<ApprovalRow>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new ApprovalRow(reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), Text(reader, 6), Text(reader, 7), reader.GetFieldValue<DateTimeOffset>(8)));
        }

        return rows;
    }

    public async ValueTask<IReadOnlyList<ArmedTicket>> ListAsync(DateOnly forDate, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand($"""
            SELECT i.id, i.plan_id, i.item_rank, i.symbol FROM trading.plan_items i JOIN trading.plans p ON p.id = i.plan_id
            WHERE p.for_date = @d AND i.item_group <> 'rejected' AND {ItemStateSql} = 'armed'
            ORDER BY i.item_rank, i.id
            """);
        select.Parameters.AddWithValue("d", NpgsqlDbType.Date, forDate);
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        var rows = new List<ArmedTicket>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new ArmedTicket(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt32(2), reader.GetString(3)));
        }

        return rows;
    }

    public async ValueTask<MoveStatus> MoveItemAsync(ItemMove move, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(move);

        // The owner decides, the services do the rest: the approval is present exactly for the three owner decisions and says the same thing as the move.
        if (TicketLifecycle.IsOwnerDecision(move.To) != (move.Approval is not null))
        {
            throw new ArgumentException("An approval goes with an owner decision (approved, modified, rejected) and with no other move.", nameof(move));
        }

        if (move.Approval is { } given && given.Decision.Target() != move.To)
        {
            throw new ArgumentException("The approval's decision must be the state moved to.", nameof(move));
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        long planId;
        await using (var owner = new NpgsqlCommand("SELECT plan_id FROM trading.plan_items WHERE id = @id", connection, transaction))
        {
            owner.Parameters.AddWithValue("id", NpgsqlDbType.Bigint, move.ItemId);
            if (await owner.ExecuteScalarAsync(cancellationToken) is not long found)
            {
                return MoveStatus.NotFound;
            }

            planId = found;
        }

        // Always the plan lock first, then the item lock: a ticket move and a plan move on the same plan never interleave.
        await LockAsync(connection, transaction, "trading.plans:" + planId.ToString(CultureInfo.InvariantCulture), cancellationToken);
        await LockAsync(connection, transaction, "trading.plan_items:" + move.ItemId.ToString(CultureInfo.InvariantCulture), cancellationToken);

        // The time is read after the locks, so a decision that waited for a lock is judged at the time it can be written.
        var now = time.GetUtcNow();
        DateTimeOffset cutoff;
        await using (var state = new NpgsqlCommand($"SELECT i.item_group, {ItemStateSql}, (SELECT {PlanStateSql} FROM trading.plans p WHERE p.id = i.plan_id), (SELECT p.review_cutoff FROM trading.plans p WHERE p.id = i.plan_id) FROM trading.plan_items i WHERE i.id = @id", connection, transaction))
        {
            state.Parameters.AddWithValue("id", NpgsqlDbType.Bigint, move.ItemId);
            await using var reader = await state.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);

            // A rejected candidate is not a ticket: it never gets a state row. A state other than the one the caller saw means another move won.
            if (reader.GetString(0) == TicketGroup.Rejected.Code() || reader.GetString(1) != move.From.Code())
            {
                return MoveStatus.Stale;
            }

            cutoff = reader.GetFieldValue<DateTimeOffset>(3);
            if (TicketLifecycle.IsOwnerDecision(move.To))
            {
                // A decision is taken while the plan is open for review, and approving or modifying only before the cutoff of the plan's session.
                if (reader.GetString(2) is not ("published" or "inreview"))
                {
                    return MoveStatus.Refused;
                }

                if (move.To != TicketState.Rejected && now >= cutoff)
                {
                    return MoveStatus.AfterCutoff;
                }
            }
        }

        long? approvalId = null;
        if (move.Approval is { } approval)
        {
            await using var insertApproval = new NpgsqlCommand("""
                INSERT INTO workflow.approvals (plan_id, item_id, kind, decision, actor, reason, confirm_reason, modifications, cutoff, decided_at)
                VALUES (@plan, @item, 'owner', @decision, @actor, @reason, @confirm, @mods, @cutoff, clock_timestamp()) RETURNING id
                """, connection, transaction);
            var p = insertApproval.Parameters;
            p.AddWithValue("plan", NpgsqlDbType.Bigint, planId);
            p.AddWithValue("item", NpgsqlDbType.Bigint, move.ItemId);
            p.AddWithValue("decision", NpgsqlDbType.Varchar, approval.Decision.Code());
            p.AddWithValue("actor", NpgsqlDbType.Varchar, move.Actor);
            p.AddWithValue("reason", NpgsqlDbType.Varchar, move.Reason);
            p.Add(new NpgsqlParameter("confirm", NpgsqlDbType.Varchar) { Value = (object?)approval.ConfirmReason ?? DBNull.Value });
            p.Add(new NpgsqlParameter("mods", NpgsqlDbType.Varchar) { Value = (object?)approval.Modifications ?? DBNull.Value });
            p.AddWithValue("cutoff", NpgsqlDbType.TimestampTz, cutoff);
            try
            {
                approvalId = (long)(await insertApproval.ExecuteScalarAsync(cancellationToken))!;
            }
            catch (PostgresException failure) when (failure.SqlState == PostgresErrorCodes.CheckViolation)
            {
                // The database stamps the decision with its own clock and refuses an approval or modification at or after the plan's cutoff, also when the application clock read just before it.
                return MoveStatus.AfterCutoff;
            }
        }

        await InsertEventAsync(connection, transaction, planId, move.ItemId, move.From.Code(), move.To.Code(), move.Actor, move.Reason, approvalId, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return MoveStatus.Created;
    }

    public async ValueTask<MoveStatus> AutoApproveAsync(AutoApproveMove move, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(move);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        long planId;
        await using (var owner = new NpgsqlCommand("SELECT plan_id FROM trading.plan_items WHERE id = @id", connection, transaction))
        {
            owner.Parameters.AddWithValue("id", NpgsqlDbType.Bigint, move.ItemId);
            if (await owner.ExecuteScalarAsync(cancellationToken) is not long found)
            {
                return MoveStatus.NotFound;
            }

            planId = found;
        }

        // The same order of locks as an owner decision (plan, then item), so of an automatic approval and a manual decision on one ticket exactly one is written; the other sees a different state (stale).
        await LockAsync(connection, transaction, "trading.plans:" + planId.ToString(CultureInfo.InvariantCulture), cancellationToken);
        await LockAsync(connection, transaction, "trading.plan_items:" + move.ItemId.ToString(CultureInfo.InvariantCulture), cancellationToken);

        var now = time.GetUtcNow();
        DateTimeOffset cutoff;
        await using (var state = new NpgsqlCommand($"SELECT i.item_group, {ItemStateSql}, (SELECT {PlanStateSql} FROM trading.plans p WHERE p.id = i.plan_id), (SELECT p.review_cutoff FROM trading.plans p WHERE p.id = i.plan_id) FROM trading.plan_items i WHERE i.id = @id", connection, transaction))
        {
            state.Parameters.AddWithValue("id", NpgsqlDbType.Bigint, move.ItemId);
            await using var reader = await state.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            if (reader.GetString(0) != TicketGroup.Primary.Code())
            {
                return MoveStatus.Refused;
            }

            if (reader.GetString(1) != TicketState.Proposed.Code())
            {
                return MoveStatus.Stale;
            }

            if (reader.GetString(2) is not ("published" or "inreview"))
            {
                return MoveStatus.Refused;
            }

            cutoff = reader.GetFieldValue<DateTimeOffset>(3);
            if (now >= cutoff)
            {
                return MoveStatus.AfterCutoff;
            }
        }

        long approvalId;
        try
        {
            await using var insertApproval = new NpgsqlCommand("""
                INSERT INTO workflow.approvals (plan_id, item_id, kind, decision, actor, reason, cutoff, decided_at, settings_id, settings_hash, policy_version)
                VALUES (@plan, @item, 'auto', 'approve', @actor, @reason, @cutoff, clock_timestamp(), @sid, @shash, @policy) RETURNING id
                """, connection, transaction);
            var p = insertApproval.Parameters;
            p.AddWithValue("plan", NpgsqlDbType.Bigint, planId);
            p.AddWithValue("item", NpgsqlDbType.Bigint, move.ItemId);
            p.AddWithValue("actor", NpgsqlDbType.Varchar, AutoApprovalWording.Actor);
            p.AddWithValue("reason", NpgsqlDbType.Varchar, move.Reason.Length <= 500 ? move.Reason : move.Reason[..500]);
            p.AddWithValue("cutoff", NpgsqlDbType.TimestampTz, cutoff);
            p.AddWithValue("sid", NpgsqlDbType.Bigint, move.SettingsId);
            p.AddWithValue("shash", NpgsqlDbType.Varchar, move.SettingsHash);
            p.AddWithValue("policy", NpgsqlDbType.Varchar, move.PolicyVersion);
            approvalId = (long)(await insertApproval.ExecuteScalarAsync(cancellationToken))!;
        }
        catch (PostgresException failure) when (failure.SqlState == PostgresErrorCodes.CheckViolation)
        {
            // The shape check holds the cutoff (the database clock) among other things; the trigger holds the switch, the group, the item and plan state and the cap. Only a cutoff that the database clock says has passed is reported as AfterCutoff.
            if (failure.ConstraintName == "ck_approvals_shape")
            {
                await using var clock = dataSource.CreateCommand("SELECT clock_timestamp() >= @cutoff");
                clock.Parameters.AddWithValue("cutoff", NpgsqlDbType.TimestampTz, cutoff);
                return (bool)(await clock.ExecuteScalarAsync(cancellationToken))! ? MoveStatus.AfterCutoff : MoveStatus.Refused;
            }

            return MoveStatus.Refused;
        }
        catch (PostgresException failure) when (failure.SqlState == PostgresErrorCodes.ForeignKeyViolation)
        {
            return MoveStatus.Refused;
        }

        await InsertEventAsync(connection, transaction, planId, move.ItemId, TicketState.Proposed.Code(), TicketState.Approved.Code(), AutoApprovalWording.Actor, move.Reason.Length <= 500 ? move.Reason : move.Reason[..500], approvalId, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return MoveStatus.Created;
    }

    public async ValueTask<int> CountAutoApprovalsAsync(DateOnly forDate, CancellationToken cancellationToken)
    {
        await using var count = dataSource.CreateCommand("SELECT count(*) FROM workflow.approvals a JOIN trading.plans p ON p.id = a.plan_id WHERE a.kind = 'auto' AND p.for_date = @date");
        count.Parameters.AddWithValue("date", NpgsqlDbType.Date, forDate);
        return (int)(long)(await count.ExecuteScalarAsync(cancellationToken))!;
    }

    public async ValueTask<MoveStatus> MovePlanAsync(PlanMove move, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(move);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await LockAsync(connection, transaction, "trading.plans:" + move.PlanId.ToString(CultureInfo.InvariantCulture), cancellationToken);

        await using (var plan = new NpgsqlCommand($"SELECT {PlanStateSql} FROM trading.plans p WHERE p.id = @id", connection, transaction))
        {
            plan.Parameters.AddWithValue("id", NpgsqlDbType.Bigint, move.PlanId);
            if (await plan.ExecuteScalarAsync(cancellationToken) is not string state)
            {
                return MoveStatus.NotFound;
            }

            if (state != move.From.Code())
            {
                return MoveStatus.Stale;
            }
        }

        // The facts are recomputed here, under the plan lock, from the newest event of each ticket (ticket moves take the same lock first): a plan is never approved or armed without a live approved or armed ticket.
        PlanFacts facts;
        await using (var count = new NpgsqlCommand($"""
            SELECT count(*) FILTER (WHERE st IN ('proposed', 'modified')), count(*) FILTER (WHERE st = 'approved'), count(*) FILTER (WHERE st = 'armed'), count(*) FILTER (WHERE st = 'rejected')
            FROM (SELECT {ItemStateSql} AS st FROM trading.plan_items i WHERE i.plan_id = @id AND i.item_group <> 'rejected') x
            """, connection, transaction))
        {
            count.Parameters.AddWithValue("id", NpgsqlDbType.Bigint, move.PlanId);
            await using var reader = await count.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            facts = new PlanFacts((int)reader.GetInt64(0), (int)reader.GetInt64(1), (int)reader.GetInt64(2), (int)reader.GetInt64(3));
        }

        if (PlanLifecycle.Refusals(move.From, move.To, facts).Count > 0)
        {
            return MoveStatus.Refused;
        }

        await InsertEventAsync(connection, transaction, move.PlanId, null, move.From.Code(), move.To.Code(), move.Actor, move.Reason, null, time.GetUtcNow(), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return MoveStatus.Created;
    }

    public async ValueTask RecordRunAsync(NewPlanRun run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        await using var insert = dataSource.CreateCommand("""
            INSERT INTO trading.plan_runs (for_date, plan_key, outcome, failed_gate, reason, plan_id, manual, started_at, finished_at)
            VALUES (@date, @key, @outcome, @gate, @reason, @plan, @manual, @start, @end)
            """);
        var p = insert.Parameters;
        p.AddWithValue("date", NpgsqlDbType.Date, run.ForDate);
        p.Add(new NpgsqlParameter("key", NpgsqlDbType.Varchar) { Value = (object?)run.PlanKey ?? DBNull.Value });
        p.AddWithValue("outcome", NpgsqlDbType.Varchar, run.Outcome);
        p.Add(new NpgsqlParameter("gate", NpgsqlDbType.Varchar) { Value = (object?)run.FailedGate ?? DBNull.Value });
        p.AddWithValue("reason", NpgsqlDbType.Varchar, run.Reason);
        p.Add(new NpgsqlParameter("plan", NpgsqlDbType.Bigint) { Value = run.PlanId is { } id ? id : DBNull.Value });
        p.AddWithValue("manual", NpgsqlDbType.Boolean, run.Manual);
        p.AddWithValue("start", NpgsqlDbType.TimestampTz, run.StartedAt);
        p.AddWithValue("end", NpgsqlDbType.TimestampTz, run.FinishedAt);
        await insert.ExecuteNonQueryAsync(cancellationToken);
    }

    // Shortcut: the attempt number is row_number() over all rows of the date, computed over the whole table before the filter (one row per night, so small); filter inside the subquery if the table ever grows large.
    public async ValueTask<IReadOnlyList<PlanRunRow>> ListRunsAsync(DateOnly? forDate, int limit, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand("""
            SELECT id, attempt, for_date, plan_key, outcome, failed_gate, reason, plan_id, manual, started_at, finished_at FROM (
                SELECT r.*, row_number() OVER (PARTITION BY for_date ORDER BY id) AS attempt FROM trading.plan_runs r
            ) x WHERE (@date::date IS NULL OR for_date = @date) ORDER BY for_date DESC, id DESC LIMIT @limit
            """);
        select.Parameters.Add(new NpgsqlParameter("date", NpgsqlDbType.Date) { Value = forDate is { } d ? d : DBNull.Value });
        select.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, limit);
        var rows = new List<PlanRunRow>();
        await using var r = await select.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken))
        {
            rows.Add(new PlanRunRow(
                r.GetInt64(0),
                (int)r.GetInt64(1),
                new NewPlanRun(r.GetFieldValue<DateOnly>(2), Text(r, 3), r.GetString(4), Text(r, 5), r.GetString(6), Whole(r, 7), r.GetBoolean(8), r.GetFieldValue<DateTimeOffset>(9), r.GetFieldValue<DateTimeOffset>(10))));
        }

        return rows;
    }

    // The lock keys are 32-bit hashtext values: two different keys can collide and then wait for each other, which is harmless (only slower); the continuity indexes and checks of the migration do not rely on the lock.
    private static async Task LockAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string key, CancellationToken cancellationToken)
    {
        await using var advisory = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtext(@k))", connection, transaction);
        advisory.Parameters.AddWithValue("k", NpgsqlDbType.Text, key);
        await advisory.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertEventAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, long planId, long? itemId, string from, string to, string actor, string reason, long? approvalId, DateTimeOffset at, CancellationToken cancellationToken)
    {
        // The previous event of the same plan or item (null for the first): the database checks that this event continues from its to_state.
        object previous = DBNull.Value;
        await using (var last = new NpgsqlCommand("SELECT id FROM trading.plan_events WHERE plan_id = @plan AND item_id IS NOT DISTINCT FROM @item ORDER BY id DESC LIMIT 1", connection, transaction))
        {
            last.Parameters.AddWithValue("plan", NpgsqlDbType.Bigint, planId);
            last.Parameters.Add(new NpgsqlParameter("item", NpgsqlDbType.Bigint) { Value = itemId is { } li ? li : DBNull.Value });
            if (await last.ExecuteScalarAsync(cancellationToken) is long found)
            {
                previous = found;
            }
        }

        await using var insert = new NpgsqlCommand("""
            INSERT INTO trading.plan_events (plan_id, item_id, from_state, to_state, actor, reason, approval_id, prev_event_id, created_at)
            VALUES (@plan, @item, @from, @to, @actor, @reason, @approval, @prev, @at)
            """, connection, transaction);
        var p = insert.Parameters;
        p.Add(new NpgsqlParameter("prev", NpgsqlDbType.Bigint) { Value = previous });
        p.AddWithValue("plan", NpgsqlDbType.Bigint, planId);
        p.Add(new NpgsqlParameter("item", NpgsqlDbType.Bigint) { Value = itemId is { } i ? i : DBNull.Value });
        p.AddWithValue("from", NpgsqlDbType.Varchar, from);
        p.AddWithValue("to", NpgsqlDbType.Varchar, to);
        p.AddWithValue("actor", NpgsqlDbType.Varchar, actor);
        p.AddWithValue("reason", NpgsqlDbType.Varchar, reason);
        p.Add(new NpgsqlParameter("approval", NpgsqlDbType.Bigint) { Value = approvalId is { } a ? a : DBNull.Value });
        p.AddWithValue("at", NpgsqlDbType.TimestampTz, at);
        await insert.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertItemAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, long planId, NewPlanItem item, CancellationToken cancellationToken)
    {
        long itemId;
        await using (var insert = new NpgsqlCommand("""
            INSERT INTO trading.plan_items (plan_id, item_rank, item_group, symbol, side, strategy_id, strategy_version_hash, trigger_text, stop_text, target_text, validity_text, reasons,
                cell_key, trades, data_range, p_win_basis, p_win, conservative_expectancy_r, unavailable, advisory_note, p_win_lower,
                entry_ref_price, stop_level, stop_anchor, target_level, reward_risk, levels_session)
            VALUES (@plan, @rank, @group, @symbol, @side, @sid, @shash, @trigger, @stop, @target, @validity, @reasons, @cell, @trades, @range, @basis, @pwin, @er, @unavail, @note, @pwl, @erp, @stl, @sta, @tgl, @rr, @ls) RETURNING id
            """, connection, transaction))
        {
            var e = item.Evidence;
            var p = insert.Parameters;
            p.AddWithValue("plan", NpgsqlDbType.Bigint, planId);
            p.AddWithValue("rank", NpgsqlDbType.Integer, item.Rank);
            p.AddWithValue("group", NpgsqlDbType.Varchar, item.Group.Code());
            p.AddWithValue("symbol", NpgsqlDbType.Varchar, item.Symbol);
            p.AddWithValue("side", NpgsqlDbType.Varchar, item.Side);
            p.AddWithValue("sid", NpgsqlDbType.Varchar, item.StrategyId);
            p.AddWithValue("shash", NpgsqlDbType.Varchar, item.StrategyVersionHash);
            p.AddWithValue("trigger", NpgsqlDbType.Varchar, item.Trigger);
            p.AddWithValue("stop", NpgsqlDbType.Varchar, item.Stop);
            p.AddWithValue("target", NpgsqlDbType.Varchar, item.Target);
            p.AddWithValue("validity", NpgsqlDbType.Varchar, item.Validity);
            p.AddWithValue("reasons", NpgsqlDbType.Array | NpgsqlDbType.Text, item.Reasons.ToArray());
            p.AddWithValue("cell", NpgsqlDbType.Varchar, e.CellKey);
            p.AddWithValue("trades", NpgsqlDbType.Integer, e.Trades);
            p.AddWithValue("range", NpgsqlDbType.Varchar, e.DataRange);
            p.AddWithValue("basis", NpgsqlDbType.Varchar, e.PWinBasis);
            p.Add(new NpgsqlParameter("pwin", NpgsqlDbType.Double) { Value = e.PWin is { } w ? w : DBNull.Value });
            p.Add(new NpgsqlParameter("pwl", NpgsqlDbType.Double) { Value = e.PWinLower is { } l ? l : DBNull.Value });
            p.Add(new NpgsqlParameter("er", NpgsqlDbType.Double) { Value = e.ConservativeExpectancyR is { } r ? r : DBNull.Value });
            p.AddWithValue("unavail", NpgsqlDbType.Array | NpgsqlDbType.Text, e.Unavailable.ToArray());
            p.Add(new NpgsqlParameter("note", NpgsqlDbType.Varchar) { Value = (object?)e.AdvisoryNote ?? DBNull.Value });
            var lv = item.Levels;
            p.Add(new NpgsqlParameter("erp", NpgsqlDbType.Numeric) { Value = lv is null ? DBNull.Value : (object)lv.EntryRef });
            p.Add(new NpgsqlParameter("stl", NpgsqlDbType.Numeric) { Value = lv?.Stop is { } sv ? (object)sv : DBNull.Value });
            p.Add(new NpgsqlParameter("sta", NpgsqlDbType.Varchar) { Value = lv?.Anchor is { } an ? (object)an.Code() : DBNull.Value });
            p.Add(new NpgsqlParameter("tgl", NpgsqlDbType.Numeric) { Value = lv?.Target is { } tv ? (object)tv : DBNull.Value });
            p.Add(new NpgsqlParameter("rr", NpgsqlDbType.Numeric) { Value = lv?.RewardRisk is { } rv ? (object)rv : DBNull.Value });
            p.Add(new NpgsqlParameter("ls", NpgsqlDbType.Date) { Value = lv is null ? DBNull.Value : (object)lv.Session });
            itemId = (long)(await insert.ExecuteScalarAsync(cancellationToken))!;
        }

        foreach (var allocation in item.Allocations)
        {
            await using var insertAllocation = new NpgsqlCommand("INSERT INTO trading.plan_item_allocations (item_id, account_id, mode, quantity) VALUES (@item, @account, @mode, @qty)", connection, transaction);
            insertAllocation.Parameters.AddWithValue("item", NpgsqlDbType.Bigint, itemId);
            insertAllocation.Parameters.AddWithValue("account", NpgsqlDbType.Uuid, allocation.AccountId);
            insertAllocation.Parameters.AddWithValue("mode", NpgsqlDbType.Varchar, allocation.Mode);
            insertAllocation.Parameters.AddWithValue("qty", NpgsqlDbType.Integer, allocation.Quantity);
            await insertAllocation.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private async Task<IReadOnlyList<PlanItemRow>> ReadItemsAsync(NpgsqlCommand select, CancellationToken cancellationToken)
    {
        var rows = new List<PlanItemRow>();
        await using (var reader = await select.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var evidence = new PlanEvidence(reader.GetString(13), reader.GetInt32(14), reader.GetString(15), reader.GetString(16), Real(reader, 17), Real(reader, 18), reader.GetFieldValue<string[]>(19), Text(reader, 20), Real(reader, 22));
                var item = new NewPlanItem(
                    reader.GetInt32(2), TicketGroupText.TryParse(reader.GetString(3)) ?? TicketGroup.Rejected, reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7),
                    reader.GetString(8), reader.GetString(9), reader.GetString(10), reader.GetString(11), reader.GetFieldValue<string[]>(12), evidence, [], LevelsOf(reader, Text(reader, 5)));
                rows.Add(new PlanItemRow(reader.GetInt64(0), reader.GetInt64(1), item, TicketStateText.TryParse(reader.GetString(21)) ?? TicketState.Proposed));
            }
        }

        // The sizes per account of these items, read after the first reader is closed.
        if (rows.Count == 0)
        {
            return rows;
        }

        await using var allocations = dataSource.CreateCommand("SELECT item_id, account_id, mode, quantity FROM trading.plan_item_allocations WHERE item_id = ANY(@ids) ORDER BY id");
        allocations.Parameters.AddWithValue("ids", NpgsqlDbType.Array | NpgsqlDbType.Bigint, rows.Select(r => r.Id).ToArray());
        var byItem = new Dictionary<long, List<NewAllocation>>();
        await using (var reader = await allocations.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var id = reader.GetInt64(0);
                if (!byItem.TryGetValue(id, out var list))
                {
                    byItem[id] = list = [];
                }

                list.Add(new NewAllocation(reader.GetGuid(1), reader.GetString(2), reader.GetInt32(3)));
            }
        }

        return [.. rows.Select(r => byItem.TryGetValue(r.Id, out var list) ? r with { Item = r.Item with { Allocations = list } } : r)];
    }

    private static async Task<IReadOnlyList<PlanRow>> ReadPlansAsync(NpgsqlCommand select, CancellationToken cancellationToken)
    {
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        var rows = new List<PlanRow>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var plan = new NewPlan(reader.GetString(1), reader.GetFieldValue<DateOnly>(2), reader.GetFieldValue<DateTimeOffset>(11), reader.GetInt32(3), reader.GetString(4), reader.GetString(5), reader.GetBoolean(6), reader.GetString(9), reader.GetFieldValue<DateTimeOffset>(10), [])
            {
                Notice = reader.GetString(7),
                Limits = reader.GetString(8),
            };
            rows.Add(new PlanRow(reader.GetInt64(0), plan, PlanStateText.TryParse(reader.GetString(12)) ?? PlanState.Draft, (int)reader.GetInt64(13), (int)reader.GetInt64(14), (int)reader.GetInt64(15)));
        }

        return rows;
    }

    // Columns 23-28; a row stored before P7-19 has none (levels: null). The database check guarantees the combination, so a row that fails TicketLevels.Create (a shortcut: the store has no logger, and a row the check passed but the domain refuses, for example a stop over the 30 % cap inserted by hand, is read as no levels rather than failing the read).
    private static TicketLevels? LevelsOf(DbDataReader r, string? side)
    {
        if (r.IsDBNull(23) || side is null)
        {
            return null;
        }

        var anchor = StopAnchorText.TryParse(Text(r, 25));
        var created = TicketLevels.Create(side, r.GetDecimal(23), r.GetFieldValue<DateOnly>(28), r.IsDBNull(24) ? null : r.GetDecimal(24), anchor, r.IsDBNull(26) ? null : r.GetDecimal(26), r.IsDBNull(27) ? null : r.GetDecimal(27));
        return created.IsSuccess ? created.Value : null;
    }

    private static string? Text(DbDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);

    private static long? Whole(DbDataReader r, int i) => r.IsDBNull(i) ? null : r.GetInt64(i);

    private static double? Real(DbDataReader r, int i) => r.IsDBNull(i) ? null : r.GetDouble(i);
}
