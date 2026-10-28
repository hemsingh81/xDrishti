using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.LiveTrading;
using XDrishti.Domain.LiveTrading;

namespace XDrishti.Infrastructure.LiveTrading;

/// <summary>The compliance record (<c>trading.compliance_records</c>, spec P11-05): insert-only, the time stamped by the database. The table checks the same rules again.</summary>
internal sealed class ComplianceStore(NpgsqlDataSource dataSource) : IComplianceStore
{
    private const string Columns = "id, kind, outcome, decided_on, summary, sources, static_ip_state, isp, monthly_cost, ip_version, whitelisted_at, lock_ends_on, next_review_on, actor, created_at";

    public async ValueTask<StoredCompliance> AppendAsync(ComplianceRecord record, string actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        await using var command = dataSource.CreateCommand($"""
            INSERT INTO trading.compliance_records (kind, outcome, decided_on, summary, sources, static_ip_state, isp, monthly_cost, ip_version, whitelisted_at, lock_ends_on, next_review_on, actor)
            VALUES (@kind, @outcome, @decided_on, @summary, @sources, @state, @isp, @cost, @ip_version, @whitelisted_at, @lock_ends_on, @next_review_on, @actor)
            RETURNING {Columns}
            """);
        command.Parameters.AddWithValue("kind", record.Kind);
        command.Parameters.AddWithValue("outcome", record.Outcome);
        command.Parameters.Add(new NpgsqlParameter("decided_on", NpgsqlDbType.Date) { Value = record.DecidedOn });
        command.Parameters.AddWithValue("summary", record.Summary);
        command.Parameters.Add(new NpgsqlParameter("sources", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = record.Sources.ToArray() });
        command.Parameters.Add(new NpgsqlParameter("state", NpgsqlDbType.Varchar) { Value = (object?)record.StaticIpState ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("isp", NpgsqlDbType.Varchar) { Value = (object?)record.Isp ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("cost", NpgsqlDbType.Numeric) { Value = (object?)record.MonthlyCost ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("ip_version", NpgsqlDbType.Varchar) { Value = (object?)record.IpVersion ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("whitelisted_at", NpgsqlDbType.Date) { Value = (object?)record.WhitelistedAt ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("lock_ends_on", NpgsqlDbType.Date) { Value = (object?)record.LockEndsOn ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("next_review_on", NpgsqlDbType.Date) { Value = record.NextReviewOn });
        command.Parameters.AddWithValue("actor", actor);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return Read(reader);
    }

    public async ValueTask<IReadOnlyList<StoredCompliance>> ListAsync(string? kind, int limit, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand($"SELECT {Columns} FROM trading.compliance_records WHERE (@kind::text IS NULL OR kind = @kind) ORDER BY id DESC LIMIT @take");
        command.Parameters.Add(new NpgsqlParameter("kind", NpgsqlDbType.Text) { Value = (object?)kind ?? DBNull.Value });
        command.Parameters.AddWithValue("take", Math.Max(1, limit));
        return await ReadAllAsync(command, cancellationToken);
    }

    public async ValueTask<IReadOnlyList<StoredCompliance>> LatestPerKindAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand($"SELECT DISTINCT ON (kind) {Columns} FROM trading.compliance_records ORDER BY kind, id DESC");
        return await ReadAllAsync(command, cancellationToken);
    }

    private static async Task<IReadOnlyList<StoredCompliance>> ReadAllAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        var rows = new List<StoredCompliance>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(Read(reader));
        }

        return rows;
    }

    private static StoredCompliance Read(NpgsqlDataReader r)
    {
        string? Text(int i) => r.IsDBNull(i) ? null : r.GetString(i);
        DateOnly? Date(int i) => r.IsDBNull(i) ? null : r.GetFieldValue<DateOnly>(i);
        var record = ComplianceRecord.Restore(
            r.GetString(1), r.GetString(2), r.GetFieldValue<DateOnly>(3), r.GetString(4), r.GetFieldValue<string[]>(5), Text(6), Text(7), r.IsDBNull(8) ? null : r.GetDecimal(8), Text(9), Date(10), Date(11), r.GetFieldValue<DateOnly>(12));
        return new StoredCompliance(r.GetInt64(0), record, r.GetString(13), new DateTimeOffset(r.GetDateTime(14), TimeSpan.Zero));
    }
}
