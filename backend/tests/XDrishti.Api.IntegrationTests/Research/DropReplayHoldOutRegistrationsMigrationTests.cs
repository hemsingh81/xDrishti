using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Api.IntegrationTests.Research;

/// <summary>ADR 0292: after all migrations the three tables of the pre-registered hold-out replay (SR-03) are gone, while the hold-out blocks and looks (HO-01) stay.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class DropReplayHoldOutRegistrationsMigrationTests(ApiFactory factory)
{
    private const string Before = "20261114172134_DropMarketContext";

    private static readonly string[] Dropped = ["research.replay_holdout_registrations", "research.replay_holdout_results", "research.replay_holdout_settings"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<string?> RegclassAsync(NpgsqlConnection connection, string table)
    {
        await using var command = new NpgsqlCommand("SELECT to_regclass(@t)::text", connection);
        command.Parameters.AddWithValue("t", table);
        return await command.ExecuteScalarAsync(Ct) as string;
    }

    [Fact]
    public async Task The_hold_out_replay_tables_are_dropped_and_the_hold_out_looks_stay()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_drop_sr03_probe", Ct);
        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);

        foreach (var gone in Dropped)
        {
            (await RegclassAsync(connection, gone)).ShouldBeNull(gone + " is dropped");
        }

        (await RegclassAsync(connection, "lab.holdout_unlocks")).ShouldNotBeNull("the hold-out looks stay");
    }

    [Fact]
    public async Task Down_brings_the_empty_tables_back_so_the_chain_stays_reversible_and_up_drops_them_again()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_drop_sr03_down_probe", Ct);
        using var scope = scratch.Host.Services.CreateScope();
        var migrator = scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>().GetService<IMigrator>();
        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);

        await migrator.MigrateAsync(Before, Ct);

        foreach (var back in Dropped)
        {
            (await RegclassAsync(connection, back)).ShouldNotBeNull(back + " is back");
        }

        await migrator.MigrateAsync("20261113064942_AddEvidenceSets", Ct); // the older Down reads the registrations table: it must be there
        await migrator.MigrateAsync(null, Ct);

        foreach (var gone in Dropped)
        {
            (await RegclassAsync(connection, gone)).ShouldBeNull(gone + " is dropped again");
        }
    }
}
