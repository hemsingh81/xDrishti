using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.IntegrationTests.Research;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Api.IntegrationTests.OptionCosts;

/// <summary>
/// P14-07 criterion 8: the migration <c>AddOptionSpreadModels</c> applies on a store built up to the migration before it, creates the two tables with the stated columns and keys, widens the generated check on the cost segment of a simulated trade, agrees with the model
/// snapshot, and is reversible (<c>Down</c> drops both tables and narrows the check; up again is clean).
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class OptionSpreadModelsMigrationTests(ApiFactory factory)
{
    private const string Before = "20261103044221_AddExpressionProfiles";

    private const string Columns = "SELECT column_name || '|' || data_type || '|' || coalesce(character_maximum_length::text, '') || '|' || is_nullable FROM information_schema.columns WHERE table_schema = 'research' AND table_name = @t";

    private const string SegmentCheck = "SELECT pg_get_constraintdef(oid) FROM pg_constraint WHERE conname = 'ck_sim_trades_cost_segment'";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string C(string name, string type, int? length = null, bool nullable = false) => $"{name}|{type}|{length}|{(nullable ? "YES" : "NO")}";

    private static async Task<List<string>> ListAsync(NpgsqlConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        var rows = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(Ct);
        while (await reader.ReadAsync(Ct))
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }

    [Fact]
    public async Task The_migration_creates_the_two_tables_widens_the_segment_check_agrees_with_the_snapshot_and_Down_removes_only_them()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_option_spread_probe", Ct, migrate: false);
        using var scope = scratch.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>();
        var migrator = db.GetService<IMigrator>();
        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);
        await migrator.MigrateAsync(Before, Ct);
        (await ListAsync(connection, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'research' AND table_name LIKE 'option_spread%'")).ShouldBeEmpty();
        (await ListAsync(connection, SegmentCheck)).Single().ShouldNotContain("'options'", Case.Sensitive);

        await migrator.MigrateAsync(null, Ct);

        (await ListAsync(connection, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'research' AND table_name LIKE 'option_spread%' ORDER BY 1")).ShouldBe(["option_spread_buckets", "option_spread_models"]);
        (await ListAsync(connection, Columns, ("t", "option_spread_models"))).Order(StringComparer.Ordinal).ShouldBe(
            new[]
            {
                C("model_id", "character", 64), C("underlying_id", "uuid"), C("sessions", "integer"), C("first_session", "date"), C("last_session", "date"), C("settings", "jsonb"), C("built_at", "timestamp with time zone"),
            }.Order(StringComparer.Ordinal));
        (await ListAsync(connection, Columns, ("t", "option_spread_buckets"))).Order(StringComparer.Ordinal).ShouldBe(
            new[]
            {
                C("model_id", "character", 64), C("moneyness", "character varying", 8), C("dte", "character varying", 8), C("tod", "character varying", 16), C("n", "integer"), C("half_spread_pct", "double precision"),
            }.Order(StringComparer.Ordinal));
        (await ListAsync(connection, "SELECT pg_get_constraintdef(oid) FROM pg_constraint WHERE conname = 'pk_option_spread_buckets'")).Single().ShouldBe("PRIMARY KEY (model_id, moneyness, dte, tod)");
        (await ListAsync(connection, "SELECT pg_get_constraintdef(oid) FROM pg_constraint WHERE conname = 'pk_option_spread_models'")).Single().ShouldBe("PRIMARY KEY (model_id)");
        (await ListAsync(connection, SegmentCheck)).Single().ShouldContain("'options'", Case.Sensitive);
        (await ListAsync(connection, SegmentCheck)).Single().ShouldContain("'futures'", Case.Sensitive);
        db.Database.HasPendingModelChanges().ShouldBeFalse("the model snapshot agrees with the model");

        await migrator.MigrateAsync(Before, Ct);

        (await ListAsync(connection, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'research' AND table_name LIKE 'option_spread%'")).ShouldBeEmpty();
        (await ListAsync(connection, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'research' AND table_name = 'sim_trades'")).ShouldBe(["sim_trades"], "Down removes the two tables and nothing else");
        (await ListAsync(connection, SegmentCheck)).Single().ShouldNotContain("'options'", Case.Sensitive);

        await migrator.MigrateAsync(null, Ct);

        (await ListAsync(connection, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'research' AND table_name LIKE 'option_spread%' ORDER BY 1")).ShouldBe(["option_spread_buckets", "option_spread_models"]);
    }

    [Fact]
    public async Task A_bucket_needs_its_model_and_a_model_id_must_be_64_lower_case_hex_and_a_negative_nan_or_infinite_spread_is_refused()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_option_spread_checks_probe", Ct);
        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);
        var id = new string('a', 64);

        async Task<string?> StateAsync(string sql)
        {
            try
            {
                await using var command = new NpgsqlCommand(sql, connection);
                await command.ExecuteNonQueryAsync(Ct);
                return null;
            }
            catch (PostgresException failure)
            {
                return failure.SqlState;
            }
        }

        (await StateAsync($"INSERT INTO research.option_spread_buckets VALUES ('{id}', '0', '0-2', 'all', 5, 1.0)")).ShouldBe(PostgresErrorCodes.ForeignKeyViolation);
        (await StateAsync("INSERT INTO research.option_spread_models (model_id, underlying_id, sessions, first_session, last_session, settings) VALUES ('" + new string('A', 64) + "', gen_random_uuid(), 60, '2026-01-01', '2026-04-01', '{}')")).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await StateAsync($"INSERT INTO research.option_spread_models (model_id, underlying_id, sessions, first_session, last_session, settings) VALUES ('{id}', gen_random_uuid(), 60, '2026-01-01', '2026-04-01', '{{}}')")).ShouldBeNull();
        (await StateAsync($"INSERT INTO research.option_spread_buckets VALUES ('{id}', '0', '0-2', 'all', 5, -0.1)")).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await StateAsync($"INSERT INTO research.option_spread_buckets VALUES ('{id}', '0', '0-2', 'all', 5, 'NaN')")).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await StateAsync($"INSERT INTO research.option_spread_buckets VALUES ('{id}', '0', '0-2', 'all', 5, 'Infinity')")).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await StateAsync($"INSERT INTO research.option_spread_buckets VALUES ('{id}', '0', '0-2', 'all', 0, 1.0)")).ShouldBe(PostgresErrorCodes.CheckViolation);
    }
}
