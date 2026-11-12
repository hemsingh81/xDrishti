using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Market.Lists;

namespace XDrishti.Infrastructure.Market.Lists;

/// <summary><c>market.list_day_coverage</c> and <c>market.list_day_members</c> by plain SQL (insert-only: the runtime role has no UPDATE or DELETE on either). A day is written in one transaction; a stored day is left alone.</summary>
internal sealed class ListDayStore(NpgsqlDataSource dataSource) : IListDayStore
{
    public async ValueTask<IReadOnlySet<DateOnly>> CoveredAsync(Guid basketId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT trade_date FROM market.list_day_coverage WHERE basket_id = @basket AND trade_date BETWEEN @from AND @to");
        command.Parameters.AddWithValue("basket", NpgsqlDbType.Uuid, basketId);
        command.Parameters.AddWithValue("from", from);
        command.Parameters.AddWithValue("to", to);
        var days = new HashSet<DateOnly>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            days.Add(reader.GetFieldValue<DateOnly>(0));
        }

        return days;
    }

    public async ValueTask<bool> InsertAsync(Guid basketId, DateOnly date, IReadOnlyCollection<Guid> instrumentIds, string source, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var coverage = new NpgsqlCommand(
            "INSERT INTO market.list_day_coverage (basket_id, trade_date, source, member_count, loaded_at) VALUES (@basket, @date, @source, @count, now()) ON CONFLICT DO NOTHING", connection, transaction))
        {
            coverage.Parameters.AddWithValue("basket", NpgsqlDbType.Uuid, basketId);
            coverage.Parameters.AddWithValue("date", date);
            coverage.Parameters.AddWithValue("source", source);
            coverage.Parameters.AddWithValue("count", instrumentIds.Count);
            if (await coverage.ExecuteNonQueryAsync(cancellationToken) == 0)
            {
                return false;
            }
        }

        if (instrumentIds.Count > 0)
        {
            await using var members = new NpgsqlCommand(
                "INSERT INTO market.list_day_members (basket_id, trade_date, instrument_id) SELECT @basket, @date, unnest(@ids) ON CONFLICT DO NOTHING", connection, transaction);
            members.Parameters.AddWithValue("basket", NpgsqlDbType.Uuid, basketId);
            members.Parameters.AddWithValue("date", date);
            members.Parameters.AddWithValue("ids", instrumentIds.Distinct().ToArray());
            await members.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }
}
