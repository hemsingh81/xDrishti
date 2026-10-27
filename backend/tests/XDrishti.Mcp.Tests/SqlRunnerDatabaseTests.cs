using System.Diagnostics;
using Microsoft.Extensions.Options;
using Npgsql;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Infrastructure.Ai.ReadOnly;
using XDrishti.Mcp.Tests.Support;

namespace XDrishti.Mcp.Tests;

/// <summary>Spec P9-02 AC 8, runner half: the read-only transaction, the cursor, the row cap and the timeout, against a real server and the real login <c>xd_mcp_sql</c>.</summary>
[Collection(ReadOnlyDatabaseSuite.Name)]
public sealed class SqlRunnerDatabaseTests(ReadOnlyDatabase db) : IAsyncLifetime
{
    private static readonly TimeSpan FiveSeconds = TimeSpan.FromSeconds(5);

    private AiDataSources? _sources;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private AiSqlRunner Runner => new(_sources!);

    public ValueTask InitializeAsync()
    {
        var read = new NpgsqlConnectionStringBuilder(db.AsRead);
        var sql = new NpgsqlConnectionStringBuilder(db.AsSql);
        _sources = new AiDataSources(Options.Create(new ReadOnlyDatabaseOptions
        {
            ReadConnectionString = $"Host={read.Host};Port={read.Port};Database={read.Database};Username={read.Username}",
            SqlConnectionString = $"Host={sql.Host};Port={sql.Port};Database={sql.Database};Username={sql.Username}",
            ReadPassword = read.Password,
            SqlPassword = sql.Password,
        }));
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _sources!.DisposeAsync();

    [Fact]
    public async Task A_select_over_a_view_returns_columns_and_rows()
    {
        await ReadOnlyDatabase.ExecuteAsync(db.Owner, "INSERT INTO market.baskets (id, name, colour, purpose, description, position, created_at, revision) VALUES (gen_random_uuid(), 'runner-probe', '#000000', 'trading', 'd', 99, now(), 1) ON CONFLICT DO NOTHING");

        var outcome = await Runner.RunAsync("select name, position from aiv.baskets where name = 'runner-probe'", 50, FiveSeconds, Ct);

        outcome.Status.ShouldBe(SqlRunStatus.Ok);
        outcome.Columns.Select(c => c.Name).ShouldBe(["name", "position"]);
        outcome.Rows.Single().ShouldBe(["runner-probe", 99L]);
        outcome.Truncated.ShouldBeFalse();
    }

    [Fact]
    public async Task A_query_for_a_thousand_rows_returns_at_most_the_cap_and_says_it_was_cut()
    {
        var outcome = await Runner.RunAsync("select g from generate_series(1, 1000) as g", 200, FiveSeconds, Ct);

        outcome.Status.ShouldBe(SqlRunStatus.Ok);
        outcome.Rows.Count.ShouldBe(200);
        outcome.Truncated.ShouldBeTrue();
        outcome.Rows[199][0].ShouldBe(200L);

        var exact = await Runner.RunAsync("select g from generate_series(1, 200) as g", 200, FiveSeconds, Ct);
        exact.Rows.Count.ShouldBe(200);
        exact.Truncated.ShouldBeFalse("exactly the cap is not a cut");
    }

    [Fact]
    public async Task A_query_over_the_time_limit_is_stopped_within_the_limit_plus_half_a_second()
    {
        var clock = Stopwatch.StartNew();

        var outcome = await Runner.RunAsync("select pg_sleep(10)", 50, FiveSeconds, Ct);

        outcome.Status.ShouldBe(SqlRunStatus.Timeout);
        clock.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(7), "the statement timeout (5 s) or the guard (5.5 s) stops it");
        (await Runner.RunAsync("select 1", 50, FiveSeconds, Ct)).Status.ShouldBe(SqlRunStatus.Ok, "the connection is usable again");
    }

    [Fact]
    public async Task A_query_for_a_table_the_role_may_not_read_is_denied()
    {
        foreach (var table in new[] { "accounts.broker_secrets", "accounts.broker_accounts", "auth.sessions", "platform.app_settings", "portfolio.ledger_entries", "research.sim_trades", "ai.settings", "pg_authid" })
        {
            (await Runner.RunAsync($"select * from {table}", 50, FiveSeconds, Ct)).Status.ShouldBe(SqlRunStatus.Denied, table);
        }
    }

    [Fact]
    public async Task The_cursor_wrapper_refuses_select_into_and_a_data_modifying_statement_even_when_the_pre_check_is_bypassed()
    {
        await ReadOnlyDatabase.ExecuteAsync(db.Owner, "TRUNCATE ai.settings");

        var into = await Runner.RunAsync("select * into temp t from aiv.instruments", 50, FiveSeconds, Ct);
        var modify = await Runner.RunAsync("with x as (insert into ai.settings (enabled, portfolio_context, max_tool_calls, changed_by, reason) values (true, true, 6, 'owner:x@example.test', 'x') returning 1) select * from x", 50, FiveSeconds, Ct);

        into.Status.ShouldNotBe(SqlRunStatus.Ok);
        modify.Status.ShouldNotBe(SqlRunStatus.Ok);
        (await ReadOnlyDatabase.ScalarAsync(db.Owner, "SELECT count(*) FROM ai.settings")).ShouldBe(0L);
    }

    [Fact]
    public async Task A_second_statement_smuggled_past_the_pre_check_changes_nothing()
    {
        await ReadOnlyDatabase.ExecuteAsync(db.Owner, "TRUNCATE ai.settings");

        foreach (var text in new[]
        {
            "select 1; insert into ai.settings (enabled, portfolio_context, max_tool_calls, changed_by, reason) values (true, true, 6, 'owner:x@example.test', 'x')",
            "select 1; drop view aiv.instruments",
            "select 1; create table aiv.smuggled (i int)",
            "select 1; truncate research.sim_runs",
        })
        {
            await Runner.RunAsync(text, 50, FiveSeconds, Ct);
        }

        (await ReadOnlyDatabase.ScalarAsync(db.Owner, "SELECT count(*) FROM ai.settings")).ShouldBe(0L);
        (await ReadOnlyDatabase.ScalarAsync(db.Owner, "SELECT count(*) FROM pg_views WHERE schemaname = 'aiv'")).ShouldBe(35L);
        (await ReadOnlyDatabase.ScalarAsync(db.Owner, "SELECT count(*) FROM pg_tables WHERE tablename = 'smuggled'")).ShouldBe(0L);
    }

    [Fact]
    public async Task The_session_cannot_become_another_role_or_leave_the_read_only_transaction()
    {
        var role = await Runner.RunAsync("select current_user, session_user, current_setting('transaction_read_only')", 50, FiveSeconds, Ct);
        var escape = await Runner.RunAsync("select set_config('role', 'xd_owner', false), set_config('transaction_read_only', 'off', false)", 50, FiveSeconds, Ct);
        var after = await Runner.RunAsync("select current_user, current_setting('transaction_read_only')", 50, FiveSeconds, Ct);

        role.Rows.Single().ShouldBe(["xd_mcp_sql", "xd_mcp_sql", "on"]);
        escape.Status.ShouldNotBe(SqlRunStatus.Ok);
        after.Rows.Single().ShouldBe(["xd_mcp_sql", "on"]);
    }

    [Fact]
    public async Task Values_are_delivered_as_plain_data_with_their_scale_and_zone()
    {
        var outcome = await Runner.RunAsync("""
            select 1234.5000::numeric(18,4) as n, 123456789012345678::numeric(18,0) as big, 12345678901::bigint as paise, 1.5::float8 as f, true as b, null::text as nothing,
                   timestamptz '2026-10-27 05:00:00+00' as ts, date '2026-10-27' as d, array[1,2,3] as arr, array['a','b']::text[] as texts, '{"k":[1,2]}'::jsonb as j, 'x'::bytea as bin,
                   gen_random_uuid() is not null as u, 1 as "naïve", interval '1 day' as i
            """, 50, FiveSeconds, Ct);

        outcome.Status.ShouldBe(SqlRunStatus.Ok, outcome.ErrorText);
        var row = outcome.Rows.Single();
        row[0].ShouldBe("1234.5000");
        row[1].ShouldBe("123456789012345678");
        row[2].ShouldBe(12_345_678_901L);
        row[3].ShouldBe(1.5d);
        row[4].ShouldBe(true);
        row[5].ShouldBeNull();
        row[6].ShouldBe("2026-10-27T05:00:00Z");
        row[7].ShouldBe("2026-10-27");
        ((List<object?>)row[8]!).ShouldBe([1L, 2L, 3L]);
        ((List<object?>)row[9]!).ShouldBe(["a", "b"]);
        ((string)row[10]!).ShouldContain("\"k\"");
        row[11].ShouldBe("[bytea 1 bytes]");
        outcome.Columns[13].Name.ShouldBe("naïve");
    }

    [Fact]
    public async Task A_syntax_error_is_an_error_with_a_short_message_and_the_pool_survives_many_calls()
    {
        var bad = await Runner.RunAsync("select from from", 50, FiveSeconds, Ct);

        bad.Status.ShouldBe(SqlRunStatus.Error);
        bad.ErrorText.ShouldNotBeNullOrEmpty();
        for (var i = 0; i < 12; i++)
        {
            (await Runner.RunAsync("select 1", 50, FiveSeconds, Ct)).Status.ShouldBe(SqlRunStatus.Ok);
        }
    }

    [Fact]
    public async Task A_login_that_is_refused_is_reported_as_unavailable_not_as_a_query_error()
    {
        var sql = new NpgsqlConnectionStringBuilder(db.AsSql);
        await using var wrong = new AiDataSources(Options.Create(new ReadOnlyDatabaseOptions
        {
            ReadConnectionString = "Host=unused",
            SqlConnectionString = $"Host={sql.Host};Port={sql.Port};Database={sql.Database};Username={sql.Username}",
            SqlPassword = "not-the-password",
        }));

        await Should.ThrowAsync<AiDatabaseUnavailableException>(() => new AiSqlRunner(wrong).RunAsync("select 1", 50, FiveSeconds, Ct));
    }
}
