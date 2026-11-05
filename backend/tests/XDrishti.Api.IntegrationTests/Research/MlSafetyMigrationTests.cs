using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Domain.Learning;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Api.IntegrationTests.Research;

/// <summary>CF-10 part 10c-2 (ADR 0217): the migration <c>AddMlSafety</c> adds <c>research.ml_influence_checks</c> (hash checks, a key to the model, an index for the newest check of a model), agrees with the snapshot, and Down removes only that table.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class MlSafetyMigrationTests(ApiFactory factory)
{
    private const string Before = "20261105075405_AddShadowGradeGross";

    private static readonly string SetId = new('a', 64);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<int> ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteNonQueryAsync(Ct);
    }

    private static async Task<long> CountAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return (long)(await command.ExecuteScalarAsync(Ct))!;
    }

    private static string Insert(long model, string setting, string inputs) =>
        $"INSERT INTO research.ml_influence_checks (model_id, evaluated_at, may_influence, terms, setting_hash, inputs_hash) VALUES ({model}, now(), false, '[]', '{setting}', '{inputs}')";

    private static async Task<long> RegisterModelAsync(IServiceProvider services, NpgsqlConnection connection)
    {
        await ExecuteAsync(connection, $"""
            INSERT INTO research.training_sets (set_id, label_set_id, rule_version, feature_version, basis, sources, required_features, half_life_days, prior_strength, min_trades, credible_level, from_date, to_date, labelled, row_count,
                positives, negatives, unresolved, no_target, dropped_no_features, dropped_unknown, created_at, created_by)
            VALUES ('{SetId}', '{SetId}', 'tb-1', 'tf-1', 'Gross', 's', 'cell_n', 180, 20, 30, 0.9, '2026-01-01', '2026-06-01', 10, 10, 6, 4, 0, 0, 0, 0, now(), 'test')
            """);
        var model = new ModelToRegister(new string('b', 64), new string('e', 64), 100, "FastTree", null, SetId, "tf-1", 3, 30, 10, 40, new DateOnly(2026, 1, 1), new DateOnly(2026, 4, 1), new DateOnly(2026, 5, 1), ValidationOutcome.Eligible, 6, 5, 4, true, 3.5, 0.65, 0.23,
            string.Empty, "report", "params", [-1.0, 0.0, 1.0], [0.3, 0.5, 0.7], [0.25, 0.25, 0.25], 1, 4, DateTimeOffset.UtcNow, "test");
        return (await services.GetRequiredService<IModelRegistry>().RegisterAsync(model, Ct)).Id;
    }

    [Fact]
    public async Task The_table_takes_a_check_of_a_model_refuses_bad_hashes_and_an_unknown_model_and_agrees_with_the_snapshot()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_ml_safety_up_probe", Ct);
        using var scope = scratch.Host.Services.CreateScope();
        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);
        var model = await RegisterModelAsync(scope.ServiceProvider, connection);
        var good = new string('a', 64);

        (await ExecuteAsync(connection, Insert(model, good, good))).ShouldBe(1);
        (await Should.ThrowAsync<PostgresException>(() => ExecuteAsync(connection, Insert(model, "ZZ", good)))).SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
        (await Should.ThrowAsync<PostgresException>(() => ExecuteAsync(connection, Insert(model, good, new string('A', 64))))).SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
        (await Should.ThrowAsync<PostgresException>(() => ExecuteAsync(connection, Insert(model + 1000, good, good)))).SqlState.ShouldBe(PostgresErrorCodes.ForeignKeyViolation);
        scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>().Database.HasPendingModelChanges().ShouldBeFalse("the migration and the model snapshot agree");
        (await CountAsync(connection, "SELECT count(*) FROM pg_indexes WHERE schemaname = 'research' AND tablename = 'ml_influence_checks' AND indexname = 'ix_ml_influence_checks_model_id_id'")).ShouldBe(1);
    }

    [Fact]
    public async Task Down_removes_only_the_table_and_up_again_is_clean()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_ml_safety_down_probe", Ct);
        using var scope = scratch.Host.Services.CreateScope();
        var migrator = scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>().GetService<IMigrator>();
        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);
        var model = await RegisterModelAsync(scope.ServiceProvider, connection);
        await ExecuteAsync(connection, Insert(model, new string('a', 64), new string('b', 64)));

        await migrator.MigrateAsync(Before, Ct);

        (await CountAsync(connection, "SELECT count(*) FROM pg_tables WHERE schemaname = 'research' AND tablename = 'ml_influence_checks'")).ShouldBe(0);
        (await CountAsync(connection, "SELECT count(*) FROM research.model_versions")).ShouldBe(1, "Down removes the table of the checks and nothing else");

        await migrator.MigrateAsync(null, Ct);

        (await CountAsync(connection, "SELECT count(*) FROM research.ml_influence_checks")).ShouldBe(0);
        (await ExecuteAsync(connection, Insert(model, new string('a', 64), new string('b', 64)))).ShouldBe(1);
    }
}
