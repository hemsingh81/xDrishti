using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.IntegrationTests.Research;

namespace XDrishti.Api.IntegrationTests.Confidence;

/// <summary>
/// CF-11 part 1 (ADR 0221): what the runtime role <c>xd_app</c> may do to the two tables of schema <c>readiness</c>, and what the database refuses whatever the application does. Runs on the dedicated server of <see cref="ResearchGrantsTests"/>, where the role has the default
/// privileges of the init script. Both tables are insert-only: no update, no delete, no truncate. The owner's switch takes only an owner; the database stamps the time and the id order; a daily snapshot is one per subject and day; a gate snapshot is not a system one.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class ReadinessGrantsTests(ResearchGrantsTests.DedicatedServer server) : IClassFixture<ResearchGrantsTests.DedicatedServer>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly string Hash = new('b', 64);

    private static string Snapshot(string strategy, string scope = "daily", string day = "2026-11-06", string label = "not_ready", double score = 0, string subject = "strategy", int? version = 1, string? hash = null)
    {
        var strategyValue = subject == "strategy" ? $"'{strategy}'" : "NULL";
        var versionValue = version is { } v && subject == "strategy" ? v.ToString(System.Globalization.CultureInfo.InvariantCulture) : "NULL";
        return $"INSERT INTO readiness.scorecard_snapshots (subject, strategy_id, version, taken_on, scope, score, label, body, settings_hash) VALUES ('{subject}', {strategyValue}, {versionValue}, '{day}', '{scope}', {score.ToString(System.Globalization.CultureInfo.InvariantCulture)}, '{label}', '{{}}', '{hash ?? Hash}') RETURNING id";
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

    private static async Task<long> ScalarAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync(Ct), System.Globalization.CultureInfo.InvariantCulture);
    }

    private Task<long> SwitchAsync(bool enforce) => ScalarAsync(server.App, $"INSERT INTO readiness.settings (enforce, actor, reason) VALUES ({enforce.ToString().ToLowerInvariant()}, 'owner:me@x.test', 'why') RETURNING id");

    [Fact]
    public async Task The_schema_holds_the_two_tables_and_the_runtime_role_can_only_read_and_insert()
    {
        await using var connection = new NpgsqlConnection(server.Owner);
        await connection.OpenAsync(Ct);
        await using var tables = new NpgsqlCommand("SELECT tablename FROM pg_tables WHERE schemaname = 'readiness' ORDER BY 1", connection);
        var names = new List<string>();
        await using (var reader = await tables.ExecuteReaderAsync(Ct))
        {
            while (await reader.ReadAsync(Ct))
            {
                names.Add(reader.GetString(0));
            }
        }

        names.ShouldBe(["scorecard_snapshots", "settings"]);
        foreach (var name in names)
        {
            foreach (var (privilege, expected) in new[] { ("SELECT", true), ("INSERT", true), ("UPDATE", false), ("DELETE", false), ("TRUNCATE", false), ("REFERENCES", false), ("TRIGGER", false) })
            {
                await using var check = new NpgsqlCommand("SELECT has_table_privilege('xd_app', @t, @p)", connection);
                check.Parameters.AddWithValue("t", "readiness." + name);
                check.Parameters.AddWithValue("p", privilege);
                ((bool)(await check.ExecuteScalarAsync(Ct))!).ShouldBe(expected, $"{privilege} on {name}");
            }

            await using var columns = new NpgsqlCommand("SELECT count(*) FROM pg_attribute a WHERE a.attrelid = @q::regclass AND a.attnum > 0 AND NOT a.attisdropped AND has_column_privilege('xd_app', a.attrelid, a.attnum, 'UPDATE')", connection);
            columns.Parameters.AddWithValue("q", "readiness." + name);
            ((long)(await columns.ExecuteScalarAsync(Ct))!).ShouldBe(0, $"no column of {name} is updatable");
        }
    }

    [Fact]
    public async Task As_xd_app_the_tables_cannot_be_updated_deleted_or_truncated()
    {
        await SwitchAsync(true);
        await ScalarAsync(server.App, Snapshot("GR1"));

        foreach (var statement in new[]
        {
            "UPDATE readiness.settings SET enforce = false", "DELETE FROM readiness.settings", "TRUNCATE readiness.settings",
            "UPDATE readiness.scorecard_snapshots SET score = 1", "UPDATE readiness.scorecard_snapshots SET body = '{}'", "DELETE FROM readiness.scorecard_snapshots", "TRUNCATE readiness.scorecard_snapshots",
        })
        {
            (await SqlStateAsync(server.App, statement)).ShouldBe(PostgresErrorCodes.InsufficientPrivilege, statement);
        }
    }

    [Fact]
    public async Task The_switch_takes_only_an_owner_with_a_reason_and_its_id_follows_commit_order()
    {
        var first = await SwitchAsync(true);
        var second = await SwitchAsync(false);

        second.ShouldBeGreaterThan(first);
        (await SqlStateAsync(server.App, "INSERT INTO readiness.settings (enforce, actor, reason) VALUES (true, 'auto', 'why')")).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await SqlStateAsync(server.App, "INSERT INTO readiness.settings (enforce, actor, reason) VALUES (true, 'owner:', 'why')")).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await SqlStateAsync(server.App, "INSERT INTO readiness.settings (enforce, actor, reason) VALUES (true, 'owner:me@x.test', '  ')")).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await SqlStateAsync(server.App, "INSERT INTO readiness.settings (enforce, actor, reason) VALUES (true, 'cli:xd', 'why')")).ShouldBe(PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task The_database_stamps_the_time_of_a_switch_and_of_a_snapshot_whatever_the_caller_says()
    {
        var id = await ScalarAsync(server.App, "INSERT INTO readiness.settings (enforce, actor, reason, changed_at) VALUES (true, 'owner:me@x.test', 'why', '2001-01-01') RETURNING id");
        var snapshot = await ScalarAsync(server.App, "INSERT INTO readiness.scorecard_snapshots (subject, strategy_id, version, taken_on, taken_at, scope, score, label, body, settings_hash) VALUES ('strategy', 'GR2', 1, '2026-11-06', '2001-01-01', 'daily', 0, 'not_ready', '{}', '" + Hash + "') RETURNING id");

        (await ScalarAsync(server.App, $"SELECT count(*) FROM readiness.settings WHERE id = {id} AND changed_at > now() - interval '1 minute'")).ShouldBe(1);
        (await ScalarAsync(server.App, $"SELECT count(*) FROM readiness.scorecard_snapshots WHERE id = {snapshot} AND taken_at > now() - interval '1 minute'")).ShouldBe(1);
    }

    [Fact]
    public async Task A_daily_snapshot_is_one_per_subject_strategy_version_and_day_and_a_gate_snapshot_is_one_per_use()
    {
        await ScalarAsync(server.App, Snapshot("GR3"));

        (await SqlStateAsync(server.App, Snapshot("GR3"))).ShouldBe(PostgresErrorCodes.UniqueViolation, "the same day");
        (await SqlStateAsync(server.App, Snapshot("GR3", day: "2026-11-07"))).ShouldBeNull();
        (await SqlStateAsync(server.App, Snapshot("GR3", version: 2))).ShouldBeNull("another version");
        (await SqlStateAsync(server.App, Snapshot("GR3", version: null))).ShouldBeNull("a strategy with no version");
        (await SqlStateAsync(server.App, Snapshot("GR3", version: null))).ShouldBe(PostgresErrorCodes.UniqueViolation, "no version twice on a day");
        (await SqlStateAsync(server.App, Snapshot("GR3", scope: "gate"))).ShouldBeNull();
        (await SqlStateAsync(server.App, Snapshot("GR3", scope: "gate"))).ShouldBeNull("a gate snapshot is one per use");
        (await SqlStateAsync(server.App, Snapshot("GR3", subject: "system"))).ShouldBeNull();
        (await SqlStateAsync(server.App, Snapshot("GR3", subject: "system"))).ShouldBe(PostgresErrorCodes.UniqueViolation, "one system snapshot a day");
    }

    [Theory]
    [InlineData("other", "daily", "not_ready", 0, "strategy")]
    [InlineData("strategy", "other", "not_ready", 0, "strategy")]
    [InlineData("strategy", "daily", "great", 0, "strategy")]
    [InlineData("strategy", "daily", "ready", 100.5, "strategy")]
    [InlineData("strategy", "daily", "ready", -1, "strategy")]
    [InlineData("system", "gate", "ready", 100, "system")]
    public async Task A_snapshot_with_a_bad_subject_scope_label_or_score_is_refused(string subjectName, string scope, string label, double score, string kind)
    {
        var sql = Snapshot("GR4", scope: scope, label: label, score: score, subject: kind).Replace($"VALUES ('{kind}'", $"VALUES ('{subjectName}'", StringComparison.Ordinal);

        (await SqlStateAsync(server.App, sql)).ShouldBe(PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task A_snapshot_with_a_bad_hash_a_strategy_without_a_name_or_a_system_with_one_is_refused()
    {
        (await SqlStateAsync(server.App, Snapshot("GR5", hash: "zz"))).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await SqlStateAsync(server.App, "INSERT INTO readiness.scorecard_snapshots (subject, strategy_id, version, taken_on, scope, score, label, body, settings_hash) VALUES ('strategy', NULL, 1, '2026-11-06', 'daily', 0, 'not_ready', '{}', '" + Hash + "')")).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await SqlStateAsync(server.App, "INSERT INTO readiness.scorecard_snapshots (subject, strategy_id, version, taken_on, scope, score, label, body, settings_hash) VALUES ('system', 'X', NULL, '2026-11-06', 'daily', 0, 'not_ready', '{}', '" + Hash + "')")).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await SqlStateAsync(server.App, "INSERT INTO readiness.scorecard_snapshots (subject, strategy_id, version, taken_on, scope, score, label, body, settings_hash) VALUES ('strategy', 'GR5', 0, '2026-11-06', 'daily', 0, 'not_ready', '{}', '" + Hash + "')")).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await SqlStateAsync(server.App, "INSERT INTO readiness.scorecard_snapshots (subject, strategy_id, version, taken_on, scope, score, label, body, settings_hash) VALUES ('strategy', 'GR5', 1, '2026-11-06', 'daily', 0, 'not_ready', '[]', '" + Hash + "')")).ShouldBe(PostgresErrorCodes.CheckViolation);
    }
}
