using Npgsql;
using XDrishti.Mcp.Tests.Support;

namespace XDrishti.Mcp.Tests;

/// <summary>Spec P9-02 AC 3 and 4: the switch lives in the database. Insert-only for the runtime role, stamped by a trigger, followed by the views at once.</summary>
[Collection(ReadOnlyDatabaseSuite.Name)]
public sealed class AiSettingsTests(ReadOnlyDatabase db)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Task ResetAsync() => ReadOnlyDatabase.ExecuteAsync(db.Owner, "TRUNCATE ai.settings CASCADE");

    private Task SetAsync(bool enabled, bool portfolio, int calls = 6) =>
        ReadOnlyDatabase.ExecuteAsync(db.App, $"INSERT INTO ai.settings (enabled, portfolio_context, max_tool_calls, changed_by, reason) VALUES ({(enabled ? "true" : "false")}, {(portfolio ? "true" : "false")}, {calls}, 'owner:probe@example.test', 'settings test')");

    private static async Task<(bool Assistant, bool Portfolio)> AllowsAsync(string connection)
    {
        var assistant = (bool)(await ReadOnlyDatabase.ScalarAsync(connection, "SELECT ai.context_allows('assistant')"))!;
        var portfolio = (bool)(await ReadOnlyDatabase.ScalarAsync(connection, "SELECT ai.context_allows('portfolio')"))!;
        return (assistant, portfolio);
    }

    private async Task<long> HoldingsSeenBySqlRoleAsync() => (long)(await ReadOnlyDatabase.ScalarAsync(db.AsSql, "SELECT count(*) FROM aiv.holdings"))!;

    [Fact]
    public async Task The_switch_follows_the_newest_row_at_once_and_with_no_row_everything_is_off()
    {
        await ResetAsync();
        await ReadOnlyDatabase.ExecuteAsync(db.Owner, """
            DELETE FROM portfolio.holdings;
            SET session_replication_role = replica;
            INSERT INTO portfolio.holdings (id, account_id, bucket_id, exchange, security_id, trading_symbol, name, qty, avg_cost, last_price, status, first_seen_at, synced_at)
                VALUES (gen_random_uuid(), gen_random_uuid(), gen_random_uuid(), 'NSE', 'S', 'SWITCH', 'Switch Ltd', 1, 1, 1, 'open', now(), now());
            """);
        try
        {
            (await AllowsAsync(db.Owner)).ShouldBe((false, false));
            (await AllowsAsync(db.AsRead)).ShouldBe((false, false));
            (await HoldingsSeenBySqlRoleAsync()).ShouldBe(0);

            await SetAsync(enabled: true, portfolio: true);
            (await AllowsAsync(db.AsRead)).ShouldBe((true, true));
            (await HoldingsSeenBySqlRoleAsync()).ShouldBe(1);

            await SetAsync(enabled: true, portfolio: false);
            (await AllowsAsync(db.AsRead)).ShouldBe((true, false));
            (await HoldingsSeenBySqlRoleAsync()).ShouldBe(0);

            await SetAsync(enabled: false, portfolio: true);
            (await AllowsAsync(db.AsRead)).ShouldBe((false, false), "the portfolio needs the assistant to be on");
            (await HoldingsSeenBySqlRoleAsync()).ShouldBe(0);

            await SetAsync(enabled: true, portfolio: true);
            (await HoldingsSeenBySqlRoleAsync()).ShouldBe(1, "a newer row switches it on again");
            (await ReadOnlyDatabase.ScalarAsync(db.AsRead, "SELECT ai.context_allows('anything')")).ShouldBe(false);
            (await ReadOnlyDatabase.ScalarAsync(db.AsRead, "SELECT ai.context_allows('')")).ShouldBe(false);
            (await ReadOnlyDatabase.ScalarAsync(db.AsRead, "SELECT ai.context_allows(NULL)")).ShouldBe(false);
        }
        finally
        {
            await ReadOnlyDatabase.ExecuteAsync(db.Owner, "DELETE FROM portfolio.holdings; TRUNCATE ai.settings CASCADE");
        }
    }

    [Fact]
    public async Task For_the_runtime_role_the_table_is_insert_only()
    {
        await ResetAsync();
        await SetAsync(true, false);

        (await ReadOnlyDatabase.SqlStateAsync(db.App, "SELECT count(*) FROM ai.settings")).ShouldBeNull();
        foreach (var statement in new[] { "UPDATE ai.settings SET enabled = false", "DELETE FROM ai.settings", "TRUNCATE ai.settings CASCADE", "ALTER TABLE ai.settings ADD COLUMN x integer", "DROP TABLE ai.settings", "CREATE TABLE ai.x (i integer)" })
        {
            (await ReadOnlyDatabase.SqlStateAsync(db.App, statement)).ShouldBe(PostgresErrorCodes.InsufficientPrivilege, statement);
        }

        await ResetAsync();
    }

    [Fact]
    public async Task The_function_and_the_trigger_cannot_be_replaced_dropped_or_disabled_by_the_runtime_role()
    {
        foreach (var statement in new[]
        {
            "CREATE OR REPLACE FUNCTION ai.context_allows(context_key text) RETURNS boolean LANGUAGE sql AS 'SELECT true'",
            "CREATE OR REPLACE FUNCTION ai.stamp_settings() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RETURN NEW; END $$",
            "DROP TRIGGER trg_ai_settings_stamp ON ai.settings",
            "ALTER TABLE ai.settings DISABLE TRIGGER trg_ai_settings_stamp",
            "DROP FUNCTION ai.context_allows(text)",
        })
        {
            (await ReadOnlyDatabase.SqlStateAsync(db.App, statement)).ShouldBe(PostgresErrorCodes.InsufficientPrivilege, statement);
        }

        (await ReadOnlyDatabase.SqlStateAsync(db.AsRead, "SELECT ai.context_allows('assistant')")).ShouldBeNull("the read role may call the switch");
        (await ReadOnlyDatabase.SqlStateAsync(db.App, "SELECT ai.context_allows('assistant')")).ShouldBe(PostgresErrorCodes.InsufficientPrivilege, "the runtime role has no use for it");
    }

    [Theory]
    [InlineData(0, "owner:probe@example.test", "why", false)]
    [InlineData(13, "owner:probe@example.test", "why", false)]
    [InlineData(1, "owner:probe@example.test", "why", true)]
    [InlineData(12, "owner:probe@example.test", "why", true)]
    [InlineData(6, "cli:xd", "why", false)]
    [InlineData(6, "rule:autonomy", "why", false)]
    [InlineData(6, "owner:", "why", false)]
    [InlineData(6, "owner:probe@example.test", "", false)]
    [InlineData(6, "owner:probe@example.test", "   ", false)]
    public async Task The_checks_of_the_table_hold(int calls, string actor, string reason, bool accepted)
    {
        await ResetAsync();
        var sql = $"INSERT INTO ai.settings (enabled, portfolio_context, max_tool_calls, changed_by, reason) VALUES (true, false, {calls}, '{actor}', '{reason}')";

        var state = await ReadOnlyDatabase.SqlStateAsync(db.App, sql);

        if (accepted)
        {
            state.ShouldBeNull();
        }
        else
        {
            state.ShouldBe(PostgresErrorCodes.CheckViolation);
        }

        await ResetAsync();
    }

    [Fact]
    public async Task A_reason_over_500_characters_is_refused()
    {
        var state = await ReadOnlyDatabase.SqlStateAsync(db.App, $"INSERT INTO ai.settings (enabled, portfolio_context, max_tool_calls, changed_by, reason) VALUES (true, false, 6, 'owner:probe@example.test', '{new string('r', 501)}')");

        state.ShouldBeOneOf([PostgresErrorCodes.StringDataRightTruncation, PostgresErrorCodes.CheckViolation]);
    }

    [Fact]
    public async Task The_database_stamps_the_id_the_time_and_the_hash_whatever_the_insert_says()
    {
        await ResetAsync();
        await ReadOnlyDatabase.ExecuteAsync(db.App, "INSERT INTO ai.settings (id, enabled, portfolio_context, max_tool_calls, changed_at, changed_by, reason, snapshot_hash) OVERRIDING SYSTEM VALUE VALUES (1, true, false, 6, '2000-01-01', 'owner:probe@example.test', 'stamp', 'x')");
        await SetAsync(true, true, 7);

        await using var connection = new NpgsqlConnection(db.Owner);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand("SELECT id, changed_at > now() - interval '1 minute', snapshot_hash, snapshot_hash = encode(sha256(convert_to('ai-settings:v1|enabled=' || enabled::text || '|portfolio_context=' || portfolio_context::text || '|max_tool_calls=' || max_tool_calls::text, 'UTF8')), 'hex') FROM ai.settings ORDER BY id", connection);
        var rows = new List<(long Id, bool Recent, string Hash, bool HashIsOfTheValues)>();
        await using (var reader = await command.ExecuteReaderAsync(Ct))
        {
            while (await reader.ReadAsync(Ct))
            {
                rows.Add((reader.GetInt64(0), reader.GetBoolean(1), reader.GetString(2), reader.GetBoolean(3)));
            }
        }

        rows.Count.ShouldBe(2);
        rows.ShouldAllBe(r => r.Recent && r.HashIsOfTheValues && r.Hash.Length == 64);
        rows[1].Id.ShouldBeGreaterThan(rows[0].Id);
        rows[0].Hash.ShouldNotBe(rows[1].Hash);
        await ResetAsync();
    }

    [Fact]
    public async Task Flipping_any_value_column_changes_the_hash_so_a_column_the_hash_ignores_fails_the_build()
    {
        // Every column except the stamped ones and the actor/reason is a value of the settings. A later migration that adds one must also replace ai.stamp_settings.
        string[] notValues = ["id", "changed_at", "changed_by", "reason", "snapshot_hash"];
        var columns = new List<(string Name, string Type)>();
        await using (var connection = new NpgsqlConnection(db.Owner))
        {
            await connection.OpenAsync(Ct);
            await using var command = new NpgsqlCommand("SELECT column_name, data_type FROM information_schema.columns WHERE table_schema = 'ai' AND table_name = 'settings' ORDER BY ordinal_position", connection);
            await using var reader = await command.ExecuteReaderAsync(Ct);
            while (await reader.ReadAsync(Ct))
            {
                if (!notValues.Contains(reader.GetString(0)))
                {
                    columns.Add((reader.GetString(0), reader.GetString(1)));
                }
            }
        }

        columns.Select(c => c.Name).ToList().ShouldBe(["allow_draft_confirm", "allow_proposal_accept", "auto_accept_hypothesis", "auto_lab_quick_test", "enabled", "max_tool_calls", "portfolio_context"], ignoreOrder: true, customMessage: "a new value column needs a line here and in the trigger function");
        foreach (var (name, type) in columns)
        {
            await ResetAsync();
            string Row(bool flipped, string column) => column == name ? (type == "boolean" ? (flipped ? "false" : "true") : (flipped ? "7" : "6")) : (column == "max_tool_calls" ? "6" : "true");
            type.ShouldBeOneOf("boolean", "integer");
            var all = columns.Select(c => c.Name).ToList();
            var sql = (bool flipped) => $"INSERT INTO ai.settings ({string.Join(", ", all)}, changed_by, reason) VALUES ({string.Join(", ", all.Select(c => Row(flipped, c)))}, 'owner:probe@example.test', 'hash test')";
            await ReadOnlyDatabase.ExecuteAsync(db.App, sql(false));
            await ReadOnlyDatabase.ExecuteAsync(db.App, sql(true));

            var distinct = (long)(await ReadOnlyDatabase.ScalarAsync(db.Owner, "SELECT count(DISTINCT snapshot_hash) FROM ai.settings"))!;

            distinct.ShouldBe(2, $"flipping {name} must change snapshot_hash");
        }

        await ResetAsync();
    }

    [Fact]
    public async Task The_read_role_reads_the_settings_and_the_sql_role_does_not()
    {
        await ResetAsync();
        await SetAsync(true, false);

        (await ReadOnlyDatabase.ScalarAsync(db.AsRead, "SELECT count(*) FROM ai.settings")).ShouldBe(1L);
        (await ReadOnlyDatabase.SqlStateAsync(db.AsSql, "SELECT count(*) FROM ai.settings")).ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
        await ResetAsync();
    }
}
