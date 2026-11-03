using Npgsql;
using XDrishti.Application.Abstractions.OptionCosts;
using XDrishti.Domain.Market;
using XDrishti.Domain.Options.Costs;

namespace XDrishti.Infrastructure.OptionCosts;

/// <summary>
/// The half-spread model's read of <c>options.chain_snapshots</c> (P14-07, ADR 0194): one <c>(underlying_id, ts)</c> range of one India day per call, and only the instant, expiry, strike, the underlying's price, the bid and the ask. No IV, Greek, open
/// interest or volume column is selected and nothing is written. Which days to ask for is the caller's: only usable sessions.
/// </summary>
internal sealed class ChainSpreadReader(NpgsqlDataSource dataSource) : IChainSpreadReader
{
    private static DateTime Start(DateOnly day) => new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), IndiaTime.Offset).UtcDateTime;

    public async ValueTask<IReadOnlyList<SpreadQuote>> QuotesAsync(Guid underlyingId, DateOnly session, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT ts, expiry, strike_px, underlying_px, bid_px, ask_px
            FROM options.chain_snapshots
            WHERE underlying_id = @u AND ts >= @from AND ts < @to
            ORDER BY ts, expiry, strike_px
            """);
        command.Parameters.AddWithValue("u", underlyingId);
        command.Parameters.AddWithValue("from", Start(session));
        command.Parameters.AddWithValue("to", Start(session.AddDays(1)));
        var list = new List<SpreadQuote>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new SpreadQuote(
                reader.GetFieldValue<DateTimeOffset>(0),
                reader.GetFieldValue<DateOnly>(1),
                reader.GetInt32(2),
                reader.GetInt32(3),
                reader.IsDBNull(4) ? null : reader.GetInt32(4),
                reader.IsDBNull(5) ? null : reader.GetInt32(5)));
        }

        return list;
    }
}
