using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Research;

namespace XDrishti.Infrastructure.Research;

/// <summary>The write path of <see cref="RunStore"/> (spec P3-10, D8): one transaction, one insert of the header, one binary copy of the trades, a count check inside the transaction, idempotent by run key.</summary>
internal sealed partial class RunStore
{
    private const string InsertRun = """
        INSERT INTO research.sim_runs (run_key, engine_version, record_schema_version, trade_schema_version, strategy_id, definition_sha256, from_date, to_date, universe_kind, universe_label,
            instruments, instruments_run, trades_in_window, candidates_in_window, trade_count, open_at_end_count, acknowledge_csv_adjusted, fills, costs_sheet_id, costs_rates_sha256,
            record_json, record_sha256, trades_sha256, created_at, created_by)
        VALUES (@run_key, @engine_version, @record_schema_version, @trade_schema_version, @strategy_id, @definition_sha256, @from_date, @to_date, @universe_kind, @universe_label,
            @instruments, @instruments_run, @trades_in_window, @candidates_in_window, @trade_count, @open_at_end_count, @acknowledge_csv_adjusted, @fills, @costs_sheet_id, @costs_rates_sha256,
            @record_json, @record_sha256, @trades_sha256, @created_at, @created_by)
        ON CONFLICT (run_key) DO NOTHING
        RETURNING id
        """;

    private const string SelectStored = "SELECT id, record_sha256, trades_sha256, trade_count, open_at_end_count, engine_version, record_schema_version, trade_schema_version FROM research.sim_runs WHERE run_key = @run_key";

    private static readonly string CopySql = "COPY research.sim_trades (" + string.Join(", ", [TradeColumns.RunId, TradeColumns.OpenAtEnd, TradeColumns.Ordinal, TradeColumns.TradeKey, .. TradeColumns.Columns.Select(c => c.Name)]) + ") FROM STDIN (FORMAT BINARY)";

    public async ValueTask<Result<StoreOutcome>> StoreAsync(RunToStore run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            long? id;
            await using (var insert = new NpgsqlCommand(InsertRun, connection, transaction))
            {
                BindHeader(insert, run);
                id = await insert.ExecuteScalarAsync(cancellationToken) as long?;
            }

            if (id is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return await CompareWithStoredAsync(connection, run, cancellationToken);
            }

            var copyStart = time.GetTimestamp();
            await CopyTradesAsync(connection, id.Value, run.Run, cancellationToken);
            var copyMilliseconds = (long)time.GetElapsedTime(copyStart).TotalMilliseconds;

            await using (var count = new NpgsqlCommand("SELECT count(*) FROM research.sim_trades WHERE run_id = @id", connection, transaction))
            {
                count.Parameters.AddWithValue("id", NpgsqlDbType.Bigint, id.Value);
                var stored = (long)(await count.ExecuteScalarAsync(cancellationToken))!;
                var expected = (long)run.Run.Trades.Count + run.Run.OpenAtEnd.Count;
                if (stored != expected)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return Result.Failure<StoreOutcome>(StoreErrors.WriteFailed($"The trade rows written ({stored}) differ from the trades of the run ({expected}); nothing was stored."));
                }
            }

            await transaction.CommitAsync(cancellationToken);
            return Result.Success(StoreOutcome.Stored(id.Value, copyMilliseconds));
        }
        catch (PostgresException failure)
        {
            return Result.Failure<StoreOutcome>(StoreErrors.WriteFailed(Describe(failure)));
        }
        catch (NpgsqlException failure) when (!cancellationToken.IsCancellationRequested)
        {
            return Result.Failure<StoreOutcome>(StoreErrors.WriteFailed("The database could not be used: " + failure.GetType().Name + "; nothing was stored."));
        }
    }

    /// <summary>The text of a database failure for the owner: the state and the constraint or relation name, never a value of the run. A missing table names the migration that creates it.</summary>
    private static string Describe(PostgresException failure) =>
        failure.SqlState == PostgresErrorCodes.UndefinedTable || failure.SqlState == PostgresErrorCodes.InvalidSchemaName
            ? "The result store tables do not exist: the migration AddResearchResultStore has not been applied (deploy/xd-up.sh applies it). Nothing was stored."
            : $"The database refused the write (SQLSTATE {failure.SqlState}{(failure.ConstraintName is { } name ? ", constraint " + name : string.Empty)}); nothing was stored.";

    private static void BindHeader(NpgsqlCommand command, RunToStore store)
    {
        var run = store.Run;
        var p = command.Parameters;
        p.AddWithValue("run_key", NpgsqlDbType.Char, run.RunKey);
        p.AddWithValue("engine_version", NpgsqlDbType.Varchar, run.EngineVersion);
        p.AddWithValue("record_schema_version", NpgsqlDbType.Smallint, (short)run.SchemaVersion);
        p.AddWithValue("trade_schema_version", NpgsqlDbType.Smallint, (short)SimTrade.CurrentSchemaVersion);
        p.AddWithValue("strategy_id", NpgsqlDbType.Varchar, run.Strategy.Id);
        p.AddWithValue("definition_sha256", NpgsqlDbType.Char, run.Strategy.DefinitionSha256);
        p.AddWithValue("from_date", NpgsqlDbType.Date, run.From);
        p.AddWithValue("to_date", NpgsqlDbType.Date, run.To);
        p.AddWithValue("universe_kind", NpgsqlDbType.Varchar, EnumText<UniverseKind>.Name(run.Universe.Kind));
        p.AddWithValue("universe_label", NpgsqlDbType.Varchar, run.Universe.Label);
        p.AddWithValue("instruments", NpgsqlDbType.Integer, run.Totals.Instruments);
        p.AddWithValue("instruments_run", NpgsqlDbType.Integer, run.Totals.InstrumentsRun);
        p.AddWithValue("trades_in_window", NpgsqlDbType.Bigint, run.Totals.TradesInWindow);
        p.AddWithValue("candidates_in_window", NpgsqlDbType.Bigint, run.Totals.CandidatesInWindow);
        p.AddWithValue("trade_count", NpgsqlDbType.Integer, run.Trades.Count);
        p.AddWithValue("open_at_end_count", NpgsqlDbType.Integer, run.OpenAtEnd.Count);
        p.AddWithValue("acknowledge_csv_adjusted", NpgsqlDbType.Boolean, run.AcknowledgeCsvAdjusted);
        p.AddWithValue("fills", NpgsqlDbType.Varchar, run.Fills);
        p.AddWithValue("costs_sheet_id", NpgsqlDbType.Varchar, (object?)run.Costs?.SheetId ?? DBNull.Value);
        p.AddWithValue("costs_rates_sha256", NpgsqlDbType.Char, (object?)run.Costs?.RatesSha256 ?? DBNull.Value);
        p.AddWithValue("record_json", NpgsqlDbType.Text, store.Record.Json);
        p.AddWithValue("record_sha256", NpgsqlDbType.Char, store.Record.RecordSha256);
        p.AddWithValue("trades_sha256", NpgsqlDbType.Char, store.Record.TradesSha256);
        p.AddWithValue("created_at", NpgsqlDbType.TimestampTz, store.CreatedAt.ToUniversalTime());
        p.AddWithValue("created_by", NpgsqlDbType.Varchar, store.Actor);
    }

    /// <summary>Streams the trades of both lists, in the record's order, from the lists the record already holds; nothing is copied into a second collection.</summary>
    private static async Task CopyTradesAsync(NpgsqlConnection connection, long runId, SimulationRun run, CancellationToken cancellationToken)
    {
        await using var writer = await connection.BeginBinaryImportAsync(CopySql, cancellationToken);
        var columns = TradeColumns.Columns;
        foreach (var (trades, openAtEnd) in new[] { (run.Trades, false), (run.OpenAtEnd, true) })
        {
            for (var ordinal = 0; ordinal < trades.Count; ordinal++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var trade = trades[ordinal];
                await writer.StartRowAsync(cancellationToken);
                await writer.WriteAsync(runId, NpgsqlDbType.Bigint, cancellationToken);
                await writer.WriteAsync(openAtEnd, NpgsqlDbType.Boolean, cancellationToken);
                await writer.WriteAsync(ordinal, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(trade.Trade.Key, NpgsqlDbType.Varchar, cancellationToken);
                foreach (var column in columns)
                {
                    var value = column.Get(trade);
                    if (value is null)
                    {
                        await writer.WriteNullAsync(cancellationToken);
                    }
                    else
                    {
                        await writer.WriteAsync(value, column.DbType, cancellationToken);
                    }
                }
            }
        }

        await writer.CompleteAsync(cancellationToken);
    }

    /// <summary>The key is taken: the same run is a no-op, another record under it is a conflict (nothing is written either way).</summary>
    private static async Task<Result<StoreOutcome>> CompareWithStoredAsync(NpgsqlConnection connection, RunToStore run, CancellationToken cancellationToken)
    {
        await using var select = new NpgsqlCommand(SelectStored, connection);
        select.Parameters.AddWithValue("run_key", NpgsqlDbType.Char, run.Run.RunKey);
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            // the row that blocked the insert is gone (removed by the owner between the two statements): the caller can store again
            return Result.Failure<StoreOutcome>(StoreErrors.WriteFailed("The run key was taken and then released while it was stored; store it again."));
        }

        var differing = new List<string>();
        if (!string.Equals(reader.GetString(1), run.Record.RecordSha256, StringComparison.Ordinal))
        {
            differing.Add("record");
        }

        if (!string.Equals(reader.GetString(2), run.Record.TradesSha256, StringComparison.Ordinal))
        {
            differing.Add("trades");
        }

        if (reader.GetInt32(3) != run.Run.Trades.Count)
        {
            differing.Add("trade_count");
        }

        if (reader.GetInt32(4) != run.Run.OpenAtEnd.Count)
        {
            differing.Add("open_at_end_count");
        }

        if (!string.Equals(reader.GetString(5), run.Run.EngineVersion, StringComparison.Ordinal))
        {
            differing.Add("engine_version");
        }

        if (reader.GetInt16(6) != run.Run.SchemaVersion || reader.GetInt16(7) != SimTrade.CurrentSchemaVersion)
        {
            differing.Add("schema_version");
        }

        return differing.Count == 0
            ? Result.Success(StoreOutcome.AlreadyStored(reader.GetInt64(0)))
            : Result.Failure<StoreOutcome>(StoreErrors.RunConflict(run.Run.RunKey, differing));
    }

}
