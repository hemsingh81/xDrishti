using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Market.Lists;
using XDrishti.Domain.Market.Baskets.Lists;

namespace XDrishti.Infrastructure.Market.Lists;

/// <summary>
/// The as-of port (DATA-01, ADR 0274) over our own tables, no network. The F&amp;O-ban list is covered on a date only when a dated day row exists for it (written by the backfill, or by the daily fetch of that trade date's file); members come from <c>list_day_members</c>.
/// The ASM/GSM list is covered on a date only when a good master refresh was recorded on that India date (no archive); members come from the dated stays. Any other date is not covered: unavailable, never "not banned".
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
        await using (var command = dataSource.CreateCommand("SELECT source FROM market.list_day_coverage WHERE basket_id = @basket AND trade_date = @date"))
        {
            command.Parameters.AddWithValue("basket", NpgsqlDbType.Uuid, ListBaskets.Ban.Id);
            command.Parameters.AddWithValue("date", date);
            if (await command.ExecuteScalarAsync(cancellationToken) is string source)
            {
                await using var members = dataSource.CreateCommand("SELECT instrument_id FROM market.list_day_members WHERE basket_id = @basket AND trade_date = @date AND instrument_id = ANY(@ids)");
                members.Parameters.AddWithValue("basket", NpgsqlDbType.Uuid, ListBaskets.Ban.Id);
                members.Parameters.AddWithValue("date", date);
                members.Parameters.AddWithValue("ids", ids);
                var found = await ReadIdsAsync(members, cancellationToken);
                // shortcut: a same-date file re-published later with a symbol the catalogue cannot match leaves the date covered (the day row stays from the first file); the unmatched symbol cannot be told apart anyway. Known limit, ADR 0274.
                if (source == "daily")
                {
                    // A same-date file re-published later is applied to the stays but the day row keeps the first file: the union with the stays covering the date can only over-state a ban, never hide one.
                    found.UnionWith(await StayMembersAsync(ListBaskets.Ban.Id, ids, date, cancellationToken));
                }

                return (true, found);
            }
        }

        return (false, []); // no stored row for the date: unavailable, never "not banned"
    }

    // Covered only on a date a good master refresh was recorded for (an applied or unchanged fetch row of that India date): a gap day is unavailable, not "not flagged".
    private async ValueTask<(bool Covered, HashSet<Guid> Members)> SurveillanceAsync(Guid[] ids, DateOnly date, CancellationToken cancellationToken)
    {
        await using (var command = dataSource.CreateCommand("SELECT 1 FROM market.basket_fetches WHERE basket_id = @basket AND outcome IN ('applied', 'unchanged') AND (fetched_at AT TIME ZONE 'Asia/Kolkata')::date = @date LIMIT 1"))
        {
            command.Parameters.AddWithValue("basket", NpgsqlDbType.Uuid, ListBaskets.AsmGsm.Id);
            command.Parameters.AddWithValue("date", date);
            if (await command.ExecuteScalarAsync(cancellationToken) is null)
            {
                return (false, []);
            }
        }

        return (true, await StayMembersAsync(ListBaskets.AsmGsm.Id, ids, date, cancellationToken));
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
