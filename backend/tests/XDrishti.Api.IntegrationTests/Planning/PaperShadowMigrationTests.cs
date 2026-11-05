using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.IntegrationTests.Research;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Api.IntegrationTests.Planning;

/// <summary>CF-08 part A (ADR 0211): the migration <c>AddPaperShadow</c> agrees with the model, its Down removes the four tables, the five functions and the triggers (and nothing else of the paper schema), and it can be applied again.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class PaperShadowMigrationTests(ApiFactory factory)
{
    private const string Before = "20261104234940_AddLabNullTrialKind";

    private const string This = "20261105051654_AddPaperShadow";

    private const string Second = "20261105065744_StampShadowSettingInCommitOrder";

    private const string Third = "20261105075405_AddShadowGradeGross";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<long> CountAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return (long)(await command.ExecuteScalarAsync(Ct))!;
    }

    private const int ObjectCount = 4 + 5 + 4;

    private const string Objects = """
        SELECT (SELECT count(*) FROM pg_tables WHERE schemaname = 'paper' AND tablename LIKE 'shadow\_%')
             + (SELECT count(*) FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace WHERE n.nspname = 'paper' AND (p.proname LIKE '%shadow%'))
             + (SELECT count(*) FROM pg_trigger WHERE tgname LIKE 'trg\_shadow\_%' AND NOT tgisinternal)
        """;

    [Fact]
    public async Task The_migration_agrees_with_the_model_and_Down_and_Up_again_remove_and_restore_every_object()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_paper_shadow_probe", Ct);
        using var scope = scratch.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>();
        var migrator = db.GetService<IMigrator>();
        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);

        db.Database.HasPendingModelChanges().ShouldBeFalse("the migration and the model snapshot agree");
        (await CountAsync(connection, Objects)).ShouldBe(ObjectCount, "four tables, five functions (the clock and four guards) and four triggers");

        await migrator.MigrateAsync(Before, Ct);

        (await CountAsync(connection, Objects)).ShouldBe(0);
        (await CountAsync(connection, "SELECT count(*) FROM pg_tables WHERE schemaname = 'paper'")).ShouldBeGreaterThan(5, "the rest of the paper schema is untouched");

        await migrator.MigrateAsync(This, Ct);

        (await CountAsync(connection, Objects)).ShouldBe(ObjectCount);
    }

    [Fact]
    public async Task The_second_migration_stamps_the_switch_under_a_lock_and_its_Down_restores_the_first_stamp()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_paper_shadow_stamp_probe", Ct);
        using var scope = scratch.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>();
        var migrator = db.GetService<IMigrator>();
        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);
        const string Definition = "SELECT pg_get_functiondef('paper.stamp_shadow_setting()'::regprocedure)";

        (await ScalarTextAsync(connection, Definition)).ShouldContain("pg_advisory_xact_lock");
        (await ScalarTextAsync(connection, Definition)).ShouldContain("nextval");

        await migrator.MigrateAsync(This, Ct);

        (await ScalarTextAsync(connection, Definition)).ShouldNotContain("pg_advisory_xact_lock");
        (await CountAsync(connection, Objects)).ShouldBe(ObjectCount, "the first migration is still whole");

        await migrator.MigrateAsync(Second, Ct);

        (await ScalarTextAsync(connection, Definition)).ShouldContain("pg_advisory_xact_lock");
    }

    [Fact]
    public async Task The_third_migration_adds_the_gross_r_column_and_its_Down_removes_only_that_column()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_paper_shadow_gross_probe", Ct);
        using var scope = scratch.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>();
        var migrator = db.GetService<IMigrator>();
        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);
        const string Column = "SELECT count(*) FROM information_schema.columns WHERE table_schema = 'paper' AND table_name = 'shadow_grades' AND column_name = 'r_gross' AND is_nullable = 'YES' AND data_type = 'double precision'";
        const string Columns = "SELECT count(*) FROM information_schema.columns WHERE table_schema = 'paper' AND table_name = 'shadow_grades'";

        db.Database.HasPendingModelChanges().ShouldBeFalse("the migration and the model snapshot agree");
        (await CountAsync(connection, Column)).ShouldBe(1);
        var withColumn = await CountAsync(connection, Columns);

        await migrator.MigrateAsync(Second, Ct);

        (await CountAsync(connection, Column)).ShouldBe(0);
        (await CountAsync(connection, Columns)).ShouldBe(withColumn - 1);

        await migrator.MigrateAsync(Third, Ct);

        (await CountAsync(connection, Column)).ShouldBe(1);
    }

    private static async Task<string> ScalarTextAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return (string)(await command.ExecuteScalarAsync(Ct))!;
    }
}
