using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.IntegrationTests.Research;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Api.IntegrationTests.Selection;

/// <summary>BK-10: <c>AddSizingControls</c> creates the schema <c>selection</c> with its two insert-only history tables and their checks, agrees with the model, and <c>Down</c> drops the schema again.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class SizingMigrationTests(ApiFactory factory)
{
    private const string Before = "20261110220211_AddListBaskets";

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

    [Fact]
    public async Task Up_creates_the_two_tables_with_their_checks_and_agrees_with_the_model()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_sizing_up_probe", Ct);
        using var scope = scratch.Host.Services.CreateScope();
        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);

        (await ListAsync(connection, "SELECT table_name || '.' || column_name || '|' || data_type || '|' || is_nullable FROM information_schema.columns WHERE table_schema = 'selection' ORDER BY table_name, ordinal_position")).ShouldBe(
        [
            "basket_risk_budgets.id|bigint|NO", "basket_risk_budgets.basket_id|uuid|NO", "basket_risk_budgets.budget_pct|numeric|YES", "basket_risk_budgets.direction|character varying|NO", "basket_risk_budgets.changed_by|character varying|NO",
            "basket_risk_budgets.changed_at|timestamp with time zone|NO", "basket_risk_budgets.hash|character varying|NO",
            "sizing_settings_changes.id|bigint|NO", "sizing_settings_changes.key|character varying|NO", "sizing_settings_changes.old_value|character varying|NO", "sizing_settings_changes.new_value|character varying|NO",
            "sizing_settings_changes.direction|character varying|NO", "sizing_settings_changes.changed_by|character varying|NO", "sizing_settings_changes.changed_at|timestamp with time zone|NO", "sizing_settings_changes.hash|character varying|NO",
        ]);
        (await ListAsync(connection, "SELECT conname FROM pg_constraint WHERE connamespace = 'selection'::regnamespace AND contype <> 'n' ORDER BY 1")).ShouldBe(
        [
            "ck_basket_risk_budgets_direction", "ck_basket_risk_budgets_owner", "ck_basket_risk_budgets_pct", "ck_sizing_settings_changes_direction", "ck_sizing_settings_changes_key", "ck_sizing_settings_changes_owner",
            "pk_basket_risk_budgets", "pk_sizing_settings_changes",
        ]);
        scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>().Database.HasPendingModelChanges().ShouldBeFalse("the migration and the model snapshot agree");
    }

    [Fact]
    public async Task The_database_refuses_a_non_owner_actor_a_budget_out_of_range_and_an_unknown_key()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_sizing_check_probe", Ct);
        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);

        foreach (var sql in new[]
        {
            "INSERT INTO selection.sizing_settings_changes (key, old_value, new_value, direction, changed_by, hash) VALUES ('sizing.capacity.pct', '0.01', '0.02', 'loosen', 'system:nightly', 'h')",
            "INSERT INTO selection.sizing_settings_changes (key, old_value, new_value, direction, changed_by, hash) VALUES ('allow_upsizing', 'false', 'true', 'loosen', 'owner:x', 'h')",
            "INSERT INTO selection.basket_risk_budgets (basket_id, budget_pct, direction, changed_by, hash) VALUES (gen_random_uuid(), 150, 'loosen', 'owner:x', 'h')",
            "INSERT INTO selection.basket_risk_budgets (basket_id, budget_pct, direction, changed_by, hash) VALUES (gen_random_uuid(), 30, 'loosen', 'auto:rule', 'h')",
        })
        {
            await using var command = new NpgsqlCommand(sql, connection);
            var failure = await Should.ThrowAsync<PostgresException>(async () => await command.ExecuteNonQueryAsync(Ct));
            failure.SqlState.ShouldBe("23514", sql);
        }
    }

    [Fact]
    public async Task Down_drops_the_schema_and_up_again_is_clean()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_sizing_down_probe", Ct);
        using var scope = scratch.Host.Services.CreateScope();
        var migrator = scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>().GetService<IMigrator>();
        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);

        await migrator.MigrateAsync(Before, Ct);

        (await ListAsync(connection, "SELECT schema_name FROM information_schema.schemata WHERE schema_name = 'selection'")).ShouldBeEmpty();

        await migrator.MigrateAsync(null, Ct);

        (await ListAsync(connection, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'selection' ORDER BY 1")).ShouldBe(["basket_risk_budgets", "sizing_settings_changes"]);
    }
}
