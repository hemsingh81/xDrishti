using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Domain.Learning;

namespace XDrishti.Infrastructure.Learning;

/// <summary><c>lab.expression_profiles</c> behind <see cref="IExpressionProfileStore"/> (P14-06, ADR 0193): raw SQL, insert and select only (the runtime role has no more). Content-addressed: the hash is the key and a second insert writes nothing.</summary>
internal sealed class ExpressionProfileStore(NpgsqlDataSource dataSource) : IExpressionProfileStore
{
    private const string Columns = "profile_hash, kind, params::text, canonical_text, created_at";

    public async ValueTask<bool> AppendAsync(ExpressionProfile profile, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        await using var command = dataSource.CreateCommand("""
            INSERT INTO lab.expression_profiles (profile_hash, kind, params, canonical_text) VALUES (@hash, @kind, @params, @text)
            ON CONFLICT (profile_hash) DO NOTHING
            """);
        command.Parameters.AddWithValue("hash", NpgsqlDbType.Char, profile.Hash);
        command.Parameters.AddWithValue("kind", NpgsqlDbType.Varchar, profile.Kind.Code());
        command.Parameters.AddWithValue("params", NpgsqlDbType.Jsonb, ParametersJson(profile));
        command.Parameters.AddWithValue("text", NpgsqlDbType.Text, profile.CanonicalText);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async ValueTask<StoredExpressionProfile?> GetAsync(string profileHash, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand($"SELECT {Columns} FROM lab.expression_profiles WHERE profile_hash = @hash");
        command.Parameters.AddWithValue("hash", NpgsqlDbType.Char, profileHash ?? string.Empty);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    public async ValueTask<IReadOnlyList<StoredExpressionProfile>> ListAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand($"SELECT {Columns} FROM lab.expression_profiles ORDER BY created_at, profile_hash");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var list = new List<StoredExpressionProfile>();
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(Read(reader));
        }

        return list;
    }

    private static string ParametersJson(ExpressionProfile profile)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var (name, value) in profile.Parameters)
            {
                writer.WriteString(name, value);
            }

            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private static StoredExpressionProfile Read(NpgsqlDataReader reader)
    {
        var hash = reader.GetString(0);
        var kind = ExpressionNames.Parse(reader.GetString(1)) ?? throw new InvalidDataException("A stored expression profile has an unknown kind.");
        using var document = JsonDocument.Parse(reader.GetString(2));
        var values = document.RootElement.EnumerateObject().Select(p => new KeyValuePair<string, object>(p.Name, p.Value.GetString() ?? string.Empty));
        var made = ExpressionProfile.Create(kind, values);
        if (made.IsFailure || !string.Equals(made.Value.Hash, hash, StringComparison.Ordinal) || !string.Equals(made.Value.CanonicalText, reader.GetString(3), StringComparison.Ordinal))
        {
            throw new InvalidDataException("A stored expression profile does not hash to its key.");
        }

        return new StoredExpressionProfile(made.Value, reader.GetFieldValue<DateTimeOffset>(4));
    }
}
