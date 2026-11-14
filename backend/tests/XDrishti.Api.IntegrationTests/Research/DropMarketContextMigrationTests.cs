using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Api.IntegrationTests.Research;

/// <summary>ADR 0291: after all migrations the NIFTY 50 / INDIA VIX context tables are gone, while the basket context and relative-strength tables stay.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class DropMarketContextMigrationTests(ApiFactory factory)
{
    private const string Before = "20261113195621_AddContextVixCsv";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<string?> RegclassAsync(NpgsqlConnection connection, string table)
    {
        await using var command = new NpgsqlCommand("SELECT to_regclass(@t)::text", connection);
        command.Parameters.AddWithValue("t", table);
        return await command.ExecuteScalarAsync(Ct) as string;
    }

    [Fact]
    public async Task The_market_context_tables_are_dropped_and_the_basket_context_tables_stay()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_drop_context_probe", Ct);
        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);

        foreach (var gone in new[] { "market.context_daily", "market.context_vix_csv" })
        {
            (await RegclassAsync(connection, gone)).ShouldBeNull(gone + " is dropped");
        }

        foreach (var kept in new[] { "market.basket_context_daily", "market.relative_strength_daily" })
        {
            (await RegclassAsync(connection, kept)).ShouldNotBeNull(kept + " stays");
        }
    }

    [Fact]
    public async Task Down_brings_the_empty_tables_back_so_the_chain_stays_reversible_and_up_drops_them_again()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_drop_context_down_probe", Ct);
        using var scope = scratch.Host.Services.CreateScope();
        var migrator = scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>().GetService<IMigrator>();
        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);

        await migrator.MigrateAsync(Before, Ct);

        foreach (var back in new[] { "market.context_daily", "market.context_vix_csv" })
        {
            (await RegclassAsync(connection, back)).ShouldNotBeNull(back + " is back");
        }

        await using (var command = new NpgsqlCommand("SELECT count(*) FROM information_schema.columns WHERE table_schema = 'market' AND table_name = 'context_daily' AND column_name LIKE 'breadth%'", connection))
        {
            ((long)(await command.ExecuteScalarAsync(Ct))!).ShouldBe(6L, "the breadth columns of AddContextBreadth are back");
        }

        await migrator.MigrateAsync(null, Ct);

        foreach (var gone in new[] { "market.context_daily", "market.context_vix_csv" })
        {
            (await RegclassAsync(connection, gone)).ShouldBeNull(gone + " is dropped again");
        }
    }
}
