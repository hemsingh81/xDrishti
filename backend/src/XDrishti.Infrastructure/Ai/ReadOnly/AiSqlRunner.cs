using System.Diagnostics;
using System.Globalization;
using Npgsql;
using XDrishti.Application.Abstractions.Ai;

namespace XDrishti.Infrastructure.Ai.ReadOnly;

/// <summary>
/// Runs one ad-hoc <c>SELECT</c> for <c>run_sql</c> (spec P9-02 AC 8, P9-00 D10). The boundary is the privilege of the login <c>xd_mcp_sql</c> (SELECT on the <c>aiv</c> views only); on top: <c>BEGIN READ ONLY</c>, a local statement timeout, the text as a <c>NO SCROLL</c> cursor (which refuses
/// <c>SELECT INTO</c> and data-modifying statements and lets the server hand over one row more than asked for, to know whether the answer was cut), and a rollback. The text is the pre-checked single statement; if it were not, the database still refuses what the role may not do.
/// </summary>
internal sealed class AiSqlRunner(AiDataSources sources) : IAiSqlRunner
{
    private const string UnreadableNumber = "numeric_out_of_range";

    public async Task<SqlRunOutcome> RunAsync(string statement, int maxRows, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(statement);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxRows, 1);
        var clock = Stopwatch.StartNew();
        using var guard = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        guard.CancelAfter(timeout + TimeSpan.FromMilliseconds(500));
        NpgsqlConnection? connection = null;
        try
        {
            connection = await sources.Sql.OpenConnectionAsync(guard.Token);
            await ExecuteAsync(connection, "BEGIN READ ONLY", guard.Token);
            await ExecuteAsync(connection, string.Create(CultureInfo.InvariantCulture, $"SET LOCAL statement_timeout = {(int)timeout.TotalMilliseconds}"), guard.Token);
            await DeclareAsync(connection, statement, guard.Token);
            return await FetchAsync(connection, maxRows, clock, guard.Token);
        }
        catch (PostgresException failure) when (failure.SqlState == PostgresErrorCodes.InsufficientPrivilege)
        {
            return SqlRunOutcome.Failed(SqlRunStatus.Denied, clock.ElapsedMilliseconds);
        }
        catch (PostgresException failure) when (failure.SqlState == PostgresErrorCodes.QueryCanceled)
        {
            return SqlRunOutcome.Failed(SqlRunStatus.Timeout, clock.ElapsedMilliseconds);
        }
        catch (PostgresException failure) when (failure.SqlState is PostgresErrorCodes.InvalidAuthorizationSpecification or PostgresErrorCodes.InvalidPassword or PostgresErrorCodes.TooManyConnections or PostgresErrorCodes.CannotConnectNow)
        {
            throw new AiDatabaseUnavailableException("The read-only database is not available.", failure);
        }
        catch (PostgresException failure)
        {
            return SqlRunOutcome.Failed(SqlRunStatus.Error, clock.ElapsedMilliseconds, failure.SqlState);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return SqlRunOutcome.Failed(SqlRunStatus.Timeout, clock.ElapsedMilliseconds);
        }
        catch (Exception failure) when (failure is NpgsqlException or TimeoutException or InvalidOperationException)
        {
            throw new AiDatabaseUnavailableException("The read-only database is not available.", failure);
        }
        finally
        {
            if (connection is not null)
            {
                await RollbackAsync(connection);
            }
        }
    }

    private static async Task<SqlRunOutcome> FetchAsync(NpgsqlConnection connection, int maxRows, Stopwatch clock, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(string.Create(CultureInfo.InvariantCulture, $"FETCH {maxRows + 1} FROM xd_c"), connection);
        await using var reader = await command.ExecuteReaderAsync(System.Data.CommandBehavior.SequentialAccess, cancellationToken);
        var columns = new List<SqlColumn>(reader.FieldCount);
        for (var i = 0; i < reader.FieldCount; i++)
        {
            columns.Add(new SqlColumn(reader.GetName(i), reader.GetDataTypeName(i)));
        }

        var rows = new List<IReadOnlyList<object?>>();
        var truncated = false;
        while (await reader.ReadAsync(cancellationToken))
        {
            if (rows.Count == maxRows)
            {
                truncated = true;
                break;
            }

            var row = new object?[reader.FieldCount];
            for (var i = 0; i < row.Length; i++)
            {
                row[i] = await ReadCellAsync(reader, i, cancellationToken);
            }

            rows.Add(row);
        }

        return new SqlRunOutcome(SqlRunStatus.Ok, columns, rows, truncated, clock.ElapsedMilliseconds, null);
    }

    private static readonly HashSet<string> TextTypes = new(StringComparer.OrdinalIgnoreCase) { "text", "character varying", "varchar", "character", "bpchar", "name", "citext", "json", "jsonb" };

    /// <summary>
    /// One cell, bounded BEFORE it is held: a text cell is read through a text reader for at most the cell limit plus one character and the rest is skipped by the driver (sequential access), so <c>repeat('x', 900000000)</c> never becomes a string; a bytea cell is
    /// reported by its kind and not read. Other types are read whole (numbers, times, short arrays).
    /// </summary>
    private static async ValueTask<object?> ReadCellAsync(NpgsqlDataReader reader, int ordinal, CancellationToken cancellationToken)
    {
        if (await reader.IsDBNullAsync(ordinal, cancellationToken))
        {
            return null;
        }

        var type = reader.GetDataTypeName(ordinal);
        var bare = System.Text.RegularExpressions.Regex.Replace(type, @"\(.*?\)", string.Empty, System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1)).Trim();
        if (bare.Equals("bytea", StringComparison.OrdinalIgnoreCase))
        {
            return "[bytea]";
        }

        if (!TextTypes.Contains(bare))
        {
            return Normalize(reader, ordinal);
        }

        using var text = reader.GetTextReader(ordinal);
        var buffer = new char[XDrishti.Domain.Assistant.ToolLimits.MaxCellChars + 1];
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await text.ReadAsync(buffer.AsMemory(total), cancellationToken);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return new string(buffer, 0, total);
    }

    /// <summary>A value as plain data: numeric as text with its scale kept, times as ISO text (UTC where the type has a zone), arrays as lists, anything unusual as its text.</summary>
    internal static object? Normalize(NpgsqlDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        try
        {
            return Plain(reader.GetValue(ordinal));
        }
        catch (OverflowException)
        {
            return UnreadableNumber;
        }
    }

    internal static object? Plain(object? value) => value switch
    {
        null or DBNull => null,
        bool or long or string => value,
        short s => (long)s,
        int i => (long)i,
        byte b => (long)b,
        float f => (double)f,
        double d => d,
        decimal m => m.ToString(CultureInfo.InvariantCulture),
        Guid g => g.ToString(),
        DateTime { Kind: DateTimeKind.Utc } t => t.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", CultureInfo.InvariantCulture),
        DateTime t => t.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture),
        DateTimeOffset o => o.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", CultureInfo.InvariantCulture),
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        TimeOnly t => t.ToString("HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture),
        byte[] bytes => $"[bytea {bytes.Length} bytes]",
        Array array => ToList(array),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture),
    };

    private static List<object?> ToList(Array array)
    {
        var list = new List<object?>(array.Length);
        foreach (var item in array)
        {
            list.Add(Plain(item));
        }

        return list;
    }

    /// <summary>
    /// Declares the cursor, but only after the driver has parsed the text and counted ONE statement: Npgsql splits a text at semicolons into several statements, and a second one would run. Preparing only parses (nothing runs); a count other than one stops the call.
    /// </summary>
    private static async Task DeclareAsync(NpgsqlConnection connection, string statement, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("DECLARE xd_c NO SCROLL CURSOR FOR " + statement, connection);
        await command.PrepareAsync(cancellationToken);
#pragma warning disable CS0618 // Statements is the only public view of the parsed statements of a command
        var statements = command.Statements.Count;
#pragma warning restore CS0618
        if (statements != 1)
        {
            throw new PostgresException("one statement only", "ERROR", "ERROR", PostgresErrorCodes.SyntaxError);
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Ends the transaction whatever happened (also after a cancel); a failed rollback only means the connection is dropped from the pool.</summary>
    private static async Task RollbackAsync(NpgsqlConnection connection)
    {
        try
        {
            if (connection.State == System.Data.ConnectionState.Open)
            {
                using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await ExecuteAsync(connection, "ROLLBACK", stop.Token);
            }
        }
#pragma warning disable CA1031 // a rollback that fails leaves nothing to do: the connection is closed below and the server ends the transaction
        catch (Exception)
#pragma warning restore CA1031
        {
        }

        await connection.DisposeAsync();
    }

    private static string Short(string text) => text.Length <= 200 ? text : text[..200] + "…";
}
