using System.Text.Json;
using Npgsql;

namespace XDrishti.Infrastructure.OptionBackfill;

/// <summary>
/// The frozen order of the backfill's shares (P14-03): the ranked symbols as a JSON array in the app setting row <c>options.backfill_stocks</c>, written once with <c>ON CONFLICT DO NOTHING</c> (the first call wins) and never rewritten by the application.
/// The turnover ranking moves every night; a plan that followed it would start fetching other shares and leave the first ones half done.
/// </summary>
internal sealed class BackfillStockList(NpgsqlDataSource dataSource)
{
    public const string Key = "options.backfill_stocks";

    /// <summary>The stored symbols in their order; empty when none was stored or the value is not a list of text.</summary>
    public async ValueTask<IReadOnlyList<string>> ReadAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT value FROM platform.app_settings WHERE key = @key");
        command.Parameters.AddWithValue("key", Key);
        return Parse(await command.ExecuteScalarAsync(cancellationToken) as string);
    }

    /// <summary>Stores the list when there is none; true when this call wrote it.</summary>
    public async ValueTask<bool> WriteOnceAsync(IReadOnlyList<string> symbols, string actor, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("INSERT INTO platform.app_settings (key, value, updated_by, updated_at) VALUES (@key, @value, @actor, @now) ON CONFLICT (key) DO NOTHING");
        command.Parameters.AddWithValue("key", Key);
        command.Parameters.AddWithValue("value", JsonSerializer.Serialize(symbols));
        command.Parameters.AddWithValue("actor", actor);
        command.Parameters.AddWithValue("now", now.UtcDateTime);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    internal static IReadOnlyList<string> Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<string[]>(value)?.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
