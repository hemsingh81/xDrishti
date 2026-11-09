using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Platform.Services;
using XDrishti.Domain.Platform.Services;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Api.IntegrationTests.Platform;

/// <summary>
/// The tables of the services framework (migration AddServiceSchedules, ADR 0018): the columns and indexes of the spec, the checks, one running run per service, the row version that refuses a stale
/// save, rows that the runtime role can never delete, and the first-start settings that are read once.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class ServiceTablesTests(ApiFactory factory) : IAsyncLifetime
{
    private const string ScratchDatabase = "xd_service_grants_probe";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await factory.ResetInstrumentsAsync();

    public async ValueTask DisposeAsync()
    {
        await factory.ResetServicesAsync();
        GC.SuppressFinalize(this);
    }

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(factory.AdminConnectionString);
        await connection.OpenAsync(Ct);
        return connection;
    }

    private static async Task<object?> ScalarAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync(Ct);
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(Ct);
    }

    private static async Task ShouldBeRejectedAsync(NpgsqlConnection connection, string sql, string sqlState)
    {
        var failure = await Should.ThrowAsync<PostgresException>(() => ExecuteAsync(connection, sql));
        failure.SqlState.ShouldBe(sqlState);
    }

    private static string RunInsert(string service, string status, string trigger = "scheduled") =>
        $"INSERT INTO platform.service_runs (id, service_id, trigger, attempt, started_at, status) VALUES (gen_random_uuid(), '{service}', '{trigger}', 1, now(), '{status}')";

    // ---- the shape the spec asks for ----

    [Fact]
    public async Task The_tables_have_the_columns_and_indexes_of_the_contract()
    {
        await using var connection = await OpenAsync();

        var schedules = await ColumnsAsync(connection, "service_schedules");
        schedules.ShouldBe(["service_id", "enabled", "schedule", "retries", "timeout_minutes", "catch_up", "notify_on_failure", "parameters", "updated_by", "updated_at"], ignoreOrder: true);
        var runs = await ColumnsAsync(connection, "service_runs");
        runs.ShouldBe(["id", "service_id", "trigger", "attempt", "started_at", "finished_at", "status", "message", "data_job_id"], ignoreOrder: true);
        (await ScalarAsync(connection, "SELECT data_type FROM information_schema.columns WHERE table_schema = 'platform' AND table_name = 'service_schedules' AND column_name = 'schedule'")).ShouldBe("jsonb");
        (await ScalarAsync(connection, "SELECT count(*) FROM pg_indexes WHERE schemaname = 'platform' AND indexname = 'ix_service_runs_service_id_started_at' AND indexdef LIKE '%service_id, started_at DESC%'")).ShouldBe(1L);
        (await ScalarAsync(connection, "SELECT count(*) FROM pg_indexes WHERE schemaname = 'platform' AND indexname = 'ux_service_runs_one_running' AND indexdef LIKE '%UNIQUE%' AND indexdef LIKE '%status%running%'")).ShouldBe(1L);
    }

    private static async Task<List<string>> ColumnsAsync(NpgsqlConnection connection, string table)
    {
        await using var command = new NpgsqlCommand($"SELECT column_name FROM information_schema.columns WHERE table_schema = 'platform' AND table_name = '{table}'", connection);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        var columns = new List<string>();
        while (await reader.ReadAsync(Ct))
        {
            columns.Add(reader.GetString(0));
        }

        return columns;
    }

    [Fact]
    public async Task A_service_has_at_most_one_running_run_but_ended_runs_and_other_services_are_not_limited()
    {
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, RunInsert("a", "running"));

        await ShouldBeRejectedAsync(connection, RunInsert("a", "running"), PostgresErrorCodes.UniqueViolation);
        await ExecuteAsync(connection, RunInsert("b", "running"));
        await ExecuteAsync(connection, RunInsert("a", "failed"));
        await ExecuteAsync(connection, RunInsert("a", "skipped"));
        await ExecuteAsync(connection, "UPDATE platform.service_runs SET status = 'succeeded', finished_at = now() WHERE service_id = 'a' AND status = 'running'");
        await ExecuteAsync(connection, RunInsert("a", "running"));
    }

    [Fact]
    public async Task Triggers_statuses_attempts_retries_timeouts_and_schedule_types_are_checked_by_the_database()
    {
        await using var connection = await OpenAsync();

        await ShouldBeRejectedAsync(connection, RunInsert("a", "running", trigger: "cron"), PostgresErrorCodes.CheckViolation);
        await ShouldBeRejectedAsync(connection, RunInsert("a", "paused"), PostgresErrorCodes.CheckViolation);
        await ShouldBeRejectedAsync(connection, "INSERT INTO platform.service_runs (id, service_id, trigger, attempt, started_at, status) VALUES (gen_random_uuid(), 'a', 'manual', 0, now(), 'failed')", PostgresErrorCodes.CheckViolation);
        const string Insert = "INSERT INTO platform.service_schedules (service_id, enabled, schedule, retries, timeout_minutes, catch_up, notify_on_failure, parameters, updated_by, updated_at) VALUES ";
        await ShouldBeRejectedAsync(connection, Insert + "('a', true, '{\"type\":\"daily\"}', 6, 30, true, true, '{}', 'x', now())", PostgresErrorCodes.CheckViolation);
        await ShouldBeRejectedAsync(connection, Insert + "('a', true, '{\"type\":\"daily\"}', 0, 241, true, true, '{}', 'x', now())", PostgresErrorCodes.CheckViolation);
        await ShouldBeRejectedAsync(connection, Insert + "('a', true, '{\"type\":\"hourly\"}', 0, 30, true, true, '{}', 'x', now())", PostgresErrorCodes.CheckViolation);
        await ExecuteAsync(connection, Insert + "('a', true, '{\"type\":\"daily\"}', 5, 240, true, true, '{}', 'x', now())");
    }

    // ---- the row version ----

    [Fact]
    public async Task A_save_made_on_a_row_someone_changed_meanwhile_is_refused_by_the_row_version()
    {
        await factory.WithDbAsync(async db =>
        {
            db.ServiceSchedules.Add(ServiceSchedule.Create("eod", new ServiceSettings(true, Schedule.Daily(new TimeOnly(15, 50)), 0, 30, true, true, new Dictionary<string, System.Text.Json.JsonElement>()), "test", DateTimeOffset.UtcNow));
            await db.SaveChangesAsync(Ct);
            return 0;
        });
        using var first = factory.Services.CreateScope();
        using var second = factory.Services.CreateScope();
        var one = first.ServiceProvider.GetRequiredService<XDrishtiDbContext>();
        var two = second.ServiceProvider.GetRequiredService<XDrishtiDbContext>();
        var a = await one.ServiceSchedules.SingleAsync(Ct);
        var b = await two.ServiceSchedules.SingleAsync(Ct);
        var revision = a.Revision;
        revision.ShouldBeGreaterThan(0u);

        a.Apply(a.Settings with { Retries = 2 }, "one", DateTimeOffset.UtcNow);
        await one.SaveChangesAsync(Ct);
        b.Apply(b.Settings with { Retries = 4 }, "two", DateTimeOffset.UtcNow);

        await Should.ThrowAsync<DbUpdateConcurrencyException>(async () => await two.SaveChangesAsync(Ct));
        a.Revision.ShouldNotBe(revision); // the database gave the saved row a new version
    }

    [Fact]
    public async Task The_schedule_and_the_parameters_round_trip_through_the_json_columns()
    {
        var settings = new ServiceSettings(false, Schedule.Interval(5, new TimeOnly(9, 15), new TimeOnly(15, 30), false, Weekdays.Monday | Weekdays.Friday), 3, 90, false, false,
            new Dictionary<string, System.Text.Json.JsonElement> { ["lookback"] = System.Text.Json.JsonSerializer.SerializeToElement(12) });
        await factory.WithDbAsync(async db =>
        {
            db.ServiceSchedules.Add(ServiceSchedule.Create("round-trip", settings, "test", DateTimeOffset.UtcNow));
            await db.SaveChangesAsync(Ct);
            return 0;
        });

        var read = await factory.WithDbAsync(async db => await db.ServiceSchedules.AsNoTracking().SingleAsync(x => x.Id == "round-trip", Ct));

        read.Settings.Same(settings).ShouldBeTrue();
        await using var connection = await OpenAsync();
        (await ScalarAsync(connection, "SELECT schedule ->> 'type' FROM platform.service_schedules WHERE service_id = 'round-trip'")).ShouldBe("interval");
        (await ScalarAsync(connection, "SELECT schedule ->> 'from' FROM platform.service_schedules WHERE service_id = 'round-trip'")).ShouldBe("09:15");
    }

    [Fact]
    public async Task The_store_refuses_a_second_running_run_and_closes_a_stale_one_first()
    {
        var first = ServiceRun.Start("stale-probe", ServiceRunTrigger.Scheduled, 1, DateTimeOffset.UtcNow.AddHours(-6));
        var second = ServiceRun.Start("stale-probe", ServiceRunTrigger.Manual, 1, DateTimeOffset.UtcNow.AddMinutes(-1));
        var third = ServiceRun.Start("stale-probe", ServiceRunTrigger.Manual, 1, DateTimeOffset.UtcNow);

        async Task<bool> StartedAsync(ServiceRun run)
        {
            using var scope = factory.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<IServiceRunStore>().TryStartAsync(run, TimeSpan.FromHours(5), Ct);
        }

        (await StartedAsync(first)).ShouldBeTrue();
        (await StartedAsync(second)).ShouldBeTrue(); // the first one is older than 5 hours: it cannot be running, it is closed as interrupted
        (await StartedAsync(third)).ShouldBeFalse(); // the second one is running
        await using var connection = await OpenAsync();
        (await ScalarAsync(connection, "SELECT status || ': ' || left(message, 11) FROM platform.service_runs WHERE service_id = 'stale-probe' ORDER BY started_at LIMIT 1")).ShouldBe("failed: Interrupted");
        (await ScalarAsync(connection, "SELECT count(*) FROM platform.service_runs WHERE service_id = 'stale-probe' AND status = 'running'")).ShouldBe(1L);
    }

    [Fact]
    public async Task The_latest_run_of_each_given_service_is_read_without_the_others_and_without_a_scan()
    {
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, $"""
            INSERT INTO platform.service_runs (id, service_id, trigger, attempt, started_at, finished_at, status) VALUES
              (gen_random_uuid(), 'a', 'scheduled', 1, now() - interval '3 hours', now() - interval '3 hours', 'succeeded'),
              (gen_random_uuid(), 'a', 'manual', 1, now() - interval '1 hour', now() - interval '1 hour', 'failed'),
              (gen_random_uuid(), 'b', 'scheduled', 1, now() - interval '2 hours', now() - interval '2 hours', 'succeeded'),
              (gen_random_uuid(), 'other', 'scheduled', 1, now(), now(), 'succeeded')
            """);

        using var scope = factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IServiceRunStore>();
        var latest = await store.GetLatestAsync(["a", "b", "never-ran"], Ct);

        latest.Keys.Order().ShouldBe(["a", "b"]);
        (latest["a"].Trigger, latest["a"].Status).ShouldBe((ServiceRunTrigger.Manual, ServiceRunStatus.Failed));
        (await store.GetLatestAsync([], Ct)).ShouldBeEmpty();
        (await ScalarAsync(connection, "SELECT count(*) FROM pg_indexes WHERE indexname = 'ix_service_runs_service_id_started_at'")).ShouldBe(1L);
    }

    // ---- the runtime role ----

    [Fact]
    public async Task The_app_role_can_read_insert_and_update_the_schedules_and_runs_but_never_delete_them()
    {
        var admin = new NpgsqlConnectionStringBuilder(factory.AdminConnectionString);
        var scratch = new NpgsqlConnectionStringBuilder(factory.AdminConnectionString) { Database = ScratchDatabase, Pooling = false };
        await CreateScratchDatabaseAsync(admin, scratch);
        try
        {
            await using (var migrated = factory.WithWebHostBuilder(b => b.UseSetting("Database:ConnectionString", scratch.ConnectionString)))
            {
                await DatabaseMigrator.MigrateAsync(migrated.Services, Ct);
            }

            await using var connection = new NpgsqlConnection(scratch.ConnectionString);
            await connection.OpenAsync(Ct);
            await ExecuteAsync(connection, "SET ROLE xd_app");

            await ExecuteAsync(connection, "INSERT INTO platform.service_schedules (service_id, enabled, schedule, retries, timeout_minutes, catch_up, notify_on_failure, parameters, updated_by, updated_at) VALUES ('eod', true, '{\"type\":\"daily\"}', 0, 30, true, true, '{}', 'x', now())");
            await ExecuteAsync(connection, "UPDATE platform.service_schedules SET enabled = false");
            (await ScalarAsync(connection, "SELECT count(*) FROM platform.service_schedules")).ShouldBe(1L);
            await ExecuteAsync(connection, RunInsert("eod", "running"));
            await ExecuteAsync(connection, "UPDATE platform.service_runs SET status = 'failed', finished_at = now()");
            (await ScalarAsync(connection, "SELECT count(*) FROM platform.service_runs")).ShouldBe(1L);

            await ShouldBeRejectedAsync(connection, "DELETE FROM platform.service_schedules", PostgresErrorCodes.InsufficientPrivilege);
            await ShouldBeRejectedAsync(connection, "DELETE FROM platform.service_runs", PostgresErrorCodes.InsufficientPrivilege);
            (await ScalarAsync(connection, "SELECT has_table_privilege('xd_app', 'platform.service_heartbeats', 'DELETE')")).ShouldBe(true); // the other platform table is unchanged
        }
        finally
        {
            await DropScratchDatabaseAsync(admin);
        }
    }

    // ---- the recovery ledger (migration AddRecoveryLedger, ADR 0240) ----

    [Fact]
    public async Task The_recovery_ledger_keeps_one_item_per_service_and_occurrence_and_a_repeated_save_updates_it()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var ledger = scope.ServiceProvider.GetRequiredService<IRecoveryLedger>();
        var now = DateTimeOffset.UtcNow;
        var pass = await ledger.BeginPassAsync("startup", now, Ct);
        var day = new DateOnly(2026, 10, 12);
        var key = "probe-" + Guid.NewGuid().ToString("N")[..8];
        await ledger.SaveAsync(new RecoveryItemRow(Guid.NewGuid(), pass, key, "2026-10-12", day, day, RecoveryStates.Running, null, 1, null), Ct);
        await ledger.SaveAsync(new RecoveryItemRow(Guid.NewGuid(), pass, key, "2026-10-12", day, day, RecoveryStates.Failed, "down", 2, null), Ct);

        var item = (await ledger.FindAsync(key, "2026-10-12", Ct)).ShouldNotBeNull();
        (item.State, item.Attempts, item.Reason).ShouldBe((RecoveryStates.Failed, 2, "down"));
        (await ledger.ListRetryableAsync(3, Ct)).ShouldContain(i => i.ServiceId == key);
        (await ledger.ListRetryableAsync(2, Ct)).ShouldNotContain(i => i.ServiceId == key);
        (await ledger.ListExhaustedAsync(2, now.AddMinutes(-1), Ct)).ShouldContain(e => e.ServiceId == key);
        (await ledger.FindNewestAsync(key, Ct)).ShouldNotBeNull().Occurrence.ShouldBe("2026-10-12");

        await using var connection = await OpenAsync();
        await ShouldBeRejectedAsync(connection, $"INSERT INTO platform.recovery_items (id, pass_id, service_id, occurrence, session_from, session_to, state, attempts) VALUES (gen_random_uuid(), '{pass}', '{key}', '2026-10-12', '2026-10-12', '2026-10-12', 'running', 0)", PostgresErrorCodes.UniqueViolation);
        await ShouldBeRejectedAsync(connection, $"UPDATE platform.recovery_items SET state = 'bogus' WHERE service_id = '{key}'", PostgresErrorCodes.CheckViolation);

        await ledger.EndPassAsync(pass, "incomplete", now, Ct);
        await ExecuteAsync(connection, $"DELETE FROM platform.recovery_items WHERE service_id = '{key}'");
    }

    [Fact]
    public async Task A_partial_item_keeps_the_per_session_detail_as_jsonb_and_is_not_retried()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var ledger = scope.ServiceProvider.GetRequiredService<IRecoveryLedger>();
        var now = DateTimeOffset.UtcNow;
        var pass = await ledger.BeginPassAsync("startup", now, Ct);
        var day = new DateOnly(2026, 10, 12);
        var key = "probe-" + Guid.NewGuid().ToString("N")[..8];
        const string Detail = "{\"qualityVerified\":true,\"sessions\":[{\"date\":\"2026-10-12\",\"state\":\"still_missing\"}]}";
        await ledger.SaveAsync(new RecoveryItemRow(Guid.NewGuid(), pass, key, "2026-10-12", day, day, RecoveryStates.Partial, "0 of 1 sessions recovered", 1, null, Detail), Ct);

        var item = (await ledger.FindAsync(key, "2026-10-12", Ct)).ShouldNotBeNull();

        item.State.ShouldBe(RecoveryStates.Partial);
        item.Detail.ShouldNotBeNull().ShouldContain("\"still_missing\""); // jsonb normalises the text
        (await ledger.ListRetryableAsync(3, Ct)).ShouldNotContain(i => i.ServiceId == key);
        (await ledger.ListPartialAsync(Ct)).ShouldContain(i => i.ServiceId == key);
        await using var connection = await OpenAsync();
        (await ScalarAsync(connection, $"SELECT detail->'sessions'->0->>'state' FROM platform.recovery_items WHERE service_id = '{key}'")).ShouldBe("still_missing");
        await ledger.EndPassAsync(pass, "incomplete", now, Ct);
        await ExecuteAsync(connection, $"DELETE FROM platform.recovery_items WHERE service_id = '{key}'");
    }

    [Fact]
    public async Task An_item_left_running_by_an_earlier_process_is_closed_as_failed_interrupted_and_so_is_its_pass()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var ledger = scope.ServiceProvider.GetRequiredService<IRecoveryLedger>();
        var now = DateTimeOffset.UtcNow;
        var pass = await ledger.BeginPassAsync("wake", now, Ct);
        var key = "probe-" + Guid.NewGuid().ToString("N")[..8];
        var day = new DateOnly(2026, 10, 12);
        await ledger.SaveAsync(new RecoveryItemRow(Guid.NewGuid(), pass, key, "2026-10-12", day, day, RecoveryStates.Running, null, 1, null), Ct);

        (await ledger.InterruptRunningAsync(now, Ct)).ShouldBeGreaterThanOrEqualTo(1);

        var item = (await ledger.FindAsync(key, "2026-10-12", Ct)).ShouldNotBeNull();
        (item.State, item.Reason).ShouldBe((RecoveryStates.Failed, "interrupted"));
        await using var connection = await OpenAsync();
        (await ScalarAsync(connection, $"SELECT status FROM platform.recovery_passes WHERE id = '{pass}'")).ShouldBe("interrupted");
        await ExecuteAsync(connection, $"DELETE FROM platform.recovery_items WHERE service_id = '{key}'");
    }

    [Fact]
    public async Task The_recovery_migration_runs_up_down_and_up_with_the_app_role_unable_to_delete_the_ledger()
    {
        var admin = new NpgsqlConnectionStringBuilder(factory.AdminConnectionString);
        var scratch = new NpgsqlConnectionStringBuilder(factory.AdminConnectionString) { Database = ScratchDatabase, Pooling = false };
        await CreateScratchDatabaseAsync(admin, scratch);
        try
        {
            await using var migrated = factory.WithWebHostBuilder(b => b.UseSetting("Database:ConnectionString", scratch.ConnectionString));
            await DatabaseMigrator.MigrateAsync(migrated.Services, Ct);
            await using var connection = new NpgsqlConnection(scratch.ConnectionString);
            await connection.OpenAsync(Ct);
            (await ScalarAsync(connection, "SELECT count(*) FROM information_schema.columns WHERE table_schema = 'trading' AND table_name = 'plan_runs' AND column_name = 'recovered' AND column_default = 'false' AND is_nullable = 'NO'")).ShouldBe(1L);
            await ExecuteAsync(connection, "SET ROLE xd_app");
            await ExecuteAsync(connection, "INSERT INTO platform.recovery_passes (id, trigger, started_at, status) VALUES ('00000000-0000-7000-8000-000000000001', 'startup', now(), 'running')");
            await ExecuteAsync(connection, "INSERT INTO platform.recovery_items (id, pass_id, service_id, occurrence, session_from, session_to, state, attempts) VALUES (gen_random_uuid(), '00000000-0000-7000-8000-000000000001', 'eod', '2026-10-12', '2026-10-12', '2026-10-12', 'running', 1)");
            await ExecuteAsync(connection, "UPDATE platform.recovery_items SET state = 'succeeded'");
            await ExecuteAsync(connection, "UPDATE platform.recovery_passes SET status = 'succeeded', finished_at = now()");
            await ShouldBeRejectedAsync(connection, "DELETE FROM platform.recovery_items", PostgresErrorCodes.InsufficientPrivilege);
            await ShouldBeRejectedAsync(connection, "DELETE FROM platform.recovery_passes", PostgresErrorCodes.InsufficientPrivilege);
            await ExecuteAsync(connection, "RESET ROLE");

            await using (var scope = migrated.Services.CreateAsyncScope())
            {
                var migrator = scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>().GetService<IMigrator>();
                await migrator.MigrateAsync("20261109090000_AddKillDismissReason", Ct); // down
                (await ScalarAsync(connection, "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'platform' AND table_name LIKE 'recovery_%'")).ShouldBe(0L);
                (await ScalarAsync(connection, "SELECT count(*) FROM information_schema.columns WHERE table_schema = 'trading' AND table_name = 'plan_runs' AND column_name = 'recovered'")).ShouldBe(0L);
                await migrator.MigrateAsync(null, Ct); // and up again
                (await ScalarAsync(connection, "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'platform' AND table_name LIKE 'recovery_%'")).ShouldBe(2L);
            }
        }
        finally
        {
            await DropScratchDatabaseAsync(admin);
        }
    }

    [Fact]
    public async Task The_manual_trigger_migration_admits_a_manual_pass_and_goes_down_and_up_again()
    {
        var admin = new NpgsqlConnectionStringBuilder(factory.AdminConnectionString);
        var scratch = new NpgsqlConnectionStringBuilder(factory.AdminConnectionString) { Database = ScratchDatabase, Pooling = false };
        await CreateScratchDatabaseAsync(admin, scratch);
        try
        {
            await using var migrated = factory.WithWebHostBuilder(b => b.UseSetting("Database:ConnectionString", scratch.ConnectionString));
            await DatabaseMigrator.MigrateAsync(migrated.Services, Ct);
            await using var connection = new NpgsqlConnection(scratch.ConnectionString);
            await connection.OpenAsync(Ct);
            await ExecuteAsync(connection, "INSERT INTO platform.recovery_passes (id, trigger, started_at, status) VALUES ('00000000-0000-7000-8000-000000000002', 'manual', now(), 'running')");

            await using var scope = migrated.Services.CreateAsyncScope();
            var migrator = scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>().GetService<IMigrator>();
            await migrator.MigrateAsync("20261109110000_AddRecoveryItemDetail", Ct); // down
            (await ScalarAsync(connection, "SELECT trigger FROM platform.recovery_passes")).ShouldBe("resume");
            await ShouldBeRejectedAsync(connection, "INSERT INTO platform.recovery_passes (id, trigger, started_at, status) VALUES (gen_random_uuid(), 'manual', now(), 'running')", PostgresErrorCodes.CheckViolation);
            await migrator.MigrateAsync(null, Ct); // and up again
            await ExecuteAsync(connection, "INSERT INTO platform.recovery_passes (id, trigger, started_at, status) VALUES (gen_random_uuid(), 'manual', now(), 'running')");
        }
        finally
        {
            await DropScratchDatabaseAsync(admin);
        }
    }

    private static async Task CreateScratchDatabaseAsync(NpgsqlConnectionStringBuilder admin, NpgsqlConnectionStringBuilder scratch)
    {
        await using (var connection = new NpgsqlConnection(admin.ConnectionString))
        {
            await connection.OpenAsync(Ct);
            await ExecuteAsync(connection, "DO $$ BEGIN IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'xd_app') THEN CREATE ROLE xd_app NOLOGIN; END IF; END $$");
            await ExecuteAsync(connection, $"DROP DATABASE IF EXISTS {ScratchDatabase} WITH (FORCE)");
            await ExecuteAsync(connection, $"CREATE DATABASE {ScratchDatabase}");
        }

        await using var scratchConnection = new NpgsqlConnection(scratch.ConnectionString);
        await scratchConnection.OpenAsync(Ct);
        await ExecuteAsync(scratchConnection, "ALTER DEFAULT PRIVILEGES GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO xd_app");
        await ExecuteAsync(scratchConnection, "ALTER DEFAULT PRIVILEGES GRANT USAGE, SELECT ON SEQUENCES TO xd_app");
    }

    private static async Task DropScratchDatabaseAsync(NpgsqlConnectionStringBuilder admin)
    {
        await using var connection = new NpgsqlConnection(admin.ConnectionString);
        await connection.OpenAsync(Ct);
        await ExecuteAsync(connection, $"DROP DATABASE IF EXISTS {ScratchDatabase} WITH (FORCE)");
    }

    // ---- the first-start settings ----

    [Fact]
    public async Task The_configuration_seeds_the_row_once_the_edit_on_the_screen_survives_a_restart_and_the_configuration_is_ignored_after_it()
    {
        // first start: Data:Eod:* say "on, 16:05"
        await using (var first = factory.WithWebHostBuilder(b => b.UseSetting("Data:Eod:Enabled", "true").UseSetting("Data:Eod:TimeIst", "16:05")))
        {
            using var client = first.CreateClient();
            await first.Services.GetRequiredService<ServiceScheduler>().StartUpAsync(Ct); // what the hosted scheduler does when the host starts
            var seeded = await factory.WithDbAsync(async db => await db.ServiceSchedules.AsNoTracking().SingleAsync(x => x.Id == "eod", Ct));
            (seeded.Enabled, seeded.Schedule.StartTime, seeded.UpdatedBy).ShouldBe((true, new TimeOnly(16, 5), "system:first-start"));

            // the owner edits the time on the screen
            await factory.WithDbAsync(async db =>
            {
                var row = await db.ServiceSchedules.SingleAsync(x => x.Id == "eod", Ct);
                row.Apply(row.Settings with { Schedule = Schedule.Daily(new TimeOnly(16, 40)) }, "owner@example.invalid", DateTimeOffset.UtcNow);
                await db.SaveChangesAsync(Ct);
                return 0;
            });
        }

        // restart with other settings in the configuration: the row wins
        await using (var second = factory.WithWebHostBuilder(b => b.UseSetting("Data:Eod:Enabled", "false").UseSetting("Data:Eod:TimeIst", "09:00")))
        {
            using var client = second.CreateClient();
            await second.Services.GetRequiredService<ServiceScheduler>().StartUpAsync(Ct);
            var row = await factory.WithDbAsync(async db => await db.ServiceSchedules.AsNoTracking().SingleAsync(x => x.Id == "eod", Ct));
            (row.Enabled, row.Schedule.StartTime, row.UpdatedBy).ShouldBe((true, new TimeOnly(16, 40), "owner@example.invalid"));
        }
    }
}
