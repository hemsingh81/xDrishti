using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.OptionCosts;
using XDrishti.Domain.Options.Costs;

namespace XDrishti.Infrastructure.OptionCosts;

/// <summary><c>research.option_spread_models</c> and <c>research.option_spread_buckets</c> behind <see cref="ISpreadModelStore"/> (P14-07, ADR 0194): raw SQL, insert and select only (the runtime role has no more). The id is the key; a second insert writes nothing.</summary>
internal sealed class SpreadModelStore(NpgsqlDataSource dataSource) : ISpreadModelStore
{
    public async ValueTask<bool> ExistsAsync(string modelId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT 1 FROM research.option_spread_models WHERE model_id = @id");
        command.Parameters.AddWithValue("id", NpgsqlDbType.Char, modelId ?? string.Empty);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    public async ValueTask<bool> AppendAsync(BuiltSpreadModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var head = new NpgsqlCommand(
            """
            INSERT INTO research.option_spread_models (model_id, underlying_id, sessions, first_session, last_session, settings)
            VALUES (@id, @u, @sessions, @first, @last, @settings)
            ON CONFLICT (model_id) DO NOTHING
            """, connection, transaction))
        {
            head.Parameters.AddWithValue("id", NpgsqlDbType.Char, model.ModelId);
            head.Parameters.AddWithValue("u", model.UnderlyingId);
            head.Parameters.AddWithValue("sessions", model.Sessions);
            head.Parameters.AddWithValue("first", model.FirstSession);
            head.Parameters.AddWithValue("last", model.LastSession);
            head.Parameters.AddWithValue("settings", NpgsqlDbType.Jsonb, model.SettingsJson);
            if (await head.ExecuteNonQueryAsync(cancellationToken) == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }
        }

        foreach (var bucket in model.Buckets)
        {
            await using var row = new NpgsqlCommand(
                "INSERT INTO research.option_spread_buckets (model_id, moneyness, dte, tod, n, half_spread_pct) VALUES (@id, @m, @d, @t, @n, @pct)", connection, transaction);
            row.Parameters.AddWithValue("id", NpgsqlDbType.Char, model.ModelId);
            row.Parameters.AddWithValue("m", NpgsqlDbType.Varchar, bucket.Moneyness);
            row.Parameters.AddWithValue("d", NpgsqlDbType.Varchar, bucket.Dte);
            row.Parameters.AddWithValue("t", NpgsqlDbType.Varchar, bucket.Tod);
            row.Parameters.AddWithValue("n", bucket.N);
            row.Parameters.AddWithValue("pct", bucket.HalfSpreadPct);
            await row.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async ValueTask<HalfSpreadModel?> LatestAsync(Guid underlyingId, CancellationToken cancellationToken)
    {
        await using var head = dataSource.CreateCommand(
            """
            SELECT model_id, sessions, first_session, last_session FROM research.option_spread_models
            WHERE underlying_id = @u ORDER BY last_session DESC, built_at DESC, model_id LIMIT 1
            """);
        head.Parameters.AddWithValue("u", underlyingId);
        string id;
        int sessions;
        DateOnly first, last;
        await using (var reader = await head.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            id = reader.GetString(0);
            sessions = reader.GetInt32(1);
            first = reader.GetFieldValue<DateOnly>(2);
            last = reader.GetFieldValue<DateOnly>(3);
        }

        await using var buckets = dataSource.CreateCommand("SELECT moneyness, dte, tod, n, half_spread_pct FROM research.option_spread_buckets WHERE model_id = @id ORDER BY moneyness, dte, tod");
        buckets.Parameters.AddWithValue("id", NpgsqlDbType.Char, id);
        var rows = new List<SpreadBucketRow>();
        await using var rowReader = await buckets.ExecuteReaderAsync(cancellationToken);
        while (await rowReader.ReadAsync(cancellationToken))
        {
            rows.Add(new SpreadBucketRow(rowReader.GetString(0), rowReader.GetString(1), rowReader.GetString(2), rowReader.GetInt32(3), rowReader.GetDouble(4)));
        }

        return new HalfSpreadModel(id, underlyingId, sessions, first, last, rows);
    }
}
