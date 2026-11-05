using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Shadow;

namespace XDrishti.Infrastructure.Paper;

/// <summary>
/// The grades of the shadow record behind <see cref="IShadowGradeStore"/> (spec CF-08 part B, ADR 0212): raw SQL, select and insert only (the runtime role has no more). The unique key on <c>entry_id</c> refuses a second grade of an entry, which is skipped, so a stored
/// grade is never rewritten; the database stamps <c>graded_at</c> and refuses a grade before the open of its session.
/// </summary>
internal sealed class ShadowGradeStore(NpgsqlDataSource dataSource) : IShadowGradeStore
{
    public async ValueTask<IReadOnlyList<UngradedShadowEntry>> UngradedAsync(DateOnly through, int take, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand("""
            SELECT e.id, e.session_date, e.plan_id, e.plan_item_id
            FROM paper.shadow_entries e
            WHERE e.counts_for_edge AND e.session_date <= @through AND NOT EXISTS (SELECT 1 FROM paper.shadow_grades g WHERE g.entry_id = e.id)
            ORDER BY e.session_date DESC, e.id
            LIMIT @take
            """);
        select.Parameters.AddWithValue("through", NpgsqlDbType.Date, through);
        select.Parameters.AddWithValue("take", NpgsqlDbType.Integer, Math.Max(1, take));
        var result = new List<UngradedShadowEntry>();
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new UngradedShadowEntry(reader.GetInt64(0), DateOnly.FromDateTime(reader.GetDateTime(1)), reader.GetInt64(2), reader.GetInt64(3)));
        }

        return result;
    }

    public async ValueTask<int> InsertAsync(IReadOnlyList<NewShadowGrade> grades, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(grades);
        var stored = 0;
        foreach (var g in grades)
        {
            await using var insert = dataSource.CreateCommand("""
                INSERT INTO paper.shadow_grades (entry_id, status, r_sim, r_basis, r_gross, entry_at, exit_at, exit_reason, engine_version, sheet_id)
                VALUES (@entry, @status, @r, @basis, @gross, @entry_at, @exit_at, @reason, @engine, @sheet)
                """);
            var p = insert.Parameters;
            p.AddWithValue("entry", NpgsqlDbType.Bigint, g.EntryId);
            p.AddWithValue("status", NpgsqlDbType.Varchar, g.Status);
            p.AddWithValue("r", NpgsqlDbType.Double, (object?)g.RSim ?? DBNull.Value);
            p.AddWithValue("basis", NpgsqlDbType.Varchar, (object?)g.RBasis ?? DBNull.Value);
            p.AddWithValue("gross", NpgsqlDbType.Double, (object?)g.RGross ?? DBNull.Value);
            p.AddWithValue("entry_at", NpgsqlDbType.TimestampTz, (object?)g.EntryAt ?? DBNull.Value);
            p.AddWithValue("exit_at", NpgsqlDbType.TimestampTz, (object?)g.ExitAt ?? DBNull.Value);
            p.AddWithValue("reason", NpgsqlDbType.Varchar, (object?)g.ExitReason ?? DBNull.Value);
            p.AddWithValue("engine", NpgsqlDbType.Varchar, g.EngineVersion);
            p.AddWithValue("sheet", NpgsqlDbType.Varchar, (object?)g.SheetId ?? DBNull.Value);
            try
            {
                await insert.ExecuteNonQueryAsync(cancellationToken);
                stored++;
            }
            catch (PostgresException failure) when (failure.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                // Another run graded this entry first: the stored grade stands and is never rewritten.
            }
        }

        return stored;
    }
}
