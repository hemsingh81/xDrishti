using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Market.Context;
using XDrishti.Domain.Reporting;

namespace XDrishti.Infrastructure.Market;

/// <summary>
/// The stored daily context of baskets (<c>market.basket_context_daily</c>, spec BK-08): raw SQL, insert and select only for the runtime role. <c>ON CONFLICT DO NOTHING</c>, so a stored day is never touched. About one row per basket and trading day.
/// </summary>
internal sealed class BasketContextStore(NpgsqlDataSource dataSource, TimeProvider clock) : IBasketContextStore
{
    private const string Columns = "basket_id, as_of, n_members, n_voters, b20, b50, b200, ad_share, new_high_share, dispersion, rho_bar, state, membership_basis";

    public async ValueTask<bool> InsertOnceAsync(BasketContextRow row, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(row);
        await using var insert = dataSource.CreateCommand($"INSERT INTO market.basket_context_daily ({Columns}, created_at) VALUES (@basket, @date, @members, @voters, @b20, @b50, @b200, @ad, @high, @disp, @rho, @state, @basis, @now) ON CONFLICT (basket_id, as_of) DO NOTHING");
        var p = insert.Parameters;
        p.AddWithValue("basket", NpgsqlDbType.Uuid, row.BasketId);
        p.AddWithValue("date", NpgsqlDbType.Date, row.AsOf);
        p.AddWithValue("members", NpgsqlDbType.Integer, row.NMembers);
        p.AddWithValue("voters", NpgsqlDbType.Integer, row.NVoters);
        Number(p, "b20", row.B20);
        Number(p, "b50", row.B50);
        Number(p, "b200", row.B200);
        Number(p, "ad", row.AdShare);
        Number(p, "high", row.NewHighShare);
        Number(p, "disp", row.Dispersion);
        Number(p, "rho", row.RhoBar);
        p.AddWithValue("state", NpgsqlDbType.Text, row.State);
        p.AddWithValue("basis", NpgsqlDbType.Text, row.MembershipBasis);
        p.AddWithValue("now", NpgsqlDbType.TimestampTz, clock.GetUtcNow());
        return await insert.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async ValueTask<BasketContextRow?> LatestBeforeAsync(Guid basketId, DateOnly beforeExclusive, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand($"SELECT {Columns} FROM market.basket_context_daily WHERE basket_id = @basket AND as_of < @before ORDER BY as_of DESC LIMIT 1");
        select.Parameters.AddWithValue("basket", NpgsqlDbType.Uuid, basketId);
        select.Parameters.AddWithValue("before", NpgsqlDbType.Date, beforeExclusive);
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    public async ValueTask<IReadOnlySet<DateOnly>> DatesAsync(Guid basketId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand("SELECT as_of FROM market.basket_context_daily WHERE basket_id = @basket AND as_of BETWEEN @from AND @to");
        select.Parameters.AddWithValue("basket", NpgsqlDbType.Uuid, basketId);
        select.Parameters.AddWithValue("from", NpgsqlDbType.Date, from);
        select.Parameters.AddWithValue("to", NpgsqlDbType.Date, to);
        var dates = new HashSet<DateOnly>();
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            dates.Add(reader.GetFieldValue<DateOnly>(0));
        }

        return dates;
    }

    public async ValueTask<IReadOnlyList<BasketContextRow>> ListAsync(Guid basketId, DateOnly? from, DateOnly? to, int take, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand($"SELECT {Columns} FROM market.basket_context_daily WHERE basket_id = @basket AND (@from::date IS NULL OR as_of >= @from) AND (@to::date IS NULL OR as_of <= @to) ORDER BY as_of DESC LIMIT @take");
        select.Parameters.AddWithValue("basket", NpgsqlDbType.Uuid, basketId);
        select.Parameters.Add(new NpgsqlParameter("from", NpgsqlDbType.Date) { Value = from is { } f ? f : DBNull.Value });
        select.Parameters.Add(new NpgsqlParameter("to", NpgsqlDbType.Date) { Value = to is { } t ? t : DBNull.Value });
        select.Parameters.AddWithValue("take", NpgsqlDbType.Integer, Math.Clamp(take, 1, 400));
        var rows = new List<BasketContextRow>();
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(Read(reader));
        }

        return rows;
    }

    private static void Number(NpgsqlParameterCollection p, string name, double? value) =>
        p.Add(new NpgsqlParameter(name, NpgsqlDbType.Numeric) { Value = value is { } v ? (object)(decimal)v : DBNull.Value });

    private static BasketContextRow Read(NpgsqlDataReader r) => new(
        r.GetGuid(0),
        r.GetFieldValue<DateOnly>(1),
        r.GetInt32(2),
        r.GetInt32(3),
        Double(r, 4),
        Double(r, 5),
        Double(r, 6),
        Double(r, 7),
        Double(r, 8),
        Double(r, 9),
        Double(r, 10),
        r.GetString(11),
        r.GetString(12));

    private static double? Double(NpgsqlDataReader r, int i) => r.IsDBNull(i) ? null : (double)r.GetDecimal(i);
}
