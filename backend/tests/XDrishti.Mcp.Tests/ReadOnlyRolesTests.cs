using Npgsql;
using XDrishti.Domain.Assistant;
using XDrishti.Mcp.Tests.Support;

namespace XDrishti.Mcp.Tests;

/// <summary>
/// Spec P9-02 AC 1, 13a and the non-login facts: what the two read-only roles may and may not do, proved as the roles themselves and from the catalog (so a base table, view or function added later without a thought fails here). Runs on a throw-away server set up like production.
/// </summary>
[Collection(ReadOnlyDatabaseSuite.Name)]
public sealed class ReadOnlyRolesTests(ReadOnlyDatabase db)
{
    private static readonly string[] BaseTables = ["research.sim_trades", "research.sim_runs", "accounts.broker_secrets", "accounts.broker_accounts", "auth.sessions", "platform.app_settings", "portfolio.ledger_entries", "portfolio.holdings", "market.bars_1m", "market.instruments", "lab.strategy_versions", "pg_authid", "pg_shadow"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string[] Roles => ["xd_mcp_read", "xd_mcp_sql"];

    private string Login(string role) => role == "xd_mcp_read" ? db.AsRead : db.AsSql;

    private async Task<List<string>> NamesAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(db.Owner);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(sql, connection);
        var names = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(Ct);
        while (await reader.ReadAsync(Ct))
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private async Task<bool> HasAsync(string function, string role, string target, string privilege) =>
        (bool)(await ReadOnlyDatabase.ScalarAsync(db.Owner, $"SELECT {function}('{role}', '{target}', '{privilege}')"))!;

    [Fact]
    public async Task Both_roles_log_in_and_carry_the_limits_of_the_migration()
    {
        foreach (var role in Roles)
        {
            (await ReadOnlyDatabase.ScalarAsync(Login(role), "SELECT current_user")).ShouldBe(role);
            (await ReadOnlyDatabase.ScalarAsync(Login(role), "SHOW default_transaction_read_only")).ShouldBe("on");
            (await ReadOnlyDatabase.ScalarAsync(Login(role), "SHOW search_path")).ShouldBe("aiv");
            (await ReadOnlyDatabase.ScalarAsync(Login(role), "SHOW lock_timeout")).ShouldBe("1s");
            (await ReadOnlyDatabase.ScalarAsync(Login(role), "SHOW idle_in_transaction_session_timeout")).ShouldBe("10s");
            (await ReadOnlyDatabase.ScalarAsync(Login(role), "SHOW work_mem")).ShouldBe("16MB");
            (await ReadOnlyDatabase.ScalarAsync(Login(role), "SHOW temp_file_limit")).ShouldBe("64MB");
        }

        (await ReadOnlyDatabase.ScalarAsync(db.AsSql, "SHOW statement_timeout")).ShouldBe("5s");
        (await ReadOnlyDatabase.ScalarAsync(db.AsRead, "SHOW statement_timeout")).ShouldBe("10s");
        (await ReadOnlyDatabase.ScalarAsync(db.Owner, "SELECT rolconnlimit FROM pg_roles WHERE rolname = 'xd_mcp_sql'")).ShouldBe(3);
        (await ReadOnlyDatabase.ScalarAsync(db.Owner, "SELECT rolconnlimit FROM pg_roles WHERE rolname = 'xd_mcp_read'")).ShouldBe(6);
    }

    [Fact]
    public async Task The_roles_are_plain_logins_without_power_or_membership()
    {
        (await NamesAsync("SELECT rolname FROM pg_roles WHERE rolname IN ('xd_mcp_read', 'xd_mcp_sql') AND (rolsuper OR rolcreatedb OR rolcreaterole OR rolreplication OR rolbypassrls OR rolinherit OR NOT rolcanlogin)")).ShouldBeEmpty();
        (await NamesAsync("SELECT m.roleid::regrole::text FROM pg_auth_members m WHERE m.member IN ('xd_mcp_read'::regrole, 'xd_mcp_sql'::regrole)")).ShouldBeEmpty();
    }

    [Fact]
    public async Task Select_works_on_every_view_of_the_manifest_for_both_roles_even_on_an_empty_database()
    {
        foreach (var role in Roles)
        {
            foreach (var view in DataManifest.Views)
            {
                (await ReadOnlyDatabase.SqlStateAsync(Login(role), $"SELECT 1 FROM aiv.{view.Name} LIMIT 1")).ShouldBeNull($"{role} on aiv.{view.Name}");
            }
        }
    }

    [Fact]
    public async Task Select_on_a_base_table_or_a_secret_fails_with_insufficient_privilege()
    {
        foreach (var role in Roles)
        {
            foreach (var table in BaseTables)
            {
                (await ReadOnlyDatabase.SqlStateAsync(Login(role), $"SELECT * FROM {table} LIMIT 1")).ShouldBe(PostgresErrorCodes.InsufficientPrivilege, $"{role} on {table}");
            }
        }

        (await ReadOnlyDatabase.SqlStateAsync(db.AsSql, "SELECT * FROM ai.settings")).ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
        (await ReadOnlyDatabase.SqlStateAsync(db.AsRead, "SELECT * FROM ai.settings")).ShouldBeNull("the read role may read the switches");
        (await ReadOnlyDatabase.SqlStateAsync(db.AsRead, "SELECT * FROM ai.account_labels")).ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
        (await ReadOnlyDatabase.SqlStateAsync(db.AsSql, "SELECT * FROM ai.account_labels")).ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
    }

    [Fact]
    public async Task Writes_and_definitions_fail_for_both_roles()
    {
        string[] statements =
        [
            "INSERT INTO aiv.instruments (id) VALUES (gen_random_uuid())",
            "UPDATE aiv.instruments SET name = 'x'",
            "DELETE FROM aiv.instruments",
            "TRUNCATE aiv.instruments",
            "INSERT INTO research.sim_runs (run_key) VALUES ('x')",
            "UPDATE research.sim_runs SET strategy_id = 'x'",
            "DELETE FROM research.sim_runs",
            "TRUNCATE research.sim_runs",
            "INSERT INTO ai.settings (enabled, portfolio_context, max_tool_calls, changed_by, reason) VALUES (true, true, 6, 'owner:x@example.test', 'x')",
            "CREATE TABLE aiv.x (i integer)",
            "CREATE TABLE public.x (i integer)",
            "CREATE TEMP TABLE t AS SELECT 1",
            "CREATE VIEW aiv.v AS SELECT 1",
            "CREATE FUNCTION aiv.f() RETURNS integer LANGUAGE sql AS 'SELECT 1'",
            "CREATE SCHEMA evil",
            "DROP VIEW aiv.instruments",
            "ALTER VIEW aiv.instruments RENAME TO x",
            "DROP SCHEMA aiv CASCADE",
            "GRANT SELECT ON research.sim_runs TO PUBLIC",
            "ALTER ROLE xd_mcp_sql SUPERUSER",
            "ALTER ROLE xd_mcp_sql RESET ALL",
            "ALTER SYSTEM SET work_mem = '1GB'",
            "COPY (SELECT 1) TO PROGRAM 'cat'",
            "COPY (SELECT 1) TO '/tmp/x'",
            "CREATE EXTENSION dblink",
            "DO $$ BEGIN EXECUTE 'SELECT * FROM accounts.broker_secrets'; END $$",
            "DO $$ BEGIN EXECUTE 'CREATE TABLE aiv.y (i integer)'; END $$",
            "SELECT lo_import('/etc/passwd')",
            "SELECT pg_read_file('/etc/passwd')",
            "SELECT pg_ls_dir('/')",
            "SELECT set_config('role', 'xd_owner', false) , (SELECT count(*) FROM accounts.broker_secrets)",
        ];
        foreach (var role in Roles)
        {
            foreach (var statement in statements)
            {
                var state = await ReadOnlyDatabase.SqlStateAsync(Login(role), statement);

                state.ShouldNotBeNull($"{role}: {statement}");
                state.ShouldBeOneOf([PostgresErrorCodes.InsufficientPrivilege, PostgresErrorCodes.ReadOnlySqlTransaction, "0LP01"], $"{role}: {statement} gave {state}");
            }
        }
    }

    [Fact]
    public async Task From_the_catalog_the_roles_hold_select_on_the_views_and_nothing_else_anywhere()
    {
        var relations = await NamesAsync("SELECT n.nspname || '.' || c.relname FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE c.relkind IN ('r', 'v', 'm', 'p', 'f') AND n.nspname NOT IN ('pg_catalog', 'information_schema', 'pg_toast') AND n.nspname NOT LIKE '\\_timescaledb%' AND n.nspname NOT LIKE 'timescaledb%' ORDER BY 1");

        relations.Where(r => r.StartsWith("aiv.", StringComparison.Ordinal)).Count().ShouldBe(DataManifest.Views.Count);
        relations.Count(r => r.StartsWith("research.", StringComparison.Ordinal)).ShouldBeGreaterThan(5, "the scan sees the base tables");
        foreach (var role in Roles)
        {
            foreach (var relation in relations)
            {
                var readable = relation.StartsWith("aiv.", StringComparison.Ordinal) || (role == "xd_mcp_read" && relation == "ai.settings");
                (await HasAsync("has_table_privilege", role, relation, "SELECT")).ShouldBe(readable, $"{role} SELECT on {relation}");
                foreach (var privilege in new[] { "INSERT", "UPDATE", "DELETE", "TRUNCATE", "REFERENCES", "TRIGGER" })
                {
                    (await HasAsync("has_table_privilege", role, relation, privilege)).ShouldBeFalse($"{role} {privilege} on {relation}");
                }
            }
        }
    }

    [Fact]
    public async Task The_roles_have_no_right_on_any_sequence_and_execute_no_security_definer_function_but_the_switch()
    {
        var sequences = await NamesAsync("SELECT n.nspname || '.' || c.relname FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE c.relkind = 'S' AND n.nspname NOT IN ('pg_catalog', 'information_schema', 'pg_toast') AND n.nspname NOT LIKE '\\_timescaledb%' AND n.nspname NOT LIKE 'timescaledb%'");
        sequences.ShouldNotBeEmpty();
        foreach (var role in Roles)
        {
            foreach (var sequence in sequences)
            {
                foreach (var privilege in new[] { "USAGE", "SELECT", "UPDATE" })
                {
                    (await HasAsync("has_sequence_privilege", role, sequence, privilege)).ShouldBeFalse($"{role} {privilege} on {sequence}");
                }
            }
        }

        var definers = await NamesAsync("SELECT p.oid::regprocedure::text FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace WHERE p.prosecdef AND n.nspname NOT IN ('pg_catalog', 'information_schema')");
        definers.ShouldContain("ai.context_allows(text)");
        foreach (var role in Roles)
        {
            foreach (var function in definers)
            {
                (await HasAsync("has_function_privilege", role, function, "EXECUTE")).ShouldBe(function == "ai.context_allows(text)", $"{role} EXECUTE {function}");
            }
        }
    }

    [Fact]
    public async Task The_runtime_role_has_no_access_to_the_views_so_it_cannot_write_base_rows_through_one()
    {
        var views = await NamesAsync("SELECT n.nspname || '.' || c.relname FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = 'aiv' AND c.relkind IN ('r', 'v', 'm', 'p', 'f')");
        views.Count.ShouldBe(DataManifest.Views.Count);
        (await HasAsync("has_schema_privilege", "xd_app", "aiv", "USAGE")).ShouldBeFalse();
        (await HasAsync("has_schema_privilege", "xd_app", "aiv", "CREATE")).ShouldBeFalse();
        foreach (var view in views.Append("ai.account_labels"))
        {
            foreach (var privilege in new[] { "SELECT", "INSERT", "UPDATE", "DELETE", "TRUNCATE" })
            {
                (await HasAsync("has_table_privilege", "xd_app", view, privilege)).ShouldBeFalse($"xd_app {privilege} on {view}");
            }
        }

        (await ReadOnlyDatabase.SqlStateAsync(db.App, "UPDATE aiv.instruments SET name = 'x'")).ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
        (await ReadOnlyDatabase.SqlStateAsync(db.App, "DELETE FROM aiv.sim_runs")).ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
        (await ReadOnlyDatabase.SqlStateAsync(db.App, "SELECT * FROM aiv.instruments")).ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
        (await ReadOnlyDatabase.SqlStateAsync(db.App, "SELECT * FROM ai.account_labels")).ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
    }

    [Fact]
    public async Task The_roles_cannot_use_the_application_schemas_at_all()
    {
        foreach (var role in Roles)
        {
            foreach (var schema in new[] { "accounts", "auth", "platform", "portfolio", "research", "lab", "market", "trading", "workflow", "paper" })
            {
                (await HasAsync("has_schema_privilege", role, schema, "USAGE")).ShouldBeFalse($"{role} USAGE on {schema}");
            }

            (await HasAsync("has_schema_privilege", role, "aiv", "USAGE")).ShouldBeTrue();
            (await HasAsync("has_schema_privilege", role, "aiv", "CREATE")).ShouldBeFalse();
            (await HasAsync("has_schema_privilege", role, "public", "CREATE")).ShouldBeFalse();
        }
    }

    [Fact]
    public async Task The_runtime_role_reads_and_inserts_settings_and_nothing_else_about_them()
    {
        (await HasAsync("has_table_privilege", "xd_app", "ai.settings", "SELECT")).ShouldBeTrue();
        (await HasAsync("has_table_privilege", "xd_app", "ai.settings", "INSERT")).ShouldBeTrue();
        foreach (var privilege in new[] { "UPDATE", "DELETE", "TRUNCATE" })
        {
            (await HasAsync("has_table_privilege", "xd_app", "ai.settings", privilege)).ShouldBeFalse(privilege);
        }
    }
}
