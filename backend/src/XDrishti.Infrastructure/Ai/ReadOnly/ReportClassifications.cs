using Npgsql;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Application.Abstractions.Portfolio;

namespace XDrishti.Infrastructure.Ai.ReadOnly;

/// <summary>The sector lens of the report engine for the tool server: the sector of the instrument master from the reviewed view <c>aiv.instruments</c> (as of today). Baskets are not needed by a report and answer nothing.</summary>
internal sealed class ReportClassifications(AiDataSources sources) : IHoldingClassifications
{
    public async ValueTask<IReadOnlyDictionary<Guid, string>> SectorsAsync(IReadOnlyCollection<Guid> instrumentIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instrumentIds);
        var result = new Dictionary<Guid, string>();
        if (instrumentIds.Count == 0)
        {
            return result;
        }

        try
        {
            await using var command = sources.Read.CreateCommand("SELECT id, sector FROM aiv.instruments WHERE id = ANY(@ids) AND sector IS NOT NULL");
            command.Parameters.AddWithValue("ids", instrumentIds.Distinct().ToArray());
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var sector = reader.GetString(1).Trim();
                if (sector.Length > 0)
                {
                    result[reader.GetGuid(0)] = sector;
                }
            }
        }
        catch (Exception failure) when (failure is NpgsqlException or TimeoutException or InvalidOperationException)
        {
            throw new AiDatabaseUnavailableException("The read-only database is not available.", failure);
        }

        return result;
    }

    public ValueTask<IReadOnlyDictionary<Guid, IReadOnlyList<BasketRef>>> BasketsAsync(IReadOnlyCollection<Guid> instrumentIds, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyDictionary<Guid, IReadOnlyList<BasketRef>>>(new Dictionary<Guid, IReadOnlyList<BasketRef>>());
}
