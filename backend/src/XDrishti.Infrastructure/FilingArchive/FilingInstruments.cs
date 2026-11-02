using Npgsql;
using XDrishti.Application.Abstractions.Filings;

namespace XDrishti.Infrastructure.FilingArchive;

/// <summary>Exact symbol match among the tracked (not retired) shares and ETFs of the master list. Reads only <c>market.instruments</c>.</summary>
internal sealed class FilingInstruments(NpgsqlDataSource dataSource) : IFilingInstruments
{
    public const string Tracked = "type IN ('share', 'etf') AND retired_at IS NULL";

    public async ValueTask<InstrumentMatch> ResolveAsync(string symbol, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(symbol);
        var text = symbol.Trim().ToUpperInvariant();
        if (text.Length is 0 or > 60)
        {
            return InstrumentMatch.Unknown;
        }

        var canonical = text.Contains(':', StringComparison.Ordinal) ? text : "NSE:" + text;
        await using var command = dataSource.CreateCommand($"SELECT id FROM market.instruments WHERE canonical_symbol = @s AND {Tracked} LIMIT 2");
        command.Parameters.AddWithValue("s", canonical);
        var ids = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            ids.Add(reader.GetGuid(0));
        }

        return ids.Count switch
        {
            0 => InstrumentMatch.Unknown,
            1 => InstrumentMatch.One(ids[0]),
            _ => InstrumentMatch.Many,
        };
    }
}
