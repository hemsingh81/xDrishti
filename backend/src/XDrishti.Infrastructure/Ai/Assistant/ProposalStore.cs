using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Ai;

namespace XDrishti.Infrastructure.Ai.Assistant;

/// <summary>
/// <c>workflow.assistant_proposals</c> and <c>workflow.assistant_proposal_decisions</c> behind the three proposal ports (spec P9-08 AC 8, 11, 12): raw SQL, insert and select only. A proposal is written once with its expiry; a proposal has at most one decision (unique). The job holds only
/// <see cref="IProposalInbox"/> and <see cref="IProposalReader"/>; <see cref="IProposalDecisions"/> is for the accept, reject and auto handlers.
/// </summary>
internal sealed class ProposalStore(NpgsqlDataSource dataSource) : IProposalInbox, IProposalReader, IProposalDecisions
{
    private const string Columns = """
        p.id, p.kind, p.dedupe_key, p.created_on, p.subject, p.payload::text, p.reason, p.evidence_json::text, p.checks_json::text, p.pack_hash, p.model_id, p.prompt_version, p.created_by, p.created_at, p.expires_at,
        d.decision, d.actor, d.auto, d.reason, d.result_ref, d.decided_at
        """;

    private const string From = "FROM workflow.assistant_proposals p LEFT JOIN workflow.assistant_proposal_decisions d ON d.proposal_id = p.id";

    public async ValueTask<long?> InsertAsync(NewProposal proposal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        await using var insert = dataSource.CreateCommand("""
            INSERT INTO workflow.assistant_proposals (kind, dedupe_key, created_on, subject, payload, reason, evidence_json, checks_json, pack_hash, model_id, model_sha256, prompt_version, created_by, expires_at)
            VALUES (@kind, @key, @on, @subject, @payload::jsonb, @reason, @evidence::jsonb, @checks::jsonb, @pack, @model, @sha, @prompt, @by, @expires)
            ON CONFLICT (dedupe_key, created_on) DO NOTHING
            RETURNING id
            """);
        var p = insert.Parameters;
        p.AddWithValue("kind", NpgsqlDbType.Varchar, proposal.Kind);
        p.AddWithValue("key", NpgsqlDbType.Char, proposal.DedupeKey);
        p.AddWithValue("on", NpgsqlDbType.Date, proposal.CreatedOn);
        p.AddWithValue("subject", NpgsqlDbType.Varchar, proposal.Subject);
        p.AddWithValue("payload", NpgsqlDbType.Text, proposal.PayloadJson);
        p.AddWithValue("reason", NpgsqlDbType.Varchar, proposal.Reason);
        p.AddWithValue("evidence", NpgsqlDbType.Text, proposal.EvidenceJson);
        p.AddWithValue("checks", NpgsqlDbType.Text, proposal.ChecksJson);
        p.AddWithValue("pack", NpgsqlDbType.Char, proposal.PackHash);
        p.AddWithValue("model", NpgsqlDbType.Varchar, (object?)proposal.ModelId ?? DBNull.Value);
        p.AddWithValue("sha", NpgsqlDbType.Char, (object?)proposal.ModelSha256 ?? DBNull.Value);
        p.AddWithValue("prompt", NpgsqlDbType.Varchar, proposal.PromptVersion);
        p.AddWithValue("by", NpgsqlDbType.Varchar, proposal.CreatedBy);
        p.AddWithValue("expires", NpgsqlDbType.TimestampTz, proposal.ExpiresAt.ToUniversalTime());
        return await insert.ExecuteScalarAsync(cancellationToken) is long id ? id : null;
    }

    public async ValueTask<StoredProposal?> GetAsync(long id, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand($"SELECT {Columns} {From} WHERE p.id = @id");
        select.Parameters.AddWithValue("id", NpgsqlDbType.Bigint, id);
        var rows = await ReadAsync(select, cancellationToken);
        return rows.Count == 0 ? null : rows[0];
    }

    public async ValueTask<IReadOnlyList<StoredProposal>> ListAsync(string? kind, int limit, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand($"SELECT {Columns} {From} WHERE (@kind::varchar IS NULL OR p.kind = @kind) ORDER BY p.id DESC LIMIT @limit");
        select.Parameters.AddWithValue("kind", NpgsqlDbType.Varchar, (object?)kind ?? DBNull.Value);
        select.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, limit);
        return await ReadAsync(select, cancellationToken);
    }

    public async ValueTask<IReadOnlyList<StoredProposal>> PendingAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand($"SELECT {Columns} {From} WHERE d.id IS NULL AND p.expires_at > @now ORDER BY p.id");
        select.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, now.ToUniversalTime());
        return await ReadAsync(select, cancellationToken);
    }

    public async ValueTask<bool> RejectedSinceAsync(string dedupeKey, DateTimeOffset since, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand("SELECT EXISTS (SELECT 1 FROM workflow.assistant_proposals p JOIN workflow.assistant_proposal_decisions d ON d.proposal_id = p.id WHERE p.dedupe_key = @key AND d.decision = 'rejected' AND d.decided_at >= @since)");
        select.Parameters.AddWithValue("key", NpgsqlDbType.Char, dedupeKey);
        select.Parameters.AddWithValue("since", NpgsqlDbType.TimestampTz, since.ToUniversalTime());
        return (bool)(await select.ExecuteScalarAsync(cancellationToken))!;
    }

    public async ValueTask<int> CountAutoBetweenAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand("SELECT count(*)::int FROM workflow.assistant_proposal_decisions WHERE auto AND decided_at >= @from AND decided_at < @to");
        select.Parameters.AddWithValue("from", NpgsqlDbType.TimestampTz, from.ToUniversalTime());
        select.Parameters.AddWithValue("to", NpgsqlDbType.TimestampTz, to.ToUniversalTime());
        return (int)(await select.ExecuteScalarAsync(cancellationToken))!;
    }

    public async ValueTask<IAsyncDisposable> LockAsync(CancellationToken cancellationToken)
    {
        var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var take = new NpgsqlCommand("SELECT pg_advisory_lock(hashtextextended('workflow.assistant_proposal_decide', 0))", connection);
            await take.ExecuteNonQueryAsync(cancellationToken);
            return new DecisionLock(connection);
        }
#pragma warning disable CA1031 // the connection must be given back whatever failed
        catch (Exception)
#pragma warning restore CA1031
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public async ValueTask<(StoredProposalDecision Decision, bool Created)> InsertAsync(NewProposalDecision decision, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(decision);
        await using var insert = dataSource.CreateCommand("""
            INSERT INTO workflow.assistant_proposal_decisions (proposal_id, decision, actor, auto, settings_id, snapshot_hash, reason, result_ref)
            VALUES (@proposal, @decision, @actor, @auto, @settings, @hash, @reason, @result)
            ON CONFLICT (proposal_id) DO NOTHING
            """);
        var p = insert.Parameters;
        p.AddWithValue("proposal", NpgsqlDbType.Bigint, decision.ProposalId);
        p.AddWithValue("decision", NpgsqlDbType.Varchar, decision.Decision);
        p.AddWithValue("actor", NpgsqlDbType.Varchar, decision.Actor);
        p.AddWithValue("auto", NpgsqlDbType.Boolean, decision.Auto);
        p.AddWithValue("settings", NpgsqlDbType.Bigint, (object?)decision.SettingsId ?? DBNull.Value);
        p.AddWithValue("hash", NpgsqlDbType.Char, (object?)decision.SnapshotHash ?? DBNull.Value);
        p.AddWithValue("reason", NpgsqlDbType.Varchar, (object?)decision.Reason ?? DBNull.Value);
        p.AddWithValue("result", NpgsqlDbType.Varchar, (object?)decision.ResultRef ?? DBNull.Value);
        var created = await insert.ExecuteNonQueryAsync(cancellationToken) == 1;
        await using var select = dataSource.CreateCommand("SELECT proposal_id, decision, actor, auto, reason, result_ref, decided_at FROM workflow.assistant_proposal_decisions WHERE proposal_id = @id");
        select.Parameters.AddWithValue("id", NpgsqlDbType.Bigint, decision.ProposalId);
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("The decision was not found after it was stored.");
        }

        return (new StoredProposalDecision(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetBoolean(3), reader.IsDBNull(4) ? null : reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5), reader.GetFieldValue<DateTimeOffset>(6)), created);
    }

    private static async Task<List<StoredProposal>> ReadAsync(NpgsqlCommand select, CancellationToken cancellationToken)
    {
        var list = new List<StoredProposal>();
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetInt64(0);
            var decision = reader.IsDBNull(15)
                ? null
                : new StoredProposalDecision(id, reader.GetString(15), reader.GetString(16), reader.GetBoolean(17), reader.IsDBNull(18) ? null : reader.GetString(18), reader.IsDBNull(19) ? null : reader.GetString(19), reader.GetFieldValue<DateTimeOffset>(20));
            list.Add(new StoredProposal(
                id, reader.GetString(1), reader.GetString(2), DateOnly.FromDateTime(reader.GetDateTime(3)), reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7), reader.GetString(8), reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetString(10), reader.GetString(11), reader.GetString(12), reader.GetFieldValue<DateTimeOffset>(13), reader.GetFieldValue<DateTimeOffset>(14), decision));
        }

        return list;
    }

    private sealed class DecisionLock(NpgsqlConnection connection) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var release = new NpgsqlCommand("SELECT pg_advisory_unlock(hashtextextended('workflow.assistant_proposal_decide', 0))", connection);
                await release.ExecuteNonQueryAsync(CancellationToken.None);
            }
            finally
            {
                await connection.DisposeAsync();
            }
        }
    }
}
