using System.Text.Json;
using Npgsql;
using XDrishti.Execution.Application;

namespace XDrishti.Execution.Persistence;

/// <summary>The newest complete settings snapshot of <c>execution.settings</c> (its <c>values</c> object, every value as text), or null when none was written. Read only.</summary>
internal sealed class PgExecSettingsSnapshots(NpgsqlDataSource data) : IExecSettingsSnapshots
{
    public async Task<IReadOnlyDictionary<string, string>?> NewestAsync(CancellationToken cancellationToken)
    {
        await using var command = data.CreateCommand("SELECT \"values\"::text FROM execution.settings ORDER BY id DESC LIMIT 1");
        if (await command.ExecuteScalarAsync(cancellationToken) is not string json)
        {
            return null;
        }

        using var document = JsonDocument.Parse(json);
        return document.RootElement.ValueKind != JsonValueKind.Object
            ? null
            : document.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString()! : p.Value.GetRawText(), StringComparer.Ordinal);
    }
}
