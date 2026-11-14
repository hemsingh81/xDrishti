using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;

namespace XDrishti.Api.IntegrationTests.Research;

/// <summary>ADR 0291: after all migrations the NIFTY 50 / INDIA VIX context tables are gone, while the basket context and relative-strength tables stay.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class DropMarketContextMigrationTests(ApiFactory factory)
{
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
}
