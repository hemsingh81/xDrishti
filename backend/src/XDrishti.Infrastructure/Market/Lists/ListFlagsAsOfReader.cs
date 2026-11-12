using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Market.Lists;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Baskets.Lists;

namespace XDrishti.Infrastructure.Market.Lists;

/// <summary>
/// The as-of port (DATA-01, ADR 0274) over our own tables, no network. The F&amp;O-ban list for a date is covered when a back-filled day row exists for it (members from <c>list_day_members</c>), or when the date lies in the forward span of the daily fetch
/// (the day after the first good check through <c>known_through</c>; members from the dated stays). The ASM/GSM list is covered in the span from the first to the last good check of the master (no archive). Any other date is not covered: unavailable, never "not banned".
/// </summary>
internal sealed class ListFlagsAsOfReader(NpgsqlDataSource dataSource) : IListFlagsAsOf
{
    private const string StayMembers = """
        SELECT instrument_id FROM market.basket_members
        WHERE basket_id = @basket AND instrument_id = ANY(@ids)
          AND (valid_from AT TIME ZONE 'Asia/Kolkata')::date <= @date AND (valid_to IS NULL OR (valid_to AT TIME ZONE 'Asia/Kolkata')::date > @date)
        """;

    public async ValueTask<ListFlags> ForAsync(IReadOnlyCollection<Guid> instrumentIds, DateOnly banDate, DateOnly surveillanceDate, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instrumentIds);
        var ids = instrumentIds.Distinct().ToArray();
        var (banCovered, banned) = await BanAsync(ids, banDate, cancellationToken);
        var (survCovered, surveilled) = await SurveillanceAsync(ids, surveillanceDate, cancellationToken);
        return new ListFlags(banCovered, survCovered, banned, surveilled);
    }

    private async ValueTask<(bool Covered, HashSet<Guid> Members)> BanAsync(Guid[] ids, DateOnly date, CancellationToken cancellationToken)
    {
        await using (var command = dataSource.CreateCommand("SELECT 1 FROM market.list_day_coverage WHERE basket_id = @basket AND trade_date = @date"))
        {
            command.Parameters.AddWithValue("basket", NpgsqlDbType.Uuid, ListBaskets.Ban.Id);
            command.Parameters.AddWithValue("date", date);
            if (await command.ExecuteScalarAsync(cancellationToken) is not null)
            {
                await using var members = dataSource.CreateCommand("SELECT instrument_id FROM market.list_day_members WHERE basket_id = @basket AND trade_date = @date AND instrument_id = ANY(@ids)");
                members.Parameters.AddWithValue("basket", NpgsqlDbType.Uuid, ListBaskets.Ban.Id);
                members.Parameters.AddWithValue("date", date);
                members.Parameters.AddWithValue("ids", ids);
                return (true, await ReadIdsAsync(members, cancellationToken));
            }
        }

        var (first, last) = await FetchSpanAsync(ListBaskets.Ban.Id, cancellationToken);
        DateOnly? through = null;
        await using (var command = dataSource.CreateCommand("SELECT known_through FROM market.basket_sources WHERE basket_id = @basket"))
        {
            command.Parameters.AddWithValue("basket", NpgsqlDbType.Uuid, ListBaskets.Ban.Id);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken) && !reader.IsDBNull(0))
            {
                through = reader.GetFieldValue<DateOnly>(0);
            }
        }

        if (first is null || last is null || through is null || date < first.Value.AddDays(1) || date > through.Value)
        {
            return (false, []);
        }

        return (true, await StayMembersAsync(ListBaskets.Ban.Id, ids, date, cancellationToken));
    }

    private async ValueTask<(bool Covered, HashSet<Guid> Members)> SurveillanceAsync(Guid[] ids, DateOnly date, CancellationToken cancellationToken)
    {
        var (first, last) = await FetchSpanAsync(ListBaskets.AsmGsm.Id, cancellationToken);
        if (first is null || last is null || date < first.Value || date > last.Value)
        {
            return (false, []);
        }

        return (true, await StayMembersAsync(ListBaskets.AsmGsm.Id, ids, date, cancellationToken));
    }

    private async ValueTask<(DateOnly? First, DateOnly? Last)> FetchSpanAsync(Guid basketId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT min(fetched_at), max(fetched_at) FROM market.basket_fetches WHERE basket_id = @basket AND outcome IN ('applied', 'unchanged')");
        command.Parameters.AddWithValue("basket", NpgsqlDbType.Uuid, basketId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken) && !reader.IsDBNull(0))
        {
            return (IndiaTime.Today(reader.GetFieldValue<DateTimeOffset>(0)), IndiaTime.Today(reader.GetFieldValue<DateTimeOffset>(1)));
        }

        return (null, null);
    }

    private async ValueTask<HashSet<Guid>> StayMembersAsync(Guid basketId, Guid[] ids, DateOnly date, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(StayMembers);
        command.Parameters.AddWithValue("basket", NpgsqlDbType.Uuid, basketId);
        command.Parameters.AddWithValue("ids", ids);
        command.Parameters.AddWithValue("date", date);
        return await ReadIdsAsync(command, cancellationToken);
    }

    private static async ValueTask<HashSet<Guid>> ReadIdsAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        var found = new HashSet<Guid>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            found.Add(reader.GetGuid(0));
        }

        return found;
    }
}
