using Npgsql;
using XDrishti.Application.Abstractions.Ai;

namespace XDrishti.Infrastructure.Ai.ReadOnly;

/// <summary>Reads both switches in one round trip through <c>ai.context_allows</c> (spec P9-02): the database decides, so a row inserted by the owner takes effect on the next call, with nothing to restart or invalidate.</summary>
internal sealed class AiContextReader(AiDataSources sources) : IAiContext
{
    private const string Sql = "SELECT ai.context_allows('assistant'), ai.context_allows('portfolio')";

    public async Task<AiContext> GetAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var command = sources.Read.CreateCommand(Sql);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken) ? new AiContext(reader.GetBoolean(0), reader.GetBoolean(1)) : new AiContext(false, false);
        }
        catch (Exception failure) when (failure is NpgsqlException or TimeoutException or InvalidOperationException)
        {
            throw new AiDatabaseUnavailableException("The read-only database is not available.", failure);
        }
    }
}
