using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;

namespace XDrishti.Api.IntegrationTests.Research;

/// <summary>ADR 0288: after all migrations the depth and tick recording tables are gone, while the opening-minute, option-chain and feature-set comparison tables (P13-05) stay.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class DropDepthAndTickMigrationTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<string?> RegclassAsync(NpgsqlConnection connection, string table)
    {
        await using var command = new NpgsqlCommand("SELECT to_regclass(@t)::text", connection);
        command.Parameters.AddWithValue("t", table);
        return await command.ExecuteScalarAsync(Ct) as string;
    }

    [Fact]
    public async Task The_depth_and_tick_tables_are_dropped_and_the_tables_that_stay_are_present()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_drop_depth_probe", Ct);
        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);

        foreach (var gone in new[] { "market.depth_snapshots", "market.depth_session_instruments", "market.depth_sessions", "market.depth_recorder_switch", "market.trade_ticks", "market.tick_session_instruments", "market.tick_sessions", "market.tick_recorder_switch" })
        {
            (await RegclassAsync(connection, gone)).ShouldBeNull(gone + " is dropped");
        }

        foreach (var kept in new[] { "market.opening_bar_events", "market.opening_bars_switch", "options.chain_snapshots", "research.trade_features_ext", "research.extension_evaluations" })
        {
            (await RegclassAsync(connection, kept)).ShouldNotBeNull(kept + " stays");
        }
    }
}
