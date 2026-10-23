using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Domain.Planning;

namespace XDrishti.Infrastructure.Planning;

/// <summary>
/// The grades behind <see cref="IGradeStore"/> (spec P7-13, ADR 0090): raw SQL, insert and select only (the runtime role has no more). A ticket has one grade: the second insert for it is skipped by the unique index
/// (<c>ON CONFLICT DO NOTHING</c>), so grading twice stores once. Nothing here talks to a broker.
/// </summary>
internal sealed class GradeStore(NpgsqlDataSource dataSource) : IGradeStore
{
    public async ValueTask<int> InsertAsync(IReadOnlyList<NewGrade> grades, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(grades);
        if (grades.Count == 0)
        {
            return 0;
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var written = 0;
        foreach (var g in grades)
        {
            await using var insert = new NpgsqlCommand("""
                INSERT INTO trading.grades (plan_id, item_id, for_date, status, r, mae_r, mfe_r, exit_reason, entry_price, exit_price, cost_basis, detail, basis, run_key, graded_at)
                VALUES (@plan, @item, @date, @status, @r, @mae, @mfe, @exit, @entry, @out, @cost, @detail, @basis, @run, @at)
                ON CONFLICT (item_id) DO NOTHING
                """, connection, transaction);
            var p = insert.Parameters;
            var t = g.Grade;
            p.AddWithValue("plan", NpgsqlDbType.Bigint, g.PlanId);
            p.AddWithValue("item", NpgsqlDbType.Bigint, g.ItemId);
            p.AddWithValue("date", NpgsqlDbType.Date, g.ForDate);
            p.AddWithValue("status", NpgsqlDbType.Varchar, t.Status.Code());
            p.AddWithValue("r", NpgsqlDbType.Double, (object?)t.R ?? DBNull.Value);
            p.AddWithValue("mae", NpgsqlDbType.Double, (object?)t.MaeR ?? DBNull.Value);
            p.AddWithValue("mfe", NpgsqlDbType.Double, (object?)t.MfeR ?? DBNull.Value);
            p.AddWithValue("exit", NpgsqlDbType.Varchar, (object?)t.ExitReason ?? DBNull.Value);
            p.AddWithValue("entry", NpgsqlDbType.Double, (object?)t.EntryPrice ?? DBNull.Value);
            p.AddWithValue("out", NpgsqlDbType.Double, (object?)t.ExitPrice ?? DBNull.Value);
            p.AddWithValue("cost", NpgsqlDbType.Varchar, Clip(t.CostBasis, 120));
            p.AddWithValue("detail", NpgsqlDbType.Varchar, Clip(t.Detail, 500));
            p.AddWithValue("basis", NpgsqlDbType.Varchar, TicketGrade.Basis);
            p.AddWithValue("run", NpgsqlDbType.Varchar, (object?)g.RunKey ?? DBNull.Value);
            p.AddWithValue("at", NpgsqlDbType.TimestampTz, g.GradedAt);
            written += await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return written;
    }

    public async ValueTask<GradedPage> ReportItemsAsync(DateOnly? from, DateOnly? to, int limit, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand("""
            SELECT p.id, p.for_date, i.item_rank, i.item_group,
                   COALESCE((SELECT e.to_state FROM trading.plan_events e WHERE e.item_id = i.id ORDER BY e.id DESC LIMIT 1), 'proposed'),
                   g.status, g.r, g.mae_r, g.mfe_r, g.exit_reason, g.entry_price, g.exit_price, g.cost_basis, g.detail
            FROM trading.grades g
            JOIN trading.plan_items i ON i.id = g.item_id
            JOIN trading.plans p ON p.id = g.plan_id
            WHERE (@from IS NULL OR p.for_date >= @from) AND (@to IS NULL OR p.for_date <= @to)
              AND p.version = (SELECT max(q.version) FROM trading.plans q WHERE q.for_date = p.for_date)
            ORDER BY p.for_date DESC, p.id, i.item_rank
            LIMIT @limit
            """);
        select.Parameters.AddWithValue("from", NpgsqlDbType.Date, (object?)from ?? DBNull.Value);
        select.Parameters.AddWithValue("to", NpgsqlDbType.Date, (object?)to ?? DBNull.Value);
        select.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, limit + 1);
        var items = new List<GradedItem>();
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            double? D(int i) => reader.IsDBNull(i) ? null : reader.GetDouble(i);
            var grade = new TicketGrade(GradeStatusText.TryParse(reader.GetString(5)) ?? GradeStatus.NotGradable, D(6), D(7), D(8), reader.IsDBNull(9) ? null : reader.GetString(9), D(10), D(11), reader.GetString(12), reader.GetString(13));
            items.Add(new GradedItem(reader.GetInt64(0), DateOnly.FromDateTime(reader.GetDateTime(1)), reader.GetInt32(2), TicketGroupText.TryParse(reader.GetString(3)) ?? TicketGroup.Rejected, TicketStateText.TryParse(reader.GetString(4)) ?? TicketState.Proposed, grade));
        }

        return new GradedPage(items.Count > limit ? items.Take(limit).ToList() : items, items.Count > limit);
    }

    public async ValueTask<IReadOnlyDictionary<long, StoredGrade>> ForPlansAsync(IReadOnlyList<long> planIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(planIds);
        var result = new Dictionary<long, StoredGrade>();
        if (planIds.Count == 0)
        {
            return result;
        }

        await using var select = dataSource.CreateCommand("""
            SELECT id, plan_id, item_id, for_date, status, r, mae_r, mfe_r, exit_reason, entry_price, exit_price, cost_basis, detail, run_key, graded_at
            FROM trading.grades WHERE plan_id = ANY(@plans) ORDER BY id
            """);
        select.Parameters.AddWithValue("plans", NpgsqlDbType.Array | NpgsqlDbType.Bigint, planIds.ToArray());
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var status = GradeStatusText.TryParse(reader.GetString(4)) ?? GradeStatus.NotGradable;
            double? D(int i) => reader.IsDBNull(i) ? null : reader.GetDouble(i);
            var grade = new TicketGrade(status, D(5), D(6), D(7), reader.IsDBNull(8) ? null : reader.GetString(8), D(9), D(10), reader.GetString(11), reader.GetString(12));
            var itemId = reader.GetInt64(2);
            result[itemId] = new StoredGrade(reader.GetInt64(0), new NewGrade(reader.GetInt64(1), itemId, DateOnly.FromDateTime(reader.GetDateTime(3)), grade, reader.IsDBNull(13) ? null : reader.GetString(13), reader.GetFieldValue<DateTimeOffset>(14)));
        }

        return result;
    }

    private static string Clip(string text, int max) => text.Length <= max ? text : string.Concat(text.AsSpan(0, max - 1), "…");
}
