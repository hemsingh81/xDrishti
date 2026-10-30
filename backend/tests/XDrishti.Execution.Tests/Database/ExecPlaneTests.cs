using Microsoft.Extensions.Configuration;
using Npgsql;
using XDrishti.Execution.Application;
using XDrishti.Execution.Domain;
using XDrishti.Execution.Host;
using XDrishti.Execution.Persistence;

namespace XDrishti.Execution.Tests.Database;

/// <summary>Criteria 11, 13 and 14 of P12-01 in their database form, the heartbeat and the health probe (P12-01b).</summary>
[Collection(ExecDatabaseSuite.Name)]
public sealed class ExecPlaneTests(ExecDatabase db)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<(string Result, string Code)?> AnswerOfAsync(string connection, Guid id)
    {
        await using var c = new NpgsqlConnection(connection);
        await c.OpenAsync(Ct);
        await using var cmd = new NpgsqlCommand("SELECT result, code FROM execution.command_results WHERE command_id = @id", c);
        cmd.Parameters.AddWithValue("id", id);
        await using var r = await cmd.ExecuteReaderAsync(Ct);
        return await r.ReadAsync(Ct) ? (r.GetString(0), r.GetString(1)) : null;
    }

    [Fact]
    public async Task A_command_row_is_answered_once_a_ping_is_done_and_an_unknown_kind_is_refused()
    {
        var ping = Guid.NewGuid();
        var odd = Guid.NewGuid();
        using var data = NpgsqlDataSource.Create(db.Exec);
        var store = new PgExecCommandStore(data);
        var processor = new ExecCommandProcessor(store);
        await processor.RunOnceAsync(Ct); // the shared database may hold a command another test class left unanswered (ExecGrantsTests inserts a ping); answer it first so only this test's two are counted
        await ExecDatabase.ExecuteAsync(db.App, $"INSERT INTO execution.commands (id, kind, actor) VALUES ('{ping}', 'ping', 'owner:me@example.com'), ('{odd}', 'flatten_everything', 'system:test')");

        (await processor.RunOnceAsync(Ct)).ShouldBe(2);
        (await processor.RunOnceAsync(Ct)).ShouldBe(0);

        (await AnswerOfAsync(db.Exec, ping)).ShouldBe(("done", "execution.ok"));
        (await AnswerOfAsync(db.Exec, odd)).ShouldBe(("refused", ExecCommandProcessor.UnknownCode));
        (await store.AnswerAsync(ping, "done", "execution.ok", Ct)).ShouldBeFalse("one id is never applied twice");
        (await store.PendingAsync(10, Ct)).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_command_table_refuses_a_secret_a_big_payload_and_an_unknown_actor()
    {
        (await ExecDatabase.SqlStateAsync(db.App, $"INSERT INTO execution.commands (id, kind, actor, payload) VALUES ('{Guid.NewGuid()}', 'ping', 'nobody', '{{}}')")).ShouldBe("23514");
        (await ExecDatabase.SqlStateAsync(db.App, $"INSERT INTO execution.commands (id, kind, actor, payload) VALUES ('{Guid.NewGuid()}', 'ping', 'system:t', '{{\"token\":\"x\"}}')")).ShouldBe("23514");
        (await ExecDatabase.SqlStateAsync(db.App, $"INSERT INTO execution.commands (id, kind, actor, payload) VALUES ('{Guid.NewGuid()}', 'ping', 'system:t', '{{\"a\":\"{new string('a', 2100)}\"}}')")).ShouldBe("23514");
    }

    [Fact]
    public async Task The_settings_reader_merges_the_newest_snapshot_over_the_defaults_and_replaces_a_value_out_of_range()
    {
        using var data = NpgsqlDataSource.Create(db.Exec);
        var reader = new ExecSettingsReader(new PgExecSettingsSnapshots(data));
        var before = await reader.ReadAsync(Ct);
        before.Values["exec.max_order_qty"].ShouldBe("2000");
        before.Replaced.ShouldBeEmpty();

        await ExecDatabase.ExecuteAsync(db.Owner, "INSERT INTO execution.settings (\"values\", actor, reason) VALUES ('{\"exec.max_order_qty\": 500, \"exec.price_band_pct\": 99, \"exec.unknown\": 1}', 'owner:me@example.com', 'test')");
        var after = await reader.ReadAsync(Ct);

        after.Values["exec.max_order_qty"].ShouldBe("500");
        after.Values["exec.price_band_pct"].ShouldBe("2.0");
        after.Replaced.ShouldBe(["exec.price_band_pct"]);
        after.Values.ContainsKey("exec.unknown").ShouldBeFalse();
        after.Hash().ShouldNotBe(before.Hash());
        (await ExecDatabase.SqlStateAsync(db.App, "INSERT INTO execution.settings (actor, reason) VALUES ('owner:x', 'y')")).ShouldBe("42501"); // the owner act has its own slice and trigger (P12-07)
    }

    [Fact]
    public async Task The_heartbeat_writes_its_row_and_the_probe_reads_it_as_the_role()
    {
        using var data = NpgsqlDataSource.Create(db.Exec);
        await new PgExecHeartbeats(data).BeatAsync(new ExecHeartbeat("recovering", "none", new string('a', 64), 3), Ct);

        (await ExecDatabase.ScalarAsync(db.Owner, "SELECT state FROM platform.service_heartbeats WHERE service_name = 'exec'")).ShouldBe("recovering");
        (await ExecDatabase.ScalarAsync(db.Owner, "SELECT details ->> 'environment' FROM platform.service_heartbeats WHERE service_name = 'exec'")).ShouldBe("none");
        var builder = new NpgsqlConnectionStringBuilder(db.Exec);
        var settings = new Dictionary<string, string?> { ["Database:ConnectionString"] = $"Host={builder.Host};Port={builder.Port};Database={builder.Database};Username=xd_exec", ["Database:Password"] = db.ExecPassword };
        (await ExecHealthProbe.RunAsync(c => c.AddInMemoryCollection(settings))).ShouldBe(0);
        settings["Database:ConnectionString"] = settings["Database:ConnectionString"]!.Replace("xd_exec", "xd_app", StringComparison.Ordinal);
        (await ExecHealthProbe.RunAsync(c => c.AddInMemoryCollection(settings))).ShouldBe(1, "the probe, like the service, refuses any other role");
        ExecHealthProbe.ExitCode(null).ShouldBe(1);
        ExecHealthProbe.ExitCode(TimeSpan.FromSeconds(-1)).ShouldBe(1, "a future-dated heartbeat is not healthy");
        ExecHealthProbe.ExitCode(TimeSpan.FromSeconds(44)).ShouldBe(0);
        ExecHealthProbe.ExitCode(TimeSpan.FromSeconds(46)).ShouldBe(1);
    }

    [Fact]
    public void The_service_refuses_to_run_under_any_role_but_xd_exec()
    {
        new ExecDatabaseOptions { ConnectionString = "Host=h;Username=xd_app" }.Validate(new(new object())).ShouldNotBeEmpty();
        new ExecDatabaseOptions { ConnectionString = "Host=h;Username=xd_owner" }.Validate(new(new object())).ShouldNotBeEmpty();
        new ExecDatabaseOptions { ConnectionString = "Host=h" }.Validate(new(new object())).ShouldNotBeEmpty();
        new ExecDatabaseOptions { ConnectionString = "Host=h;Username=xd_exec" }.Validate(new(new object())).ShouldBeEmpty();
        new ExecDatabaseOptions { ConnectionString = "x", Password = "secret" }.ToString().ShouldNotContain("secret");
    }

    [Fact]
    public async Task A_direct_write_of_the_projection_or_of_another_services_heartbeat_is_refused()
    {
        var intent = ExecIntent.Create(777001, Guid.NewGuid(), db.AccountId, 777001, ExecSide.Buy, "TCS", "1", 5, 100m, 98m, 104m, ExecStructure.Super).Value;
        await new PgExecEventStore(NpgsqlDataSource.Create(db.Exec)).StartAsync(intent, new DateOnly(2026, 10, 29), ExecEnvironment.Sandbox, "system:test", Ct);

        (await ExecDatabase.SqlStateAsync(db.Exec, "UPDATE execution.intent_state SET state = 'closed' WHERE intent_id = 777001")).ShouldBe("23514");
        (await ExecDatabase.SqlStateAsync(db.Exec, "UPDATE execution.intent_state SET filled_qty = 5 WHERE intent_id = 777001")).ShouldBe("23514");
        (await ExecDatabase.SqlStateAsync(db.Exec, "INSERT INTO execution.intents (id, account_id, plan_item_id, session_date, symbol, security_id, side, quantity, entry_price, stop_price, target_price, structure, environment, created_by) VALUES (777002, '" + db.AccountId + "', 777002, current_date, 'A', '1', 'buy', 1, 1, 1, 1, 'super', 'sandbox', 'x'); UPDATE execution.intent_state SET state = 'open' WHERE intent_id = 777002")).ShouldBe("23514");
        (await ExecDatabase.ScalarAsync(db.Owner, "SELECT state FROM execution.intent_state WHERE intent_id = 777001")).ShouldBe("planned");

        await ExecDatabase.ExecuteAsync(db.Owner, "INSERT INTO platform.service_heartbeats (service_name, instance, version, started_at, last_seen_at) VALUES ('xd-feed-probe', 'i', '1', now(), now())");
        (await ExecDatabase.SqlStateAsync(db.Exec, "UPDATE platform.service_heartbeats SET last_seen_at = now() WHERE service_name = 'xd-feed-probe'")).ShouldBe("42501");
        (await ExecDatabase.SqlStateAsync(db.Exec, "INSERT INTO platform.service_heartbeats (service_name, instance, version, started_at, last_seen_at) VALUES ('other', 'i', '1', now(), now())")).ShouldBe("42501");
        (await ExecDatabase.SqlStateAsync(db.Exec, "UPDATE platform.service_heartbeats SET service_name = 'exec' WHERE service_name = 'xd-feed-probe'")).ShouldBe("42501");
    }

    [Fact]
    public async Task The_role_gets_its_login_only_from_a_password_and_logs_in_as_itself()
    {
        await using var host = db.Host(null);
        (await XDrishti.Infrastructure.PostMigration.ProvisionExecutionRoleAsync(host, Ct)).ShouldBeFalse();
        await using var empty = db.Host("  ");
        (await XDrishti.Infrastructure.PostMigration.ProvisionExecutionRoleAsync(empty, Ct)).ShouldBeFalse();

        // Fail closed: an absent or empty file takes a login away; the password brings it back.
        (await ExecDatabase.ScalarAsync(db.Owner, "SELECT rolcanlogin FROM pg_roles WHERE rolname = 'xd_exec'")).ShouldBe(false);
        await using var again = db.Host(db.ExecPassword);
        (await XDrishti.Infrastructure.PostMigration.ProvisionExecutionRoleAsync(again, Ct)).ShouldBeTrue();
        (await ExecDatabase.ScalarAsync(db.Exec, "SELECT current_user")).ShouldBe("xd_exec");
        (await ExecDatabase.ScalarAsync(db.Owner, "SELECT rolcanlogin AND NOT rolsuper AND NOT rolcreaterole AND NOT rolcreatedb AND NOT rolbypassrls AND NOT rolinherit FROM pg_roles WHERE rolname = 'xd_exec'")).ShouldBe(true);
    }
}
