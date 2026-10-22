using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Research;

namespace XDrishti.Infrastructure.Research;

/// <summary>
/// The stored strategies of the <c>lab</c> schema (spec P3-02d, D4; ADR 0056): raw SQL on the one pooled data source, insert-only like the other result tables. An append is one transaction; a lost race on the hash re-reads, a lost race on the
/// version or the family is tried again from the start (a few times: eight writers need eight rounds at worst). It checks the hash against the canonical text and the sizes and judges nothing else. Never logs a text.
/// </summary>
internal sealed partial class StrategyStore(NpgsqlDataSource dataSource, TimeProvider time, ILogger<StrategyStore> logger) : IStrategyStore
{
    private const int MaxSourceBytes = 262_144;

    private const int MaxNoteLength = 500;

    private const int MaxAttempts = 24;

    private const string VersionColumns = "strategy_id, version, schema_version, hash, source_text, canonical_json, parent_hash, note, created_at";

    private static readonly Regex IdPattern = new("^[A-Za-z][A-Za-z0-9_]{0,63}$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    private static readonly Regex HashPattern = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public async ValueTask<IReadOnlyList<StoredStrategy>> ListAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT s.id, s.style, s.built_in, s.created_at, COALESCE(max(v.version), 0), count(v.version)
            FROM lab.strategies s LEFT JOIN lab.strategy_versions v ON v.strategy_id = s.id
            GROUP BY s.id, s.style, s.built_in, s.created_at
            ORDER BY s.id
            """);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var list = new List<StoredStrategy>();
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new StoredStrategy(reader.GetString(0), reader.GetString(1), reader.GetBoolean(2), reader.GetFieldValue<DateTimeOffset>(3), reader.GetInt32(4), (int)reader.GetInt64(5)));
        }

        return list;
    }

    public async ValueTask<StoredStrategy?> GetAsync(string id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        return (await ListAsync(cancellationToken)).FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.Ordinal));
    }

    public async ValueTask<StoredVersion?> GetVersionAsync(string id, int? version, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        await using var command = dataSource.CreateCommand(
            $"SELECT {VersionColumns} FROM lab.strategy_versions WHERE strategy_id = @id AND (@version::int IS NULL OR version = @version) ORDER BY version DESC LIMIT 1");
        command.Parameters.AddWithValue("id", NpgsqlDbType.Text, id);
        command.Parameters.AddWithValue("version", NpgsqlDbType.Integer, (object?)version ?? DBNull.Value);
        return await ReadOneAsync(command, cancellationToken);
    }

    public async ValueTask<StoredVersion?> GetByHashAsync(string hash, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(hash);
        await using var command = dataSource.CreateCommand($"SELECT {VersionColumns} FROM lab.strategy_versions WHERE hash = @hash");
        command.Parameters.AddWithValue("hash", NpgsqlDbType.Char, hash);
        return await ReadOneAsync(command, cancellationToken);
    }

    public async ValueTask<Result<AppendResult>> AppendAsync(NewVersion version, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(version);
        if (Check(version) is { } refused)
        {
            return Result.Failure<AppendResult>(refused);
        }

        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            try
            {
                var done = await TryAppendAsync(version, cancellationToken);
                if (done.IsSuccess)
                {
                    LogAppended(logger, done.Value.StrategyId, done.Value.Version, done.Value.Hash[..12], done.Value.Created);
                }

                return done;
            }
            catch (PostgresException failure) when (failure.SqlState == PostgresErrorCodes.UniqueViolation && failure.ConstraintName == "uq_strategy_versions_hash")
            {
                // the same content was stored by someone else a moment ago: it is that row
                var stored = await GetByHashAsync(version.Hash, cancellationToken);
                if (stored is not null)
                {
                    return Result.Success(new AppendResult(stored.StrategyId, stored.Version, stored.Hash, false));
                }
            }
            catch (PostgresException failure) when (failure.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                // another writer took the version number or created the family first: start again with what is there now
            }
            catch (PostgresException failure) when (failure.SqlState == PostgresErrorCodes.ForeignKeyViolation)
            {
                return Result.Failure<AppendResult>(StrategyStoreErrors.Invalid("The parent hash is not a stored version."));
            }
            catch (PostgresException failure) when (failure.SqlState == PostgresErrorCodes.CheckViolation)
            {
                return Result.Failure<AppendResult>(StrategyStoreErrors.Invalid("A value breaks a rule of the table (" + failure.ConstraintName + ")."));
            }
        }

        LogGaveUp(logger, version.StrategyId);
        return Result.Failure<AppendResult>(StoreErrors.WriteFailed("The version could not be stored after repeated races; try again. Nothing was written."));
    }

    private static Error? Check(NewVersion v)
    {
        if (v.StrategyId is null || !IdPattern.IsMatch(v.StrategyId))
        {
            return StrategyStoreErrors.Invalid("The strategy id must be a letter followed by up to 63 letters, digits or underscores.");
        }

        if (v.Style is not ("intraday" or "swing"))
        {
            return StrategyStoreErrors.Invalid("The style must be `intraday` or `swing`.");
        }

        if (v.SourceText is null || v.CanonicalJson is null || v.Hash is null || !HashPattern.IsMatch(v.Hash) || (v.ParentHash is not null && !HashPattern.IsMatch(v.ParentHash)))
        {
            return StrategyStoreErrors.Invalid("The text, the canonical text and a 64-character lowercase hex hash are required.");
        }

        if (Encoding.UTF8.GetByteCount(v.SourceText) > MaxSourceBytes)
        {
            return StrategyStoreErrors.Invalid("The strategy text is larger than 262,144 bytes.");
        }

        if (v.Note is { Length: > MaxNoteLength })
        {
            return StrategyStoreErrors.Invalid("The note is longer than 500 characters.");
        }

        if (v.ExpectedVersion is < 1 || v.SchemaVersion < 1)
        {
            return StrategyStoreErrors.Invalid("The expected version and the schema version are at least 1.");
        }

        return string.Equals(CanonicalText.Sha256Hex(v.CanonicalJson), v.Hash, StringComparison.Ordinal) ? null : StrategyStoreErrors.IntegrityFailure();
    }

    private async ValueTask<Result<AppendResult>> TryAppendAsync(NewVersion v, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var find = new NpgsqlCommand("SELECT strategy_id, version, hash FROM lab.strategy_versions WHERE hash = @hash", connection, transaction))
        {
            find.Parameters.AddWithValue("hash", NpgsqlDbType.Char, v.Hash);
            await using var reader = await find.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                return Result.Success(new AppendResult(reader.GetString(0), reader.GetInt32(1), reader.GetString(2), false));
            }
        }

        string? existing = null;
        var builtIn = false;
        await using (var family = new NpgsqlCommand("SELECT id, built_in FROM lab.strategies WHERE lower(id) = lower(@id)", connection, transaction))
        {
            family.Parameters.AddWithValue("id", NpgsqlDbType.Text, v.StrategyId);
            await using var reader = await family.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                existing = reader.GetString(0);
                builtIn = reader.GetBoolean(1);
            }
        }

        if (existing is not null && !string.Equals(existing, v.StrategyId, StringComparison.Ordinal))
        {
            return Result.Failure<AppendResult>(StrategyStoreErrors.IdClash(v.StrategyId, existing));
        }

        if (existing is not null && builtIn && !v.Seed)
        {
            return Result.Failure<AppendResult>(StrategyStoreErrors.BuiltInReadOnly(v.StrategyId));
        }

        var next = 1;
        string? previousHash = null;
        if (existing is not null)
        {
            await using var latest = new NpgsqlCommand("SELECT version, hash FROM lab.strategy_versions WHERE strategy_id = @id ORDER BY version DESC LIMIT 1", connection, transaction);
            latest.Parameters.AddWithValue("id", NpgsqlDbType.Text, v.StrategyId);
            await using var reader = await latest.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                next = reader.GetInt32(0) + 1;
                previousHash = reader.GetString(1);
            }
        }

        if (v.ExpectedVersion is { } expected && expected != next)
        {
            return Result.Failure<AppendResult>(StrategyStoreErrors.VersionExists(v.StrategyId, expected, next));
        }

        var now = time.GetUtcNow();
        if (existing is null)
        {
            await using var create = new NpgsqlCommand("INSERT INTO lab.strategies (id, style, built_in, created_at) VALUES (@id, @style, @built_in, @created_at)", connection, transaction);
            create.Parameters.AddWithValue("id", NpgsqlDbType.Text, v.StrategyId);
            create.Parameters.AddWithValue("style", NpgsqlDbType.Text, v.Style);
            create.Parameters.AddWithValue("built_in", NpgsqlDbType.Boolean, v.Seed);
            create.Parameters.AddWithValue("created_at", NpgsqlDbType.TimestampTz, now);
            await create.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var insert = new NpgsqlCommand($"INSERT INTO lab.strategy_versions ({VersionColumns}) VALUES (@strategy_id, @version, @schema_version, @hash, @source_text, @canonical_json, @parent_hash, @note, @created_at)", connection, transaction))
        {
            var p = insert.Parameters;
            p.AddWithValue("strategy_id", NpgsqlDbType.Text, v.StrategyId);
            p.AddWithValue("version", NpgsqlDbType.Integer, next);
            p.AddWithValue("schema_version", NpgsqlDbType.Smallint, (short)v.SchemaVersion);
            p.AddWithValue("hash", NpgsqlDbType.Char, v.Hash);
            p.AddWithValue("source_text", NpgsqlDbType.Text, v.SourceText);
            p.AddWithValue("canonical_json", NpgsqlDbType.Text, v.CanonicalJson);
            p.AddWithValue("parent_hash", NpgsqlDbType.Char, (object?)(v.ParentHash ?? previousHash) ?? DBNull.Value);
            p.AddWithValue("note", NpgsqlDbType.Text, (object?)v.Note ?? DBNull.Value);
            p.AddWithValue("created_at", NpgsqlDbType.TimestampTz, now);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return Result.Success(new AppendResult(v.StrategyId, next, v.Hash, true));
    }

    private static async ValueTask<StoredVersion?> ReadOneAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new StoredVersion(
            reader.GetString(0),
            reader.GetInt32(1),
            reader.GetInt16(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.GetFieldValue<DateTimeOffset>(8));
    }

    [LoggerMessage(EventId = 4431, Level = LogLevel.Information, Message = "lab.store.appended strategy {StrategyId} version {Version} hash {Hash}, created {Created}")]
    private static partial void LogAppended(ILogger logger, string strategyId, int version, string hash, bool created);

    [LoggerMessage(EventId = 4432, Level = LogLevel.Error, Message = "lab.store.gave_up strategy {StrategyId}: the append lost its race too many times")]
    private static partial void LogGaveUp(ILogger logger, string strategyId);
}
