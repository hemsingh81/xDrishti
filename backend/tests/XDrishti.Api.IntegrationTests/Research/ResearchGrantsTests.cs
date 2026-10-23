using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Api.IntegrationTests.Research;

/// <summary>
/// Criterion 7 of P3-10, a standing test: what the runtime role <c>xd_app</c> may do to the research tables. The role is cluster-wide and the grant helpers of the migrations hard-code it, so creating it in the shared test server would turn the helpers
/// from no-ops into real grants for every later migration test; this class therefore runs on its own throw-away server, where the role is created with the default privileges of <c>deploy/db/init/10-roles.sh</c> and the whole migration chain is applied
/// by <c>xd_owner</c>. The assertions run over every table of the <c>research</c> schema that exists at that time, so a later research migration that re-grants the schema and forgets <c>MakeAppendOnlyForAppRole</c> fails here.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class ResearchGrantsTests(ApiFactory factory, ResearchGrantsTests.DedicatedServer server) : IClassFixture<ResearchGrantsTests.DedicatedServer>
{
    private const string InsertRun = """
        INSERT INTO research.sim_runs (run_key, engine_version, record_schema_version, trade_schema_version, strategy_id, definition_sha256, from_date, to_date, universe_kind, universe_label, instruments, instruments_run,
            trades_in_window, candidates_in_window, trade_count, open_at_end_count, acknowledge_csv_adjusted, fills, record_json, record_sha256, trades_sha256, created_at, created_by)
        VALUES (repeat('a', 64), 'sim-5', 5, 4, 'ORB', repeat('b', 64), '2026-01-01', '2026-02-01', 'basket', 'probe', 1, 1, 0, 0, 0, 0, false, '{}', '{}', repeat('c', 64), repeat('d', 64), now(), 'probe') RETURNING id
        """;

    private string _owner => server.Owner;

    private string _app => server.App;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>One dedicated throw-away server for all the facts of the class (never the shared one): the role is created with the default privileges of the init script and the whole chain is migrated by the owner.</summary>
    public sealed class DedicatedServer : IAsyncLifetime
    {
        private PostgreSqlContainer? _container;

        public string Owner { get; private set; } = string.Empty;

        public string App { get; private set; } = string.Empty;

        public async ValueTask InitializeAsync()
        {
            var appPassword = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
            ContainerRuntime.UsePodmanIfNeeded();
            _container = new PostgreSqlBuilder(ApiFactory.TimescaleImage).WithDatabase("xdrishti").WithUsername("xd_owner").WithPassword(Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16))).Build();
            await _container.StartAsync(TestContext.Current.CancellationToken);
            Owner = new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Pooling = false }.ConnectionString;
            App = new NpgsqlConnectionStringBuilder(Owner) { Username = "xd_app", Password = appPassword }.ConnectionString;
            await StoreKit.ExecuteAsync(
                Owner,
                $"""
                CREATE ROLE xd_app LOGIN PASSWORD '{appPassword}';
                GRANT CONNECT ON DATABASE xdrishti TO xd_app;
                ALTER DEFAULT PRIVILEGES FOR ROLE xd_owner GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO xd_app;
                ALTER DEFAULT PRIVILEGES FOR ROLE xd_owner GRANT USAGE, SELECT ON SEQUENCES TO xd_app;
                """,
                TestContext.Current.CancellationToken);
            var options = new DbContextOptionsBuilder<XDrishtiDbContext>()
                .UseNpgsql(Owner, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "platform"))
                .UseSnakeCaseNamingConvention()
                .Options;
            await using var db = new XDrishtiDbContext(options);
            await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            NpgsqlConnection.ClearAllPools();
            if (_container is not null)
            {
                await _container.DisposeAsync();
            }
        }
    }

    private static async Task<string?> SqlStateAsync(string connectionString, string sql)
    {
        try
        {
            await StoreKit.ExecuteAsync(connectionString, sql, Ct);
            return null;
        }
        catch (PostgresException failure)
        {
            return failure.SqlState;
        }
    }

    [Fact]
    public async Task On_every_research_table_the_runtime_role_reads_and_inserts_and_can_neither_change_nor_delete()
    {
        await using var connection = new NpgsqlConnection(_owner);
        await connection.OpenAsync(Ct);
        await using var tables = new NpgsqlCommand("SELECT tablename FROM pg_tables WHERE schemaname = 'research' ORDER BY 1", connection);
        var names = new List<string>();
        await using (var reader = await tables.ExecuteReaderAsync(Ct))
        {
            while (await reader.ReadAsync(Ct))
            {
                names.Add(reader.GetString(0));
            }
        }

        names.Order(StringComparer.Ordinal).ShouldBe(["cell_stats", "exit_selection_folds", "exit_selections", "model_designations", "model_versions", "sim_comparisons", "sim_runs", "sim_trade_leg_charges", "sim_trade_legs", "sim_trades", "trade_features", "training_sets"]);
        foreach (var name in names)
        {
            foreach (var (privilege, expected) in new[] { ("SELECT", true), ("INSERT", true), ("UPDATE", false), ("DELETE", false), ("TRUNCATE", false) })
            {
                await using var check = new NpgsqlCommand("SELECT has_table_privilege('xd_app', @t, @p)", connection);
                check.Parameters.AddWithValue("t", "research." + name);
                check.Parameters.AddWithValue("p", privilege);
                ((bool)(await check.ExecuteScalarAsync(Ct))!).ShouldBe(expected, $"{privilege} on research.{name}");
            }
        }
    }

    [Fact]
    public async Task As_xd_app_select_and_insert_work_update_and_delete_fail_with_insufficient_privilege()
    {
        await using var connection = new NpgsqlConnection(_app);
        await connection.OpenAsync(Ct);
        var before = await StoreKit.CountAsync(_app, "SELECT count(*) FROM research.sim_runs", Ct);
        await using var insert = new NpgsqlCommand(InsertRun, connection);
        var id = (long)(await insert.ExecuteScalarAsync(Ct))!;

        id.ShouldBeGreaterThan(0);
        (await StoreKit.CountAsync(_app, "SELECT count(*) FROM research.sim_runs", Ct)).ShouldBe(before + 1);
        (await SqlStateAsync(_app, "INSERT INTO research.sim_trades (run_id) VALUES (1)")).ShouldBe(PostgresErrorCodes.NotNullViolation, "the privilege is there; the row is incomplete");
        (await SqlStateAsync(_app, "INSERT INTO research.sim_trade_legs (run_id) VALUES (1)")).ShouldBe(PostgresErrorCodes.NotNullViolation, "the privilege to insert a leg is there; the row is incomplete");
        (await SqlStateAsync(_app, "INSERT INTO research.sim_trade_leg_charges (run_id) VALUES (1)")).ShouldBe(PostgresErrorCodes.NotNullViolation, "the privilege to insert the charges of a leg is there; the row is incomplete");
        (await SqlStateAsync(_app, "INSERT INTO research.sim_comparisons (strategy_id) VALUES ('ORB')")).ShouldBe(PostgresErrorCodes.NotNullViolation, "the privilege to insert a comparison is there; the row is incomplete");
        (await SqlStateAsync(_app, "INSERT INTO research.exit_selections (strategy_id) VALUES ('ORB')")).ShouldBe(PostgresErrorCodes.NotNullViolation, "the privilege to insert an exit choice is there; the row is incomplete");
        (await SqlStateAsync(_app, "INSERT INTO research.exit_selection_folds (selection_id) VALUES (1)")).ShouldBe(PostgresErrorCodes.NotNullViolation, "the privilege to insert a fold row is there; the row is incomplete");
        (await SqlStateAsync(_app, "INSERT INTO research.cell_stats (strategy) VALUES ('ORB')")).ShouldBe(PostgresErrorCodes.NotNullViolation, "the privilege to insert a cell statistic is there; the row is incomplete");
        (await SqlStateAsync(_app, "INSERT INTO research.trade_features (strategy) VALUES ('ORB')")).ShouldBe(PostgresErrorCodes.NotNullViolation, "the privilege to insert a trade's features is there; the row is incomplete");
        (await SqlStateAsync(_app, "INSERT INTO research.training_sets (rule_version) VALUES ('tb-1')")).ShouldBe(PostgresErrorCodes.NotNullViolation, "the privilege to insert a training set is there; the row is incomplete");
        foreach (var statement in new[]
        {
            "UPDATE research.trade_features SET strategy = 'X' WHERE false",
            "DELETE FROM research.trade_features WHERE false",
            "TRUNCATE research.trade_features",
            "UPDATE research.model_versions SET created_by = 'X' WHERE false",
            "DELETE FROM research.model_versions WHERE false",
            "TRUNCATE research.model_versions",
            "UPDATE research.model_designations SET role = 'champion' WHERE false",
            "DELETE FROM research.model_designations WHERE false",
            "TRUNCATE research.model_designations",
            "UPDATE research.training_sets SET created_by = 'X' WHERE false",
            "DELETE FROM research.training_sets WHERE false",
            "TRUNCATE research.training_sets",
            "UPDATE research.cell_stats SET enough = true WHERE false",
            "DELETE FROM research.cell_stats WHERE false",
            "TRUNCATE research.cell_stats",
            "UPDATE research.exit_selections SET stable = true WHERE false",
            "DELETE FROM research.exit_selections WHERE false",
            "TRUNCATE research.exit_selections",
            "UPDATE research.exit_selection_folds SET is_winner = true WHERE false",
            "DELETE FROM research.exit_selection_folds WHERE false",
            "TRUNCATE research.exit_selection_folds",
            "UPDATE research.sim_runs SET created_by = 'x'",
            "DELETE FROM research.sim_runs",
            "UPDATE research.sim_trades SET ordinal = 1 WHERE false",
            "DELETE FROM research.sim_trades WHERE false",
            "TRUNCATE research.sim_trades",
            "UPDATE research.sim_trade_legs SET ordinal = 1 WHERE false",
            "DELETE FROM research.sim_trade_legs WHERE false",
            "TRUNCATE research.sim_trade_legs",
            "UPDATE research.sim_trade_leg_charges SET ordinal = 1 WHERE false",
            "DELETE FROM research.sim_trade_leg_charges WHERE false",
            "UPDATE research.sim_comparisons SET unit = 'x' WHERE false",
            "DELETE FROM research.sim_comparisons WHERE false",
            "TRUNCATE research.sim_comparisons",
        })
        {
            (await SqlStateAsync(_app, statement)).ShouldBe(PostgresErrorCodes.InsufficientPrivilege, statement);
        }

        (await StoreKit.CountAsync(_app, "SELECT count(*) FROM research.sim_runs", Ct)).ShouldBe(before + 1);
    }

    [Fact]
    public async Task As_xd_app_creating_dropping_and_altering_a_research_object_fail_with_insufficient_privilege()
    {
        foreach (var statement in new[]
        {
            "CREATE TABLE research.x (i integer)",
            "DROP TABLE research.sim_trades",
            "ALTER TABLE research.sim_trades ADD COLUMN extra integer",
            "DROP SCHEMA research CASCADE",
        })
        {
            (await SqlStateAsync(_app, statement)).ShouldBe(PostgresErrorCodes.InsufficientPrivilege, statement);
        }

        (await StoreKit.CountAsync(_owner, "SELECT count(*) FROM pg_tables WHERE schemaname = 'research'", Ct)).ShouldBe(12);
    }

    [Fact]
    public async Task The_store_itself_works_as_xd_app_the_header_insert_with_returning_the_binary_copy_the_count_and_the_reads()
    {
        var run = StoredRunFixtures.Run(costed: true, to: RunStoreRoundTripTests.NextTo());
        await using var host = factory.WithWebHostBuilder(b => b.UseSetting("Database:ConnectionString", _app));

        var first = await StoreKit.WithStoreAsync(host.Services, s => s.StoreAsync(StoreKit.ToStore(run), Ct).AsTask());
        var second = await StoreKit.WithStoreAsync(host.Services, s => s.StoreAsync(StoreKit.ToStore(run), Ct).AsTask());
        var header = await StoreKit.WithStoreAsync(host.Services, s => s.FindAsync(run.RunKey, Ct).AsTask());
        var trades = await StoreKit.WithStoreAsync(host.Services, async s => await s.StreamTradesAsync(run.RunKey, false, Ct).ToListAsync(Ct));

        first.IsSuccess.ShouldBeTrue(first.IsFailure ? first.Error.Message : string.Empty);
        first.Value.WasAlreadyStored.ShouldBeFalse();
        second.Value.WasAlreadyStored.ShouldBeTrue();
        header!.TradeCount.ShouldBe(run.Trades.Count);
        SameTrade.ShouldBeSame(trades, run.Trades, "trades read as xd_app");
    }

    [Fact]
    public async Task The_runtime_role_has_usage_of_the_schema_and_cannot_create_in_it()
    {
        (await StoreKit.CountAsync(_owner, "SELECT CASE WHEN has_schema_privilege('xd_app', 'research', 'USAGE') THEN 1 ELSE 0 END", Ct)).ShouldBe(1);
        (await StoreKit.CountAsync(_owner, "SELECT CASE WHEN has_schema_privilege('xd_app', 'research', 'CREATE') THEN 1 ELSE 0 END", Ct)).ShouldBe(0);
    }

    [Fact]
    public void The_migration_grants_the_schema_before_it_makes_each_table_append_only()
    {
        var migrations = Path.GetFullPath(Path.Combine(ThisDirectory(), "..", "..", "..", "src", "XDrishti.Infrastructure", "Persistence", "Migrations"));
        var text = File.ReadAllText(Directory.EnumerateFiles(migrations, "*_AddResearchResultStore.cs").Single());

        var grant = text.IndexOf("GrantSchemaUsageToAppRole(Schemas.Research)", StringComparison.Ordinal);
        var runs = text.IndexOf("MakeAppendOnlyForAppRole(Schemas.Research, \"sim_runs\")", StringComparison.Ordinal);
        var trades = text.IndexOf("MakeAppendOnlyForAppRole(Schemas.Research, \"sim_trades\")", StringComparison.Ordinal);

        grant.ShouldBeGreaterThan(text.LastIndexOf("CreateTable(", StringComparison.Ordinal));
        runs.ShouldBeGreaterThan(grant);
        trades.ShouldBeGreaterThan(grant);
    }

    [Theory]
    [InlineData("AddSimTradeLegs", new[] { "sim_trade_legs", "sim_trade_leg_charges" })]
    [InlineData("AddSimComparisons", new[] { "sim_comparisons" })]
    [InlineData("AddExitSelections", new[] { "exit_selections", "exit_selection_folds" })]
    [InlineData("AddCellStats", new[] { "cell_stats" })]
    [InlineData("AddTradeFeaturesAndTrainingSets", new[] { "trade_features", "training_sets" })]
    [InlineData("AddModelRegistry", new[] { "model_versions", "model_designations" })]
    public void The_later_research_migrations_make_each_of_their_tables_append_only(string migration, string[] tables)
    {
        var migrations = Path.GetFullPath(Path.Combine(ThisDirectory(), "..", "..", "..", "src", "XDrishti.Infrastructure", "Persistence", "Migrations"));
        var text = File.ReadAllText(Directory.EnumerateFiles(migrations, "*_" + migration + ".cs").Single());

        foreach (var table in tables)
        {
            text.IndexOf("MakeAppendOnlyForAppRole(Schemas.Research, \"" + table + "\")", StringComparison.Ordinal).ShouldBeGreaterThan(text.LastIndexOf("CreateTable(", StringComparison.Ordinal), table);
        }
    }

    private static string ThisDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
}
