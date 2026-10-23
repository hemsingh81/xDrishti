using System.Data.Common;
using System.Globalization;
using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Domain.Planning;

namespace XDrishti.Infrastructure.Planning;

/// <summary>
/// The plan record behind <see cref="IPlanStore"/> (spec P7-03, ADR 0083): raw SQL, insert and select only (the runtime role has no more). A state change is a new row in <c>trading.plan_events</c>, written under a transaction advisory lock
/// per plan or item after checking that the newest state is still the one the caller saw. Nothing here talks to a broker.
/// </summary>
internal sealed class PlanStore(NpgsqlDataSource dataSource, TimeProvider time) : IPlanStore
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
        i.cell_key, i.trades, i.data_range, i.p_win_basis, i.p_win, i.conservative_expectancy_r, i.unavailable, i.advisory_note, {ItemStateSql}
        """;

    public async ValueTask<PlanPublished> PublishAsync(NewPlan plan, CancellationToken cancellationToken)
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
                await transaction.RollbackAsync(cancellationToken);
                return new PlanPublished(known, false);
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

        await transaction.CommitAsync(cancellationToken);
        return new PlanPublished(planId, true);
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
                cell_key, trades, data_range, p_win_basis, p_win, conservative_expectancy_r, unavailable, advisory_note)
            VALUES (@plan, @rank, @group, @symbol, @side, @sid, @shash, @trigger, @stop, @target, @validity, @reasons, @cell, @trades, @range, @basis, @pwin, @er, @unavail, @note) RETURNING id
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
            p.Add(new NpgsqlParameter("er", NpgsqlDbType.Double) { Value = e.ConservativeExpectancyR is { } r ? r : DBNull.Value });
            p.AddWithValue("unavail", NpgsqlDbType.Array | NpgsqlDbType.Text, e.Unavailable.ToArray());
            p.Add(new NpgsqlParameter("note", NpgsqlDbType.Varchar) { Value = (object?)e.AdvisoryNote ?? DBNull.Value });
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
                var evidence = new PlanEvidence(reader.GetString(13), reader.GetInt32(14), reader.GetString(15), reader.GetString(16), Real(reader, 17), Real(reader, 18), reader.GetFieldValue<string[]>(19), Text(reader, 20));
                var item = new NewPlanItem(
                    reader.GetInt32(2), TicketGroupText.TryParse(reader.GetString(3)) ?? TicketGroup.Rejected, reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7),
                    reader.GetString(8), reader.GetString(9), reader.GetString(10), reader.GetString(11), reader.GetFieldValue<string[]>(12), evidence, []);
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

    private static string? Text(DbDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);

    private static long? Whole(DbDataReader r, int i) => r.IsDBNull(i) ? null : r.GetInt64(i);

    private static double? Real(DbDataReader r, int i) => r.IsDBNull(i) ? null : r.GetDouble(i);
}
