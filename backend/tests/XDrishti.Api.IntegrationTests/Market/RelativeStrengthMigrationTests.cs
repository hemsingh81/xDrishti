using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.IntegrationTests.Research;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Api.IntegrationTests.Market;

/// <summary>BK-09 criterion 8 and the integration plan: the migration <c>AddRelativeStrengthDaily</c> creates the two tables with the primary keys of the spec and agrees with the model, and Down removes only them.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class RelativeStrengthMigrationTests(ApiFactory factory)
{
    private const string Before = "20261110230546_AddSizingControls";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<List<string>> ListAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        var rows = new List<string>();
        while (await reader.ReadAsync(Ct))
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }

    private const string Tables = "SELECT table_name FROM information_schema.tables WHERE table_schema = 'market' AND table_name IN ('relative_strength_daily', 'sector_strength_daily') ORDER BY 1";

    [Fact]
    public async Task The_migration_creates_both_tables_with_their_keys_and_agrees_with_the_model()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_rs_up_probe", Ct);
        using var scope = scratch.Host.Services.CreateScope();
        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);

        (await ListAsync(connection, Tables)).ShouldBe(["relative_strength_daily", "sector_strength_daily"]);
        (await ListAsync(connection, "SELECT a.attname FROM pg_index i JOIN pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = ANY (i.indkey) WHERE i.indrelid = 'market.relative_strength_daily'::regclass AND i.indisprimary ORDER BY a.attname"))
            .ShouldBe(["as_of", "basket_id", "instrument_id", "lookback", "skip"]);
        (await ListAsync(connection, "SELECT a.attname FROM pg_index i JOIN pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = ANY (i.indkey) WHERE i.indrelid = 'market.sector_strength_daily'::regclass AND i.indisprimary ORDER BY a.attname"))
            .ShouldBe(["as_of", "sector", "window_sessions"]);
        scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>().Database.HasPendingModelChanges().ShouldBeFalse("the migration and the model snapshot agree");
    }

    [Fact]
    public async Task Down_removes_only_the_two_tables_and_up_again_is_clean()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_rs_down_probe", Ct);
        using var scope = scratch.Host.Services.CreateScope();
        var migrator = scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>().GetService<IMigrator>();
        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);

        await migrator.MigrateAsync(Before, Ct);

        (await ListAsync(connection, Tables)).ShouldBeEmpty();
        (await ListAsync(connection, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'market' AND table_name = 'context_daily'")).ShouldBe(["context_daily"]);

        await migrator.MigrateAsync(null, Ct);

        (await ListAsync(connection, Tables)).ShouldBe(["relative_strength_daily", "sector_strength_daily"]);
    }
}
