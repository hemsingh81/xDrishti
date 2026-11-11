using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.IntegrationTests.Research;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Api.IntegrationTests.Selection;

/// <summary>BK-08: <c>AddBasketContextDaily</c> creates <c>market.basket_context_daily</c> and <c>selection.basket_settings_changes</c> with their checks, agrees with the model, and <c>Down</c> removes exactly those two tables.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class BasketContextMigrationTests(ApiFactory factory)
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

    [Fact]
    public async Task Up_creates_the_two_tables_with_their_checks_and_agrees_with_the_model()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_basketctx_up_probe", Ct);
        using var scope = scratch.Host.Services.CreateScope();
        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);

        (await ListAsync(connection, "SELECT column_name || '|' || data_type || '|' || is_nullable FROM information_schema.columns WHERE table_schema = 'market' AND table_name = 'basket_context_daily' ORDER BY ordinal_position")).ShouldBe(
        [
            "basket_id|uuid|NO", "as_of|date|NO", "n_members|integer|NO", "n_voters|integer|NO", "b20|numeric|YES", "b50|numeric|YES", "b200|numeric|YES", "ad_share|numeric|YES", "new_high_share|numeric|YES", "dispersion|numeric|YES", "rho_bar|numeric|YES",
            "state|text|NO", "membership_basis|text|NO", "created_at|timestamp with time zone|NO",
        ]);
        (await ListAsync(connection, "SELECT conname FROM pg_constraint WHERE conrelid = 'market.basket_context_daily'::regclass AND contype <> 'n' ORDER BY 1")).ShouldBe(
            ["ck_basket_context_daily_basis", "ck_basket_context_daily_counts", "ck_basket_context_daily_shares", "ck_basket_context_daily_state", "ck_basket_context_daily_value", "pk_basket_context_daily"]);
        (await ListAsync(connection, "SELECT conname FROM pg_constraint WHERE conrelid = 'selection.basket_settings_changes'::regclass AND contype <> 'n' ORDER BY 1")).ShouldBe(
            ["ck_basket_settings_changes_direction", "ck_basket_settings_changes_key", "ck_basket_settings_changes_owner", "pk_basket_settings_changes"]);
        scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>().Database.HasPendingModelChanges().ShouldBeFalse("the migration and the model snapshot agree");
    }

    [Fact]
    public async Task The_database_refuses_a_non_owner_actor_an_unknown_key_a_bad_state_and_a_share_out_of_range()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_basketctx_check_probe", Ct);
        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);

        foreach (var sql in new[]
        {
            "INSERT INTO selection.basket_settings_changes (key, old_value, new_value, direction, changed_by, hash) VALUES ('selection.cluster_rho', '0.7', '0.8', 'loosen', 'system:nightly', 'h')",
            "INSERT INTO selection.basket_settings_changes (key, old_value, new_value, direction, changed_by, hash) VALUES ('gate.allow_orders', 'a', 'b', 'loosen', 'owner:x', 'h')",
            "INSERT INTO selection.basket_settings_changes (key, old_value, new_value, direction, changed_by, hash) VALUES ('selection.cluster_rho', '0.7', '0.8', 'auto', 'owner:x', 'h')",
            "INSERT INTO market.basket_context_daily VALUES (gen_random_uuid(), '2026-10-09', 10, 8, 0.5, NULL, NULL, NULL, NULL, NULL, NULL, 'maybe', 'single_snapshot', now())",
            "INSERT INTO market.basket_context_daily VALUES (gen_random_uuid(), '2026-10-09', 10, 8, 1.5, NULL, NULL, NULL, NULL, NULL, NULL, 'value', 'single_snapshot', now())",
            "INSERT INTO market.basket_context_daily VALUES (gen_random_uuid(), '2026-10-09', 10, 8, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 'value', 'single_snapshot', now())",
            "INSERT INTO market.basket_context_daily VALUES (gen_random_uuid(), '2026-10-09', 10, 8, 0.5, NULL, NULL, NULL, NULL, NULL, NULL, 'value', 'guess', now())",
        })
        {
            await using var command = new NpgsqlCommand(sql, connection);
            var failure = await Should.ThrowAsync<PostgresException>(async () => await command.ExecuteNonQueryAsync(Ct));
            failure.SqlState.ShouldBe("23514", sql);
        }
    }

    [Fact]
    public async Task A_second_row_for_the_same_basket_and_day_is_refused_by_the_primary_key()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_basketctx_pk_probe", Ct);
        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);
        const string insert = "INSERT INTO market.basket_context_daily VALUES ('00000000-0000-0000-0000-0000000000b1', '2026-10-09', 10, 8, 0.5, NULL, NULL, NULL, NULL, NULL, NULL, 'value', 'single_snapshot', now())";
        await using (var first = new NpgsqlCommand(insert, connection))
        {
            (await first.ExecuteNonQueryAsync(Ct)).ShouldBe(1);
        }

        await using var second = new NpgsqlCommand(insert, connection);
        (await Should.ThrowAsync<PostgresException>(async () => await second.ExecuteNonQueryAsync(Ct))).SqlState.ShouldBe("23505");
    }

    [Fact]
    public async Task Down_drops_only_the_two_tables_and_up_again_is_clean()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_basketctx_down_probe", Ct);
        using var scope = scratch.Host.Services.CreateScope();
        var migrator = scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>().GetService<IMigrator>();
        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);

        await migrator.MigrateAsync(Before, Ct);

        (await ListAsync(connection, "SELECT table_schema || '.' || table_name FROM information_schema.tables WHERE table_name IN ('basket_context_daily', 'basket_settings_changes')")).ShouldBeEmpty();
        (await ListAsync(connection, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'selection' ORDER BY 1")).ShouldBe(["basket_risk_budgets", "sizing_settings_changes"]);

        await migrator.MigrateAsync(null, Ct);

        (await ListAsync(connection, "SELECT table_schema || '.' || table_name FROM information_schema.tables WHERE table_name IN ('basket_context_daily', 'basket_settings_changes') ORDER BY 1")).ShouldBe(["market.basket_context_daily", "selection.basket_settings_changes"]);
    }
}
