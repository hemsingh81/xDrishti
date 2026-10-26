using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.ChargeSheet;
using XDrishti.Domain.Common;
using XDrishti.Domain.Reporting;

namespace XDrishti.Infrastructure.Reporting;

/// <summary>
/// The charge-sheet confirmation log behind its two ports (spec P8-23, ADR 0127): <c>workflow.charge_sheet_events</c>, raw SQL, select and insert only. An act is a new row; the newest confirm-or-revoke row is what stands and the newest capability row says whether confirming
/// is allowed (no row: off). The append takes the lock the insert trigger takes, re-reads, and writes nothing when the log already says the same (so two confirmations at once make one row). The database stamps the id, the time and the India date of a confirmation, and
/// refuses a confirmation while the capability is off.
/// </summary>
internal sealed class ChargeSheetEventStore(NpgsqlDataSource dataSource) : IChargeSheetLog, IChargeSheetLogWriter
{
    private const string Columns = "id, kind, sheet_id, rates_sha256, confirmed_on, actor, reason, created_at";

    public async ValueTask<ChargeSheetLedger> ReadAsync(int historyLimit, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var (gate, newest) = await ReadStateAsync(connection, null, cancellationToken);
        var history = new List<ChargeSheetEntry>();
        if (historyLimit > 0)
        {
            await using var select = new NpgsqlCommand($"SELECT {Columns} FROM workflow.charge_sheet_events ORDER BY id DESC LIMIT @limit", connection);
            select.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, Math.Clamp(historyLimit, 1, 200));
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                history.Add(Read(reader));
            }
        }

        return new ChargeSheetLedger(gate, newest, history);
    }

    public async ValueTask<Result<ChargeSheetAppend>> AppendAsync(ChargeSheetAct act, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(act);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // The same lock the insert trigger takes, taken first so the reads below and the insert cannot be interleaved with another act.
        await using (var advisory = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended('workflow.charge_sheet_events', 0))", connection, transaction))
        {
            await advisory.ExecuteNonQueryAsync(cancellationToken);
        }

        var (gateOn, newest) = await ReadStateAsync(connection, transaction, cancellationToken);
        var sheetId = act.SheetId;
        var rates = act.RatesSha256;
        switch (act.Kind)
        {
            case ChargeSheetActKind.GateOn when gateOn:
            case ChargeSheetActKind.GateOff when !gateOn:
                return Result.Success(new ChargeSheetAppend(null, Created: false)); // already so
            case ChargeSheetActKind.Confirm when !gateOn:
                return Result.Failure<ChargeSheetAppend>(Error.Forbidden("chargesheet.capability_off", "Confirming a charge sheet is switched off. Switch it on first (it is off by default)."));
            case ChargeSheetActKind.Confirm when newest is { Kind: ChargeSheetActKind.Confirm } same && same.SheetId == sheetId && same.RatesSha256 == rates:
                return Result.Success(new ChargeSheetAppend(same, Created: false)); // the same confirmation again: idempotent
            case ChargeSheetActKind.Revoke when newest is not { Kind: ChargeSheetActKind.Confirm }:
                return Result.Success(new ChargeSheetAppend(newest, Created: false)); // nothing stands: nothing to revoke
            case ChargeSheetActKind.Revoke:
                sheetId = newest!.SheetId; // a revoke names what it withdraws
                rates = newest.RatesSha256;
                break;
            default:
                break;
        }

        await using var insert = new NpgsqlCommand($"""
            INSERT INTO workflow.charge_sheet_events (kind, sheet_id, rates_sha256, actor, reason)
            VALUES (@kind, @sheet, @rates, @actor, @reason) RETURNING {Columns}
            """, connection, transaction);
        var p = insert.Parameters;
        p.AddWithValue("kind", NpgsqlDbType.Varchar, ChargeSheetActKinds.Word(act.Kind));
        p.AddWithValue("sheet", NpgsqlDbType.Varchar, (object?)sheetId ?? DBNull.Value);
        p.AddWithValue("rates", NpgsqlDbType.Varchar, (object?)rates ?? DBNull.Value);
        p.AddWithValue("actor", NpgsqlDbType.Varchar, act.Actor);
        p.AddWithValue("reason", NpgsqlDbType.Varchar, act.Reason);
        ChargeSheetEntry row;
        await using (var reader = await insert.ExecuteReaderAsync(cancellationToken))
        {
            await reader.ReadAsync(cancellationToken);
            row = Read(reader);
        }

        await transaction.CommitAsync(cancellationToken);
        return Result.Success(new ChargeSheetAppend(row, Created: true));
    }

    private static async Task<(bool GateOn, ChargeSheetEntry? Newest)> ReadStateAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, CancellationToken cancellationToken)
    {
        bool gateOn;
        await using (var gate = new NpgsqlCommand("SELECT kind FROM workflow.charge_sheet_events WHERE kind IN ('gate_on', 'gate_off') ORDER BY id DESC LIMIT 1", connection, transaction))
        {
            gateOn = await gate.ExecuteScalarAsync(cancellationToken) is "gate_on";
        }

        ChargeSheetEntry? newest = null;
        await using var select = new NpgsqlCommand($"SELECT {Columns} FROM workflow.charge_sheet_events WHERE kind IN ('confirm', 'revoke') ORDER BY id DESC LIMIT 1", connection, transaction);
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            newest = Read(reader);
        }

        return (gateOn, newest);
    }

    private static ChargeSheetEntry Read(NpgsqlDataReader reader) =>
        new(
            reader.GetInt64(0),
            ChargeSheetActKinds.Parse(reader.GetString(1)) ?? throw new InvalidOperationException("The log holds an act this build does not know."),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : DateOnly.FromDateTime(reader.GetFieldValue<DateTime>(4)),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetFieldValue<DateTimeOffset>(7));
}
