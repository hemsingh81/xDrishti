using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Domain.Learning.Extensions;
using XDrishti.Infrastructure.Learning;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Api.IntegrationTests.Research;

/// <summary>
/// P15-05 criterion 4 on the throw-away server of <see cref="ResearchGrantsTests"/>: <c>research.extension_extractions</c> exists and is insert-only for <c>xd_app</c>; the other roles have no right on it; the store round-trips with <c>ON CONFLICT DO NOTHING</c> (the second write of a record
/// is a no-op); the table refuses a bad run key, a version of another extension and a negative count; <c>Down</c> drops it only and <c>Up</c> brings it back insert-only again.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class ExtensionExtractionsTests(ResearchGrantsTests.DedicatedServer server) : IClassFixture<ResearchGrantsTests.DedicatedServer>
{
    private static readonly string RunKey = new('b', 64);

    private static readonly DateTimeOffset Now = new(2026, 11, 2, 10, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

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

    private async Task<bool> ScalarBooleanAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(server.Owner);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(sql, connection);
        return (bool)(await command.ExecuteScalarAsync(Ct))!;
    }

    private static ExtensionExtractionRecord Record(string version = "fu-1", string runKey = "") =>
        new(runKey.Length == 0 ? RunKey : runKey, version[..2], version, 5, version == "fu-1" ? "xbrl-1-synthetic" : null, new string('c', 64), new DateTimeOffset(2026, 7, 24, 10, 42, 0, TimeSpan.Zero), 12, "{\"nan\":{},\"parser_unverified\":{}}", Now);

    [Fact]
    public async Task The_runtime_role_reads_and_inserts_and_can_neither_change_nor_delete_nor_truncate_the_table()
    {
        foreach (var (privilege, expected) in new[] { ("SELECT", true), ("INSERT", true), ("UPDATE", false), ("DELETE", false), ("TRUNCATE", false) })
        {
            (await ScalarBooleanAsync($"SELECT has_table_privilege('xd_app', 'research.extension_extractions', '{privilege}')")).ShouldBe(expected, privilege);
        }

        (await SqlStateAsync(server.App, "DELETE FROM research.extension_extractions")).ShouldBe("42501");
        (await SqlStateAsync(server.App, "TRUNCATE research.extension_extractions")).ShouldBe("42501");
        (await SqlStateAsync(server.App, "UPDATE research.extension_extractions SET rows = rows")).ShouldBe("42501");
    }

    [Fact]
    public async Task The_execution_and_read_only_roles_have_no_right_on_the_table()
    {
        await StoreKit.ExecuteAsync(
            server.Owner,
            "DO $$ BEGIN IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'xd_exec') THEN CREATE ROLE xd_exec NOLOGIN; END IF; IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'xd_mcp_read') THEN CREATE ROLE xd_mcp_read NOLOGIN; END IF; IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'xd_mcp_sql') THEN CREATE ROLE xd_mcp_sql NOLOGIN; END IF; END $$",
            Ct);
        foreach (var role in new[] { "xd_exec", "xd_mcp_read", "xd_mcp_sql" })
        {
            foreach (var privilege in new[] { "SELECT", "INSERT", "UPDATE", "DELETE", "TRUNCATE", "REFERENCES", "TRIGGER" })
            {
                (await ScalarBooleanAsync($"SELECT has_table_privilege('{role}', 'research.extension_extractions', '{privilege}')")).ShouldBeFalse($"{role} {privilege}");
            }
        }
    }

    private static ExtensionFeatureRow Row(string runKey, string trade, string version = "fu-1") => new(runKey, trade, version[..2], version, Now, [1.5, double.NaN]);

    [Fact]
    public async Task The_store_writes_the_rows_and_one_record_in_one_transaction_and_reads_the_record_back()
    {
        await using var dataSource = NpgsqlDataSource.Create(server.App);
        var store = new ExtensionFeatureStore(dataSource);
        var records = new ExtensionExtractionStore(dataSource);
        var key = new string('d', 64);

        var first = await store.AppendWithRecordAsync([Row(key, "t1"), Row(key, "t2")], Record(runKey: key), Now, Ct);
        var again = await store.AppendWithRecordAsync([Row(key, "t1"), Row(key, "t2")], Record(runKey: key) with { Rows = 99 }, Now, Ct);
        var other = await store.AppendWithRecordAsync([Row(key, "t1", "ev-1")], Record("ev-1", key), Now, Ct);
        var found = await records.FindAsync(key, "fu-1", Ct);

        (first, again, other).ShouldBe(((2, true), (0, false), (1, true)));
        found.ShouldNotBeNull().Rows.ShouldBe(12, "the first record is kept as it was");
        found.ParserVersion.ShouldBe("xbrl-1-synthetic");
        found.CalendarSha256.ShouldBe(new string('c', 64));
        found.FilingsWatermark.ShouldBe(new DateTimeOffset(2026, 7, 24, 10, 42, 0, TimeSpan.Zero));
        found.NanCountsJson.ShouldContain("parser_unverified");
        (await records.FindAsync(key, "ev-1", Ct)).ShouldNotBeNull().ParserVersion.ShouldBeNull();
        (await records.FindAsync(new string('e', 64), "fu-1", Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task A_row_the_table_refuses_rolls_the_record_back_with_it()
    {
        await using var dataSource = NpgsqlDataSource.Create(server.App);
        var store = new ExtensionFeatureStore(dataSource);
        var key = new string('f', 64);

        await Should.ThrowAsync<PostgresException>(async () => await store.AppendWithRecordAsync([Row(key, "t1"), Row(key, "t2") with { Values = [] }], Record(runKey: key), Now, Ct));

        (await new ExtensionExtractionStore(dataSource).FindAsync(key, "fu-1", Ct)).ShouldBeNull("no record without its rows");
        (await StoreKit.CountAsync(server.Owner, $"SELECT count(*) FROM research.trade_features_ext WHERE run_key = '{key}'", Ct)).ShouldBe(0);
    }

    [Fact]
    public async Task The_table_refuses_a_bad_run_key_a_version_of_another_extension_and_a_negative_count()
    {
        const string Insert = "INSERT INTO research.extension_extractions (run_key, extension, version, lag_minutes, calendar_sha256, rows, nan_counts) VALUES ";
        var sha = new string('c', 64);

        (await SqlStateAsync(server.App, Insert + $"('{new string('A', 64)}', 'fu', 'fu-1', 5, '{sha}', 1, '{{}}')")).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await SqlStateAsync(server.App, Insert + $"('{RunKey}', 'fu', 'ev-1', 5, '{sha}', 1, '{{}}')")).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await SqlStateAsync(server.App, Insert + $"('{RunKey}', 'fu', 'fu-1', 5, '{sha}', -1, '{{}}')")).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await SqlStateAsync(server.App, Insert + $"('{RunKey}', 'fu', 'fu-1', 5, 'not-a-hash', 1, '{{}}')")).ShouldBe(PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task Down_drops_the_table_only_and_up_brings_it_back_insert_only()
    {
        var options = new DbContextOptionsBuilder<XDrishtiDbContext>()
            .UseNpgsql(server.Owner, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "platform"))
            .UseSnakeCaseNamingConvention()
            .Options;
        await using var db = new XDrishtiDbContext(options);
        var migrator = db.GetService<IMigrator>();

        await migrator.MigrateAsync("20261102123646_ReshapeResultFacts", Ct);
        (await ScalarBooleanAsync("SELECT to_regclass('research.extension_extractions') IS NULL")).ShouldBeTrue("dropped");
        (await ScalarBooleanAsync("SELECT to_regclass('research.trade_features_ext') IS NOT NULL AND to_regclass('research.extension_evaluations') IS NOT NULL")).ShouldBeTrue("the tables of P13-05 stay");

        await migrator.MigrateAsync(null, Ct);
        (await ScalarBooleanAsync("SELECT to_regclass('research.extension_extractions') IS NOT NULL")).ShouldBeTrue("back");
        (await ScalarBooleanAsync("SELECT has_table_privilege('xd_app', 'research.extension_extractions', 'INSERT') AND NOT has_table_privilege('xd_app', 'research.extension_extractions', 'UPDATE')")).ShouldBeTrue("insert-only again");
    }
}
