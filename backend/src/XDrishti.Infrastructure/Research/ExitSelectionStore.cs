using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Common;

namespace XDrishti.Infrastructure.Research;

/// <summary>
/// The exit choices of the result store (spec P4-06, D10): one transaction on one connection that finds the stored comparison, inserts the header that does nothing on a stored selection key and then the fold rows. Insert-only; a choice whose
/// comparison is not stored is refused, and a stored key with another record is a conflict. Never logs a record or a trade.
/// </summary>
internal sealed class ExitSelectionStore(NpgsqlDataSource dataSource) : IExitSelectionStore
{
    private const string InsertSelection = """
        INSERT INTO research.exit_selections (selection_key, comparison_id, strategy_id, timeframe, side, style, regime, chosen_candidate_id, stable, record_json, record_sha256, created_at, created_by)
        VALUES (@selection_key, @comparison_id, @strategy_id, @timeframe, @side, @style, @regime, @chosen, @stable, @record_json, @record_sha256, @created_at, @created_by)
        ON CONFLICT (selection_key) DO NOTHING
        RETURNING id
        """;

    private const string InsertFold = """
        INSERT INTO research.exit_selection_folds (selection_id, fold_index, candidate_id, test_start, test_end, qualified, train_trades, train_score, test_trades, test_score, is_winner)
        VALUES (@selection_id, @fold_index, @candidate_id, @test_start, @test_end, @qualified, @train_trades, @train_score, @test_trades, @test_score, @is_winner)
        """;

    public async ValueTask<Result<StoreOutcome>> StoreAsync(ExitSelectionToStore selection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selection);
        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            long? comparisonId;
            await using (var find = new NpgsqlCommand("SELECT id FROM research.sim_comparisons WHERE comparison_key = @key", connection, transaction))
            {
                find.Parameters.AddWithValue("key", NpgsqlDbType.Char, selection.ComparisonKey);
                comparisonId = await find.ExecuteScalarAsync(cancellationToken) as long?;
            }

            if (comparisonId is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Result.Failure<StoreOutcome>(StoreErrors.WriteFailed("The comparison the choice was made from is not stored; store the comparison first (--store). Nothing was stored."));
            }

            long? id;
            await using (var insert = new NpgsqlCommand(InsertSelection, connection, transaction))
            {
                var p = insert.Parameters;
                p.AddWithValue("selection_key", NpgsqlDbType.Char, selection.SelectionKey);
                p.AddWithValue("comparison_id", NpgsqlDbType.Bigint, comparisonId.Value);
                p.AddWithValue("strategy_id", NpgsqlDbType.Varchar, selection.StrategyId);
                p.AddWithValue("timeframe", NpgsqlDbType.Varchar, selection.Timeframe);
                p.AddWithValue("side", NpgsqlDbType.Varchar, selection.Side);
                p.AddWithValue("style", NpgsqlDbType.Varchar, selection.Style);
                p.AddWithValue("regime", NpgsqlDbType.Varchar, selection.Regime);
                p.AddWithValue("chosen", NpgsqlDbType.Varchar, (object?)selection.ChosenCandidateId ?? DBNull.Value);
                p.AddWithValue("stable", NpgsqlDbType.Boolean, (object?)selection.Stable ?? DBNull.Value);
                p.AddWithValue("record_json", NpgsqlDbType.Text, selection.RecordJson);
                p.AddWithValue("record_sha256", NpgsqlDbType.Char, selection.RecordSha256);
                p.AddWithValue("created_at", NpgsqlDbType.TimestampTz, selection.CreatedAt.ToUniversalTime());
                p.AddWithValue("created_by", NpgsqlDbType.Varchar, selection.Actor);
                id = await insert.ExecuteScalarAsync(cancellationToken) as long?;
            }

            if (id is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return await CompareAsync(connection, selection, cancellationToken);
            }

            foreach (var fold in selection.Folds)
            {
                await using var row = new NpgsqlCommand(InsertFold, connection, transaction);
                var p = row.Parameters;
                p.AddWithValue("selection_id", NpgsqlDbType.Bigint, id.Value);
                p.AddWithValue("fold_index", NpgsqlDbType.Integer, fold.FoldIndex);
                p.AddWithValue("candidate_id", NpgsqlDbType.Varchar, fold.CandidateId);
                p.AddWithValue("test_start", NpgsqlDbType.Date, fold.TestStart);
                p.AddWithValue("test_end", NpgsqlDbType.Date, fold.TestEnd);
                p.AddWithValue("qualified", NpgsqlDbType.Boolean, fold.Qualified);
                p.AddWithValue("train_trades", NpgsqlDbType.Integer, fold.TrainTrades);
                p.AddWithValue("train_score", NpgsqlDbType.Double, (object?)fold.TrainScore ?? DBNull.Value);
                p.AddWithValue("test_trades", NpgsqlDbType.Integer, fold.TestTrades);
                p.AddWithValue("test_score", NpgsqlDbType.Double, (object?)fold.TestScore ?? DBNull.Value);
                p.AddWithValue("is_winner", NpgsqlDbType.Boolean, fold.IsWinner);
                await row.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return Result.Success(StoreOutcome.Stored(id.Value, 0));
        }
        catch (PostgresException failure)
        {
            return Result.Failure<StoreOutcome>(StoreErrors.WriteFailed("The exit choice could not be stored (" + failure.SqlState + "); nothing was stored."));
        }
        catch (NpgsqlException failure) when (!cancellationToken.IsCancellationRequested)
        {
            return Result.Failure<StoreOutcome>(StoreErrors.WriteFailed("The database could not be used: " + failure.GetType().Name + "; nothing was stored."));
        }
    }

    /// <summary>The key is taken: the same record is a no-op, another record under it is a conflict (nothing is written either way).</summary>
    private static async Task<Result<StoreOutcome>> CompareAsync(NpgsqlConnection connection, ExitSelectionToStore selection, CancellationToken cancellationToken)
    {
        await using var select = new NpgsqlCommand("SELECT id, record_sha256, chosen_candidate_id FROM research.exit_selections WHERE selection_key = @key", connection);
        select.Parameters.AddWithValue("key", NpgsqlDbType.Char, selection.SelectionKey);
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return Result.Failure<StoreOutcome>(StoreErrors.WriteFailed("The selection key was taken and then released while it was stored; store it again."));
        }

        var differing = new List<string>();
        if (!string.Equals(reader.GetString(1), selection.RecordSha256, StringComparison.Ordinal))
        {
            differing.Add("record");
        }

        if (!string.Equals(reader.IsDBNull(2) ? null : reader.GetString(2), selection.ChosenCandidateId, StringComparison.Ordinal))
        {
            differing.Add("chosen_candidate");
        }

        return differing.Count == 0
            ? Result.Success(StoreOutcome.AlreadyStored(reader.GetInt64(0)))
            : Result.Failure<StoreOutcome>(StoreErrors.RunConflict(selection.SelectionKey, differing));
    }
}
