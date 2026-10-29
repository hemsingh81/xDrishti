using System.Text.RegularExpressions;
using Npgsql;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain;

namespace XDrishti.Execution.Persistence;

/// <summary>
/// The persistent write-ahead (P12-02b), as the role xd_exec. <see cref="BeginAsync"/> inserts the leg row (unique client order id: a second begin of the same id throws, so one id is never sent twice) and the <c>begin</c> audit row in ONE
/// transaction that is committed before it returns; the sender sends only after that. The outcome and drop rows are inserts; the leg row is updated. Only codes are written: a reason is cut to a short code, a broker order id must be a plain token,
/// and the table refuses anything else. Also the source of the restart-proof counts (<see cref="BeginsSinceAsync"/>).
/// </summary>
internal sealed partial class PgSendJournal(NpgsqlDataSource data) : ISendJournal, ISendHistory
{
    private const string Insert = """
        INSERT INTO execution.broker_calls (account_id, intent_id, client_order_id, call, phase, environment, outcome, reason, broker_order_id)
        SELECT i.account_id, l.intent_id, l.client_order_id, 'orders_post', @phase, 'sandbox', @outcome, @reason, @broker_order_id
        FROM execution.legs l JOIN execution.intents i ON i.id = l.intent_id WHERE l.client_order_id = @cid
        """;

    public async Task BeginAsync(Guid accountId, long intentId, BrokerLeg leg, CancellationToken cancellationToken)
    {
        await using var connection = await data.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (var insertLeg = new NpgsqlCommand("""
            INSERT INTO execution.legs (intent_id, kind, client_order_id, status, qty, price, send_started_at)
            SELECT i.id, @kind, @cid, 'new', @qty, @price, now() FROM execution.intents i WHERE i.id = @intent AND i.account_id = @account
            """, connection, tx))
        {
            insertLeg.Parameters.AddWithValue("kind", KindChar(leg.Kind).ToString());
            insertLeg.Parameters.AddWithValue("cid", leg.ClientOrderId);
            insertLeg.Parameters.AddWithValue("qty", leg.Qty);
            insertLeg.Parameters.AddWithValue("price", leg.Price);
            insertLeg.Parameters.AddWithValue("intent", intentId);
            insertLeg.Parameters.AddWithValue("account", accountId);
            if (await insertLeg.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            {
                throw new InvalidOperationException("execution.intent_unknown");
            }
        }

        await AuditAsync(connection, tx, leg.ClientOrderId, "begin", null, null, null, cancellationToken).ConfigureAwait(false);
        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DroppedAsync(BrokerLeg leg, string reason, CancellationToken cancellationToken)
    {
        await using var connection = await data.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await UpdateLegAsync(connection, tx, leg.ClientOrderId, "cancelled", null, acked: false, cancellationToken).ConfigureAwait(false);
        await AuditAsync(connection, tx, leg.ClientOrderId, "dropped", null, Code(reason) ?? "dropped", null, cancellationToken).ConfigureAwait(false);
        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task OutcomeAsync(BrokerLeg leg, BrokerAck ack, CancellationToken cancellationToken)
    {
        var brokerId = ack.BrokerOrderId is { } id && PlainToken().IsMatch(id) ? id : null;
        var status = ack.Outcome switch
        {
            BrokerOutcome.Acknowledged => "transit",
            BrokerOutcome.NotSent => "rejected",
            _ => "sent",
        };
        await using var connection = await data.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await UpdateLegAsync(connection, tx, leg.ClientOrderId, status, brokerId, ack.Outcome == BrokerOutcome.Acknowledged, cancellationToken).ConfigureAwait(false);
        await AuditAsync(connection, tx, leg.ClientOrderId, "outcome", ack.Outcome switch
        {
            BrokerOutcome.Acknowledged => "acknowledged",
            BrokerOutcome.NotSent => "not_sent",
            _ => "unknown",
        }, Code(ack.Reason), brokerId, cancellationToken).ConfigureAwait(false);
        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DateTimeOffset>> BeginsSinceAsync(DateTimeOffset since, CancellationToken cancellationToken)
    {
        await using var command = data.CreateCommand("SELECT at FROM execution.broker_calls WHERE phase = 'begin' AND at >= @since ORDER BY at");
        command.Parameters.AddWithValue("since", since);
        List<DateTimeOffset> times = [];
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            times.Add(reader.GetFieldValue<DateTimeOffset>(0));
        }

        return times;
    }

    private static async Task UpdateLegAsync(NpgsqlConnection connection, NpgsqlTransaction tx, string clientOrderId, string status, string? brokerId, bool acked, CancellationToken cancellationToken)
    {
        await using var update = new NpgsqlCommand("""
            UPDATE execution.legs SET status = @status, broker_order_id = COALESCE(@broker_order_id, broker_order_id), acked_at = CASE WHEN @acked THEN now() ELSE acked_at END, last_update_at = now()
            WHERE client_order_id = @cid
            """, connection, tx);
        update.Parameters.AddWithValue("status", status);
        update.Parameters.Add(new NpgsqlParameter("broker_order_id", NpgsqlTypes.NpgsqlDbType.Varchar) { Value = (object?)brokerId ?? DBNull.Value });
        update.Parameters.AddWithValue("acked", acked);
        update.Parameters.AddWithValue("cid", clientOrderId);
        if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
        {
            throw new InvalidOperationException("execution.leg_unknown");
        }
    }

    private static async Task AuditAsync(NpgsqlConnection connection, NpgsqlTransaction tx, string clientOrderId, string phase, string? outcome, string? reason, string? brokerId, CancellationToken cancellationToken)
    {
        await using var audit = new NpgsqlCommand(Insert, connection, tx);
        audit.Parameters.AddWithValue("phase", phase);
        audit.Parameters.Add(new NpgsqlParameter("outcome", NpgsqlTypes.NpgsqlDbType.Varchar) { Value = (object?)outcome ?? DBNull.Value });
        audit.Parameters.Add(new NpgsqlParameter("reason", NpgsqlTypes.NpgsqlDbType.Varchar) { Value = (object?)reason ?? DBNull.Value });
        audit.Parameters.Add(new NpgsqlParameter("broker_order_id", NpgsqlTypes.NpgsqlDbType.Varchar) { Value = (object?)brokerId ?? DBNull.Value });
        audit.Parameters.AddWithValue("cid", clientOrderId);
        if (await audit.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
        {
            throw new InvalidOperationException("execution.leg_unknown");
        }
    }

    private static char KindChar(LegKind kind) => kind switch
    {
        LegKind.Entry => 'E',
        LegKind.Stop => 'S',
        LegKind.Target => 'T',
        LegKind.Exit => 'X',
        _ => 'F',
    };

    /// <summary>A reason as a stored code: the lower-cased text when it already is a short code (letters, digits, underscore, dot, colon, at most 64), else the fixed code <c>reason_invalid</c> (free text is never stored, not even cut); null when there is none.</summary>
    internal static string? Code(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return null;
        }

        var code = reason.ToLowerInvariant();
        return ShortCode().IsMatch(code) ? code : "reason_invalid";
    }

    [GeneratedRegex("^[a-z0-9_.:]{1,64}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ShortCode();

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex PlainToken();
}
