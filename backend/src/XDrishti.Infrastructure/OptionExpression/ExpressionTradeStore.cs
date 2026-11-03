using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.OptionExpression;
using XDrishti.Domain.Options.Remark;

namespace XDrishti.Infrastructure.OptionExpression;

/// <summary><c>research.expression_runs</c> and <c>research.expression_trades</c> behind <see cref="IExpressionTradeStore"/> (P14-08, ADR 0197): raw SQL, insert and select only (the runtime role has no more). The run id is the key; a second insert writes nothing.</summary>
internal sealed class ExpressionTradeStore(NpgsqlDataSource dataSource) : IExpressionTradeStore
{
    public async ValueTask<bool> ExistsAsync(string runId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT 1 FROM research.expression_runs WHERE run_id = @id");
        command.Parameters.AddWithValue("id", NpgsqlDbType.Char, runId ?? string.Empty);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    public async ValueTask<bool> AppendAsync(ExpressionRunRow run, IReadOnlyList<RemarkRow> rows, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count != run.Trades)
        {
            throw new InvalidOperationException("The rows of an expression run must be as many as its trades.");
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var head = new NpgsqlCommand(
            """
            INSERT INTO research.expression_runs (run_id, cash_run_key, profile_hash, remark_version, inputs, trades, paired, unpaired, bs_fallback_share, labels)
            VALUES (@id, @cash, @profile, @version, @inputs, @trades, @paired, @unpaired, @share, @labels)
            ON CONFLICT (run_id) DO NOTHING
            """, connection, transaction))
        {
            var p = head.Parameters;
            p.AddWithValue("id", NpgsqlDbType.Char, run.RunId);
            p.AddWithValue("cash", NpgsqlDbType.Varchar, run.CashRunKey);
            p.AddWithValue("profile", NpgsqlDbType.Char, run.ProfileHash);
            p.AddWithValue("version", NpgsqlDbType.Varchar, run.RemarkVersion);
            p.AddWithValue("inputs", NpgsqlDbType.Jsonb, run.InputsJson);
            p.AddWithValue("trades", NpgsqlDbType.Integer, run.Trades);
            p.AddWithValue("paired", NpgsqlDbType.Integer, run.Paired);
            p.AddWithValue("unpaired", NpgsqlDbType.Jsonb, run.UnpairedJson);
            p.Add(new NpgsqlParameter("share", NpgsqlDbType.Double) { Value = (object?)run.BsFallbackShare ?? DBNull.Value });
            p.AddWithValue("labels", NpgsqlDbType.Array | NpgsqlDbType.Text, run.Labels.ToArray());
            if (await head.ExecuteNonQueryAsync(cancellationToken) == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }
        }

        foreach (var row in rows)
        {
            await using var command = new NpgsqlCommand(
                """
                INSERT INTO research.expression_trades (run_id, trade_key, fate, side, expiry_flag, expiry_code, expiry, expiry_source, strike_px, entry_ts, exit_ts, entry_premium_px, exit_premium_px, units, gross_inr, net_inr, r_equiv, cash_r, exit_reason, lot, sizable, labels)
                VALUES (@run, @key, @fate, @side, @flag, @code, @expiry, @source, @strike, @entry, @exit, @entryPx, @exitPx, @units, @gross, @net, @r, @cashr, @reason, @lot, @sizable, @labels)
                ON CONFLICT (run_id, trade_key) DO NOTHING
                """, connection, transaction);
            var p = command.Parameters;
            p.AddWithValue("run", NpgsqlDbType.Char, run.RunId);
            p.AddWithValue("key", NpgsqlDbType.Varchar, row.TradeKey);
            p.AddWithValue("fate", NpgsqlDbType.Varchar, row.Fate);
            p.AddWithValue("side", NpgsqlDbType.Char, row.OptionSide);
            p.Add(new NpgsqlParameter("flag", NpgsqlDbType.Char) { Value = row.ExpiryFlag is { } f ? f.ToString() : DBNull.Value });
            Nullable<short>(p, "code", NpgsqlDbType.Smallint, row.ExpiryCode is { } c ? (short)c : null);
            Nullable(p, "expiry", NpgsqlDbType.Date, row.Expiry);
            p.AddWithValue("source", NpgsqlDbType.Varchar, row.ExpirySource);
            Nullable(p, "strike", NpgsqlDbType.Integer, row.StrikePx);
            p.AddWithValue("entry", NpgsqlDbType.TimestampTz, row.EntryTs.ToUniversalTime());
            p.AddWithValue("exit", NpgsqlDbType.TimestampTz, row.ExitTs.ToUniversalTime());
            Nullable(p, "entryPx", NpgsqlDbType.Integer, row.EntryPremiumPx);
            Nullable(p, "exitPx", NpgsqlDbType.Integer, row.ExitPremiumPx);
            Nullable(p, "units", NpgsqlDbType.Double, row.Units);
            Nullable(p, "gross", NpgsqlDbType.Numeric, row.GrossInr);
            Nullable(p, "net", NpgsqlDbType.Numeric, row.NetInr);
            Nullable(p, "r", NpgsqlDbType.Double, row.REquiv);
            Nullable(p, "cashr", NpgsqlDbType.Double, row.CashR);
            p.AddWithValue("reason", NpgsqlDbType.Varchar, row.ExitReason);
            Nullable(p, "lot", NpgsqlDbType.Integer, row.Lot);
            p.AddWithValue("sizable", NpgsqlDbType.Varchar, row.Sizable);
            p.AddWithValue("labels", NpgsqlDbType.Array | NpgsqlDbType.Text, row.Labels.ToArray());
            if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new InvalidOperationException("An expression trade row was not written.");
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private static void Nullable<T>(NpgsqlParameterCollection parameters, string name, NpgsqlDbType type, T? value)
        where T : struct => parameters.Add(new NpgsqlParameter(name, type) { Value = value is { } v ? v : DBNull.Value });
}
