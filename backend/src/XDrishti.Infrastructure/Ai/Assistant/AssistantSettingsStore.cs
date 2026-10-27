using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Domain.Assistant;

namespace XDrishti.Infrastructure.Ai.Assistant;

/// <summary>
/// The assistant's switches behind <see cref="IAssistantSettingsStore"/> (spec P9-03, ADR 0137): raw SQL, insert and select only on <c>ai.settings</c>. A change is a new row; the newest (the highest id, drawn by the insert trigger under the exclusive lock) is current. The id, the time and the hash are stamped by the
/// trigger. The check of the newest row and the insert share the lock the trigger takes, so two changes cannot interleave. No row means off.
/// </summary>
internal sealed class AssistantSettingsStore(NpgsqlDataSource dataSource) : IAssistantSettingsStore
{
    public async ValueTask<AssistantSettings> CurrentAsync(CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand("SELECT id, enabled, portfolio_context, max_tool_calls, allow_draft_confirm, auto_lab_quick_test, allow_proposal_accept, auto_accept_hypothesis FROM ai.settings ORDER BY id DESC LIMIT 1");
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : AssistantSettings.Defaults;
    }

    public async ValueTask<AssistantSettings?> AppendAsync(AssistantSettingsChange change, long? expectedCurrentId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var advisory = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended('ai.settings', 0))", connection, transaction))
        {
            await advisory.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var newest = new NpgsqlCommand("SELECT id FROM ai.settings ORDER BY id DESC LIMIT 1", connection, transaction))
        {
            long? current = await newest.ExecuteScalarAsync(cancellationToken) is long id ? id : null;
            if (current != expectedCurrentId)
            {
                return null;
            }
        }

        await using var insert = new NpgsqlCommand(
            "INSERT INTO ai.settings (enabled, portfolio_context, max_tool_calls, changed_by, reason, allow_draft_confirm, auto_lab_quick_test, allow_proposal_accept, auto_accept_hypothesis) VALUES (@enabled, @portfolio, @max, @actor, @reason, @draft, @auto, @proposal, @hypothesis) RETURNING id, enabled, portfolio_context, max_tool_calls, allow_draft_confirm, auto_lab_quick_test, allow_proposal_accept, auto_accept_hypothesis", connection, transaction);
        insert.Parameters.AddWithValue("enabled", NpgsqlDbType.Boolean, change.Enabled);
        insert.Parameters.AddWithValue("portfolio", NpgsqlDbType.Boolean, change.PortfolioContext);
        insert.Parameters.AddWithValue("max", NpgsqlDbType.Integer, change.MaxToolCalls);
        insert.Parameters.AddWithValue("actor", NpgsqlDbType.Varchar, change.Actor);
        insert.Parameters.AddWithValue("reason", NpgsqlDbType.Varchar, change.Reason);
        insert.Parameters.AddWithValue("draft", NpgsqlDbType.Boolean, change.AllowDraftConfirm);
        insert.Parameters.AddWithValue("auto", NpgsqlDbType.Boolean, change.AutoLabQuickTest);
        insert.Parameters.AddWithValue("proposal", NpgsqlDbType.Boolean, change.AllowProposalAccept);
        insert.Parameters.AddWithValue("hypothesis", NpgsqlDbType.Boolean, change.AutoAcceptHypothesis);
        AssistantSettings row;
        await using (var reader = await insert.ExecuteReaderAsync(cancellationToken))
        {
            await reader.ReadAsync(cancellationToken);
            row = Read(reader);
        }

        await transaction.CommitAsync(cancellationToken);
        return row;
    }

    private static AssistantSettings Read(NpgsqlDataReader reader) => new(reader.GetInt64(0), reader.GetBoolean(1), reader.GetBoolean(2), reader.GetInt32(3), reader.GetBoolean(4), reader.GetBoolean(5), reader.GetBoolean(6), reader.GetBoolean(7));
}
