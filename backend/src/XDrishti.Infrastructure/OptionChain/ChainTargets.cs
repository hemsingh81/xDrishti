using Npgsql;
using XDrishti.Application.Abstractions.OptionChain;
using XDrishti.Application.Abstractions.Options;
using XDrishti.Domain.Options;

namespace XDrishti.Infrastructure.OptionChain;

/// <summary>
/// The underlyings of the option-chain recorder (P14-02): the symbols of the app setting <c>options.underlyings</c> (ADR 0175) with their Dhan id and segment from the master list - an index by its <c>IDX_I</c> id, a share by its <c>NSE_EQ</c> id. A symbol
/// without such an id is left out. Reads reference data only, no chain row.
/// </summary>
internal sealed class ChainTargets(IOptionUnderlyingSetting setting, NpgsqlDataSource dataSource) : IChainTargets
{
    private const string Query =
        """
        SELECT i.id, i.canonical_symbol, b.segment, b.broker_id
        FROM market.instruments i
        JOIN market.instrument_broker_ids b ON b.instrument_id = i.id AND b.broker = 'dhan' AND b.segment IN ('IDX_I', 'NSE_EQ')
        WHERE i.canonical_symbol = ANY(@symbols) AND i.retired_at IS NULL
        ORDER BY i.canonical_symbol
        """;

    public async ValueTask<IReadOnlyList<ChainTarget>> LoadAsync(CancellationToken cancellationToken)
    {
        var listed = OptionUnderlyings.Parse(await setting.ReadAsync(cancellationToken));
        if (listed.IsEmpty)
        {
            return [];
        }

        await using var command = dataSource.CreateCommand(Query);
        command.Parameters.AddWithValue("symbols", listed.Symbols.ToArray());
        var targets = new List<ChainTarget>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var segment = reader.GetString(2);
            if (long.TryParse(reader.GetString(3), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var scrip) && scrip > 0)
            {
                targets.Add(new ChainTarget(reader.GetGuid(0), reader.GetString(1), scrip, segment, segment == "IDX_I"));
            }
        }

        return targets;
    }
}
