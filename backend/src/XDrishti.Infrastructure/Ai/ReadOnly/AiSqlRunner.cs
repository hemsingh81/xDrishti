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
            await ExecuteAsync(connection, "DECLARE xd_c NO SCROLL CURSOR FOR " + statement, guard.Token);
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
            return SqlRunOutcome.Failed(SqlRunStatus.Error, clock.ElapsedMilliseconds, Short(failure.MessageText));
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
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
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
                row[i] = Normalize(reader, i);
            }

            rows.Add(row);
        }

        return new SqlRunOutcome(SqlRunStatus.Ok, columns, rows, truncated, clock.ElapsedMilliseconds, null);
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
