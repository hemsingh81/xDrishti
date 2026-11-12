using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Evidence;

namespace XDrishti.Api.IntegrationTests.Cli;

/// <summary>The real use case of <c>xd evidence preflight</c> (spec EV-01 criteria 1 and 3) over the real database with a few rows seeded: twelve items, the seeded rows are read, and no row of any schema changes (an md5 of every table, not only a count).</summary>
[Collection(ApiTestSuite.Name)]
public sealed class EvidencePreflightWriteTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_preflight_prints_twelve_items_reads_seeded_rows_and_writes_nothing()
    {
        await factory.ResetAllAsync();
        await ExecuteAsync("INSERT INTO lab.holdout_blocks (start_date, history_first, history_last, length_months, setting_hash, created_at) VALUES ('2031-01-01', '2026-03-02', '2031-06-30', 12, repeat('a', 64), now()); "
            + "INSERT INTO platform.service_schedules (service_id, enabled, schedule, retries, timeout_minutes, catch_up, notify_on_failure, parameters, updated_by, updated_at) VALUES ('paper-capture', true, '{\"type\":\"daily\"}', 0, 5, true, true, '[]', 'test', now())");
        EvidencePreflightView view;
        try
        {
            var before = await ChecksumsAsync();
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                view = await scope.ServiceProvider.GetRequiredService<IQueryHandler<GetEvidencePreflightQuery, EvidencePreflightView>>().HandleAsync(new GetEvidencePreflightQuery(), Ct);
            }

            (await ChecksumsAsync()).ShouldBe(before);
        }
        finally
        {
            await ExecuteAsync("DELETE FROM lab.holdout_blocks; DELETE FROM platform.service_schedules WHERE service_id = 'paper-capture'");
        }

        view.Items.Count.ShouldBe(12);
        view.Items.ShouldAllBe(i => i.State == "MET" || i.State == "NOT MET" || i.State == "UNAVAILABLE");
        view.Items.Single(i => i.Item == GetEvidencePreflightHandler.HoldOutItem).State.ShouldBe("MET");
        view.Items.Single(i => i.Item == GetEvidencePreflightHandler.CaptureItem).State.ShouldBe("MET");
        view.Items.Single(i => i.Item == GetEvidencePreflightHandler.ChargeSheetItem).State.ShouldNotBe("MET");
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(factory.AdminConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(Ct);
    }

    /// <summary>The row count and an md5 of every row of every table, so that an update, or a delete plus an insert, is seen too.</summary>
    private async Task<Dictionary<string, string>> ChecksumsAsync()
    {
        await using var connection = new NpgsqlConnection(factory.AdminConnectionString);
        await connection.OpenAsync(Ct);
        var tables = new List<string>();
        await using (var list = new NpgsqlCommand("select table_schema || '.' || table_name from information_schema.tables where table_type = 'BASE TABLE' and table_schema not in ('pg_catalog', 'information_schema') and table_schema not like '\\_timescaledb%' and table_schema <> 'timescaledb_information' order by 1", connection))
        await using (var reader = await list.ExecuteReaderAsync(Ct))
        {
            while (await reader.ReadAsync(Ct))
            {
                tables.Add(reader.GetString(0));
            }
        }

        var sums = new Dictionary<string, string>();
        foreach (var table in tables)
        {
            var parts = table.Split('.');
            await using var sum = new NpgsqlCommand($"select count(*) || ':' || coalesce(md5(string_agg(t::text, '' order by t::text)), '') from \"{parts[0]}\".\"{parts[1]}\" t", connection);
            sums[table] = (string)(await sum.ExecuteScalarAsync(Ct))!;
        }

        return sums;
    }
}
