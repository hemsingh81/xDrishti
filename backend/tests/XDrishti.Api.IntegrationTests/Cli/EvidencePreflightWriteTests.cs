using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Evidence;

namespace XDrishti.Api.IntegrationTests.Cli;

/// <summary>The real use case of <c>xd evidence preflight</c> (spec EV-01 criteria 1 and 3) over the real database: twelve items, and not one row of any schema changes.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class EvidencePreflightWriteTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_preflight_prints_twelve_items_and_writes_nothing()
    {
        await factory.ResetAllAsync();
        var before = await RowCountsAsync();

        EvidencePreflightView view;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            view = await scope.ServiceProvider.GetRequiredService<IQueryHandler<GetEvidencePreflightQuery, EvidencePreflightView>>().HandleAsync(new GetEvidencePreflightQuery(), Ct);
        }

        view.Items.Count.ShouldBe(12);
        view.Items.ShouldAllBe(i => i.State == "MET" || i.State == "NOT MET" || i.State == "UNAVAILABLE");
        view.Items.Single(i => i.Item == GetEvidencePreflightHandler.HoldOutItem).State.ShouldNotBe("MET");
        view.Items.Single(i => i.Item == GetEvidencePreflightHandler.ChargeSheetItem).State.ShouldNotBe("MET");
        (await RowCountsAsync()).ShouldBe(before);
    }

    private async Task<Dictionary<string, long>> RowCountsAsync()
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

        var counts = new Dictionary<string, long>();
        foreach (var table in tables)
        {
            var parts = table.Split('.');
            await using var count = new NpgsqlCommand($"select count(*) from \"{parts[0]}\".\"{parts[1]}\"", connection);
            counts[table] = (long)(await count.ExecuteScalarAsync(Ct))!;
        }

        return counts;
    }
}
