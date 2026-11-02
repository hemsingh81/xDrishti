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
/// P13-05 criteria 12 and the ledger-family check, on the throw-away server of <see cref="ResearchGrantsTests"/> (the runtime role with the init script's default privileges): both tables exist and are insert-only for <c>xd_app</c>; the other roles have no right on them;
/// the stores round-trip with <c>ON CONFLICT DO NOTHING</c>; the hold-out partial unique index refuses a second reading; <c>Down</c> drops both and <c>Up</c> brings them back; no stored strategy id can look like a ledger family <c>ext:*</c>.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class ExtensionTablesTests(ResearchGrantsTests.DedicatedServer server) : IClassFixture<ResearchGrantsTests.DedicatedServer>
{
    private static readonly string[] Tables = ["trade_features_ext", "extension_evaluations"];

    private static readonly string RunKey = new('a', 64);

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

    private static ExtensionEvaluationRecord Record(string id, string family = "ext:md-1", string versionKey = "md-1:e1", bool holdOut = false) =>
        new(id, family, "X", "md-1", versionKey, "tf-1", new string('b', 64), new string('c', 64), new string('d', 64), 4, 1.5, "month", 7, 6, 2400, 310, """{"delta_r":0.05,"lower":"unavailable"}""", "KEEP", [], ["a limit"], """{"folds":[]}""", """{"seed":1337}""", holdOut);

    [Fact]
    public async Task The_runtime_role_reads_and_inserts_and_can_neither_change_nor_delete_nor_truncate_either_table()
    {
        foreach (var table in Tables)
        {
            foreach (var (privilege, expected) in new[] { ("SELECT", true), ("INSERT", true), ("UPDATE", false), ("DELETE", false), ("TRUNCATE", false) })
            {
                (await ScalarBooleanAsync($"SELECT has_table_privilege('xd_app', 'research.{table}', '{privilege}')")).ShouldBe(expected, table + " " + privilege);
            }

            (await SqlStateAsync(server.App, $"DELETE FROM research.{table}")).ShouldBe("42501");
            (await SqlStateAsync(server.App, $"TRUNCATE research.{table}")).ShouldBe("42501");
        }

        (await SqlStateAsync(server.App, "UPDATE research.trade_features_ext SET version = version")).ShouldBe("42501");
        (await SqlStateAsync(server.App, "UPDATE research.extension_evaluations SET verdict = verdict")).ShouldBe("42501");
    }

    [Fact]
    public async Task The_execution_and_read_only_roles_have_no_right_on_either_table()
    {
        await StoreKit.ExecuteAsync(
            server.Owner,
            "DO $$ BEGIN IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'xd_exec') THEN CREATE ROLE xd_exec NOLOGIN; END IF; IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'xd_mcp_read') THEN CREATE ROLE xd_mcp_read NOLOGIN; END IF; IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'xd_mcp_sql') THEN CREATE ROLE xd_mcp_sql NOLOGIN; END IF; END $$",
            Ct);
        foreach (var role in new[] { "xd_exec", "xd_mcp_read", "xd_mcp_sql" })
        {
            foreach (var table in Tables)
            {
                foreach (var privilege in new[] { "SELECT", "INSERT", "UPDATE", "DELETE", "TRUNCATE", "REFERENCES", "TRIGGER" })
                {
                    (await ScalarBooleanAsync($"SELECT has_table_privilege('{role}', 'research.{table}', '{privilege}')")).ShouldBeFalse($"{role} {privilege} on {table}");
                }
            }
        }
    }

    [Fact]
    public async Task The_feature_store_writes_once_per_key_and_reads_back_in_order()
    {
        await using var dataSource = NpgsqlDataSource.Create(server.App);
        var store = new ExtensionFeatureStore(dataSource);
        var at = new DateTimeOffset(2026, 11, 2, 4, 0, 0, TimeSpan.Zero);
        var rows = new[]
        {
            new ExtensionFeatureRow(RunKey, "t2", "md", "md-1", at, [1.5, double.NaN]),
            new ExtensionFeatureRow(RunKey, "t1", "md", "md-1", at, [0.25, 2.0]),
        };

        var first = await store.AppendAsync(rows, Now, Ct);
        var again = await store.AppendAsync(rows, Now, Ct);
        var read = await store.ReadAsync([RunKey], "md-1", Ct);
        var other = await store.ReadAsync([RunKey], "md-2", Ct);

        (first, again).ShouldBe((2, 0));
        read.Select(r => r.TradeKey).ShouldBe(["t1", "t2"]);
        read[0].Values.ShouldBe([0.25, 2.0]);
        double.IsNaN(read[1].Values[1]).ShouldBeTrue("an unknown value stays NaN");
        read[0].DecisionAt.ShouldBe(at);
        other.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_feature_table_refuses_a_bad_run_key_a_version_of_another_extension_and_an_empty_array()
    {
        const string Insert = "INSERT INTO research.trade_features_ext (run_key, trade_key, extension, version, decision_at, feature_values) VALUES ";

        (await SqlStateAsync(server.App, Insert + $"('{new string('A', 64)}', 't', 'md', 'md-1', now(), ARRAY[1.0])")).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await SqlStateAsync(server.App, Insert + $"('{RunKey}', 't', 'md', 'ev-1', now(), ARRAY[1.0])")).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await SqlStateAsync(server.App, Insert + $"('{RunKey}', 't', 'MD', 'MD-1', now(), ARRAY[1.0])")).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await SqlStateAsync(server.App, Insert + $"('{RunKey}', 't', 'md', 'md-1', now(), ARRAY[]::double precision[])")).ShouldBe(PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task The_evaluation_store_writes_once_per_id_lists_newest_first_and_reads_back_the_record()
    {
        await using var dataSource = NpgsqlDataSource.Create(server.App);
        var store = new ExtensionEvaluationStore(dataSource);
        var one = Record(new string('1', 64), "ext:md-9");
        var two = Record(new string('2', 64), "ext:md-9");

        var first = await store.AppendAsync(one, Now, Ct);
        var again = await store.AppendAsync(one, Now.AddHours(1), Ct);
        await store.AppendAsync(two, Now.AddHours(2), Ct);
        var listed = await store.ListAsync("ext:md-9", 10, Ct);
        var got = await store.GetAsync(one.EvaluationId, Ct);

        (first.Value, again.Value).ShouldBe((true, false));
        listed.Select(r => r.EvaluationId).ShouldBe([two.EvaluationId, one.EvaluationId]);
        got.ShouldNotBeNull().Verdict.ShouldBe("KEEP");
        got.Flips.ShouldBe(310);
        got.Limits.ShouldBe(["a limit"]);
        got.Missing.ShouldBeEmpty();
        got.Factor.ShouldBe(1.5);
        got.EstimatesJson.ShouldContain("unavailable");
        (await store.GetAsync(new string('9', 64), Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task The_hold_out_is_read_once_per_family_and_version_key()
    {
        await using var dataSource = NpgsqlDataSource.Create(server.App);
        var store = new ExtensionEvaluationStore(dataSource);

        (await store.HoldOutUsedAsync("ext:md-7", "md-7:e1", Ct)).ShouldBeFalse();
        var first = await store.AppendAsync(Record(new string('3', 64), "ext:md-7", "md-7:e1", holdOut: true), Now, Ct);
        var second = await store.AppendAsync(Record(new string('4', 64), "ext:md-7", "md-7:e1", holdOut: true), Now, Ct);
        var otherKey = await store.AppendAsync(Record(new string('5', 64), "ext:md-7", "md-7:e1,e2", holdOut: true), Now, Ct);
        var developmentRow = await store.AppendAsync(Record(new string('6', 64), "ext:md-7", "md-7:e1"), Now, Ct);

        first.Value.ShouldBeTrue();
        second.IsFailure.ShouldBeTrue();
        second.Error.Code.ShouldBe("extensions.holdout_used");
        otherKey.Value.ShouldBeTrue();
        developmentRow.Value.ShouldBeTrue("a development record is not a reading of the hold-out");
        (await store.HoldOutUsedAsync("ext:md-7", "md-7:e1", Ct)).ShouldBeTrue();
        (await StoreKit.CountAsync(server.Owner, "SELECT count(*) FROM research.extension_evaluations WHERE family = 'ext:md-7' AND version_key = 'md-7:e1'", Ct)).ShouldBe(2);
    }

    [Fact]
    public async Task The_evaluation_table_refuses_a_verdict_word_a_fold_unit_a_hash_shape_and_a_trial_count_it_does_not_know()
    {
        await using var dataSource = NpgsqlDataSource.Create(server.App);
        var store = new ExtensionEvaluationStore(dataSource);

        foreach (var bad in new[]
        {
            Record(new string('7', 64)) with { Verdict = "MAYBE" },
            Record(new string('7', 64)) with { FoldUnit = "week" },
            Record(new string('7', 64)) with { Trials = 0 },
            Record(new string('7', 64)) with { TrialHash = "not-a-hash" },
            Record(new string('7', 64)) with { FoldsTotal = -1 },
        })
        {
            var failure = await Should.ThrowAsync<PostgresException>(async () => await store.AppendAsync(bad, Now, Ct));

            failure.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
        }
    }

    [Fact]
    public async Task No_stored_strategy_id_can_contain_a_colon_so_an_ext_family_never_collides_with_one()
    {
        (await ScalarBooleanAsync("SELECT 'ext:md-1' ~ '^[A-Za-z][A-Za-z0-9_]{0,63}$'")).ShouldBeFalse("the pattern of the check constraint refuses the family spelling");
        (await ScalarBooleanAsync("SELECT count(*) = 1 FROM pg_constraint WHERE conname = 'ck_strategies_id' AND pg_get_constraintdef(oid) LIKE '%[A-Za-z][A-Za-z0-9_]{0,63}%'")).ShouldBeTrue();
        (await StoreKit.CountAsync(server.Owner, "SELECT count(*) FROM lab.test_ledger WHERE strategy_id LIKE '%:%' AND strategy_id NOT LIKE 'ext:%'", Ct)).ShouldBe(0);
    }

    [Fact]
    public async Task Down_drops_both_tables_and_up_brings_them_back()
    {
        var options = new DbContextOptionsBuilder<XDrishtiDbContext>()
            .UseNpgsql(server.Owner, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "platform"))
            .UseSnakeCaseNamingConvention()
            .Options;
        await using var db = new XDrishtiDbContext(options);
        var migrator = db.GetService<IMigrator>();

        await migrator.MigrateAsync("20261102080553_AddEventFilterDecisions", Ct);
        foreach (var table in Tables)
        {
            (await ScalarBooleanAsync($"SELECT to_regclass('research.{table}') IS NULL")).ShouldBeTrue(table + " dropped");
        }

        await migrator.MigrateAsync(null, Ct);
        foreach (var table in Tables)
        {
            (await ScalarBooleanAsync($"SELECT to_regclass('research.{table}') IS NOT NULL")).ShouldBeTrue(table + " back");
            (await ScalarBooleanAsync($"SELECT has_table_privilege('xd_app', 'research.{table}', 'INSERT') AND NOT has_table_privilege('xd_app', 'research.{table}', 'UPDATE')")).ShouldBeTrue(table + " insert-only again");
        }
    }
}
