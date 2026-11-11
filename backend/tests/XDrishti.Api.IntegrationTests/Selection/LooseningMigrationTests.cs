using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.IntegrationTests.Research;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Api.IntegrationTests.Selection;

/// <summary>BK-10b: <c>AddLooseningDismissals</c> creates the two insert-only tables of schema <c>trading</c> with their checks, agrees with the model, and <c>Down</c> drops them (and their functions) again.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class LooseningMigrationTests(ApiFactory factory)
{
    private const string Before = "20261111045019_AddBasketToolAccess";

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
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_loosening_up_probe", Ct);
        using var scope = scratch.Host.Services.CreateScope();
        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);

        (await ListAsync(connection, "SELECT table_name || '.' || column_name || '|' || data_type || '|' || is_nullable FROM information_schema.columns WHERE table_schema = 'trading' AND table_name LIKE 'loosening_%' ORDER BY table_name, ordinal_position")).ShouldBe(
        [
            "loosening_dismissals.id|bigint|NO", "loosening_dismissals.notice_source|character varying|NO", "loosening_dismissals.notice_id|bigint|NO", "loosening_dismissals.actor|character varying|NO",
            "loosening_dismissals.setting_id|bigint|YES", "loosening_dismissals.setting_hash|character varying|YES", "loosening_dismissals.created_at|timestamp with time zone|NO",
            "loosening_settings.id|bigint|NO", "loosening_settings.auto_dismiss|boolean|NO", "loosening_settings.changed_by|character varying|NO", "loosening_settings.changed_at|timestamp with time zone|NO", "loosening_settings.hash|character varying|NO",
        ]);
        (await ListAsync(connection, "SELECT conname FROM pg_constraint WHERE conrelid IN ('trading.loosening_dismissals'::regclass, 'trading.loosening_settings'::regclass) AND contype <> 'n' ORDER BY 1")).ShouldBe(
        [
            "ck_loosening_dismissals_actor", "ck_loosening_dismissals_source", "ck_loosening_settings_owner", "fk_loosening_dismissals_loosening_settings_setting_id",
            "pk_loosening_dismissals", "pk_loosening_settings", "uq_loosening_dismissals_notice",
        ]);
        scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>().Database.HasPendingModelChanges().ShouldBeFalse("the migration and the model snapshot agree");
    }

    [Fact]
    public async Task Down_drops_the_tables_and_functions_and_up_again_is_clean()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_loosening_down_probe", Ct);
        using var scope = scratch.Host.Services.CreateScope();
        var migrator = scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>().GetService<IMigrator>();
        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);

        await migrator.MigrateAsync(Before, Ct);

        (await ListAsync(connection, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'trading' AND table_name LIKE 'loosening_%'")).ShouldBeEmpty();
        (await ListAsync(connection, "SELECT proname FROM pg_proc WHERE proname LIKE '%loosening%'")).ShouldBeEmpty();

        await migrator.MigrateAsync(null, Ct);

        (await ListAsync(connection, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'trading' AND table_name LIKE 'loosening_%' ORDER BY 1")).ShouldBe(["loosening_dismissals", "loosening_settings"]);
    }
}
