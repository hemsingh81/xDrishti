using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Domain.Planning;
using XDrishti.Infrastructure.Auto;

namespace XDrishti.Api.IntegrationTests.Cli;

/// <summary>
/// The facts of <c>xd auto check</c> (spec AU-19 criterion 3): read as a role that holds SELECT only, so the verb can write nothing; a fact that cannot be read is null, never zero.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class AutoProgrammeFactsReaderTests(ApiFactory factory)
{
    private const string Role = "xd_auto_check_probe";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_select_only_role_reads_every_fact_and_cannot_write()
    {
        var service = "au19-" + Guid.NewGuid().ToString("N")[..12];
        await SeedMultiDayRecoveryAsync(service);
        await PrepareRoleAsync("GRANT SELECT ON ALL TABLES IN SCHEMA platform, planning, trading TO " + Role);
        await using var source = RoleSource();
        var reader = new AutoProgrammeFactsReader(source, null);

        var facts = await reader.ReadAsync(Ct);

        facts.RecoveryPasses.ShouldNotBeNull().ShouldBeGreaterThan(0);
        facts.MultiDayRecoveries.ShouldNotBeNull().ShouldBeGreaterThan(0);
        facts.SwitchChanges.ShouldNotBeNull();
        facts.AutoOnNightsWithSuggestions.ShouldNotBeNull();
        facts.RemovalAdrPresent.ShouldBeNull();
        await using var command = source.CreateCommand("INSERT INTO platform.recovery_passes (id, trigger, started_at, status) VALUES (gen_random_uuid(), 'startup', now(), 'running')");
        var failure = await Should.ThrowAsync<PostgresException>(async () => await command.ExecuteNonQueryAsync(Ct));
        failure.SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
    }

    [Fact]
    public async Task A_table_the_role_cannot_read_is_a_missing_fact_not_a_zero()
    {
        await PrepareRoleAsync("GRANT SELECT ON ALL TABLES IN SCHEMA platform TO " + Role + "; REVOKE ALL ON ALL TABLES IN SCHEMA planning FROM " + Role);
        await using var source = RoleSource();
        var reader = new AutoProgrammeFactsReader(source, null);

        var facts = await reader.ReadAsync(Ct);

        facts.RecoveryPasses.ShouldNotBeNull();
        facts.SwitchChanges.ShouldBeNull();
        facts.AutoOnNightsWithSuggestions.ShouldBeNull();
    }

    [Fact]
    public async Task The_removal_adr_is_found_by_its_number_in_a_folder_and_unknown_without_one()
    {
        var folder = Directory.CreateTempSubdirectory("xd-adr-").FullName;
        try
        {
            await using var source = NpgsqlDataSource.Create(factory.AdminConnectionString);
            (await new AutoProgrammeFactsReader(source, folder).ReadAsync(Ct)).RemovalAdrPresent.ShouldBe(false);
            await File.WriteAllTextAsync(Path.Combine(folder, "0245-the-list.md"), "x", Ct);
            (await new AutoProgrammeFactsReader(source, folder).ReadAsync(Ct)).RemovalAdrPresent.ShouldBe(true);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task A_published_night_after_an_on_change_with_a_primary_ticket_is_counted_once()
    {
        await using var source = NpgsqlDataSource.Create(factory.AdminConnectionString);
        var reader = new AutoProgrammeFactsReader(source, null);
        var before = (await reader.ReadAsync(Ct)).AutoOnNightsWithSuggestions.ShouldNotBeNull();
        await SwitchAsync("on", new DateTimeOffset(2097, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await PublishNightAsync(new DateOnly(2097, 1, 2), recovered: false);

        var after = (await reader.ReadAsync(Ct)).AutoOnNightsWithSuggestions.ShouldNotBeNull();

        after.ShouldBe(before + 1);
    }

    [Fact]
    public async Task A_night_after_an_off_change_or_made_up_by_a_recovery_pass_is_not_counted()
    {
        await using var source = NpgsqlDataSource.Create(factory.AdminConnectionString);
        var reader = new AutoProgrammeFactsReader(source, null);
        var before = (await reader.ReadAsync(Ct)).AutoOnNightsWithSuggestions.ShouldNotBeNull();
        await SwitchAsync("off", new DateTimeOffset(2098, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await PublishNightAsync(new DateOnly(2098, 1, 2), recovered: false);
        await SwitchAsync("on", new DateTimeOffset(2099, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await PublishNightAsync(new DateOnly(2099, 1, 2), recovered: true);

        var after = (await reader.ReadAsync(Ct)).AutoOnNightsWithSuggestions.ShouldNotBeNull();

        after.ShouldBe(before);
    }

    private async Task SwitchAsync(string kind, DateTimeOffset at)
    {
        await using var connection = new NpgsqlConnection(factory.AdminConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand("INSERT INTO planning.auto_switch_changes (kind, actor, at, changed, snapshot) VALUES (@kind, 'owner:probe', @at, ARRAY[]::varchar[], '{}'::jsonb)", connection);
        command.Parameters.AddWithValue("kind", kind);
        command.Parameters.AddWithValue("at", at);
        await command.ExecuteNonQueryAsync(Ct);
    }

    private async Task PublishNightAsync(DateOnly session, bool recovered)
    {
        var cutoff = new DateTimeOffset(session.Year, session.Month, session.Day, 3, 35, 0, TimeSpan.Zero);
        var started = new DateTimeOffset(session.Year, session.Month, session.Day, 0, 0, 0, TimeSpan.Zero);
        var evidence = new PlanEvidence("ORB|15m|BUY|intraday|FIXED_RR|*", 41, "about 2.1 years, replayed history, modelled fills, no hold-out", "cell statistics only", 0.52, 0.12, [], null);
        var item = new NewPlanItem(1, TicketGroup.Primary, "NSE:RELIANCE", "BUY", "ORB", new string('a', 64), "close above 15m high", "0.8 ATR below entry", "2R", "09:30-11:30", [], evidence, []);
        var plan = new NewPlan($"au19-{session:yyyyMMdd}", session, cutoff, 1, new string('c', 64), "cell statistics only", true, "test", DateTimeOffset.UnixEpoch, [item]);
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IPlanStore>().PublishWithRunAsync(plan, new PublishRun(false, started, started.AddMinutes(1), "test", recovered), Ct);
    }

    private NpgsqlDataSource RoleSource() =>
        NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder(factory.AdminConnectionString) { Options = "-c role=" + Role }.ConnectionString);

    private async Task PrepareRoleAsync(string grants)
    {
        await using var connection = new NpgsqlConnection(factory.AdminConnectionString);
        await connection.OpenAsync(Ct);
        await ExecuteAsync(connection, $"DO $$ BEGIN IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = '{Role}') THEN CREATE ROLE {Role} NOLOGIN; END IF; END $$");
        await ExecuteAsync(connection, $"GRANT {Role} TO CURRENT_USER");
        await ExecuteAsync(connection, $"REVOKE ALL ON ALL TABLES IN SCHEMA platform, research, planning, trading FROM {Role}");
        await ExecuteAsync(connection, $"GRANT USAGE ON SCHEMA platform, research, planning, trading TO {Role}");
        await ExecuteAsync(connection, grants);
    }

    private async Task SeedMultiDayRecoveryAsync(string service)
    {
        await using var connection = new NpgsqlConnection(factory.AdminConnectionString);
        await connection.OpenAsync(Ct);
        var pass = Guid.NewGuid();
        await ExecuteAsync(connection, $"INSERT INTO platform.recovery_passes (id, trigger, started_at, finished_at, status) VALUES ('{pass}', 'startup', now(), now(), 'succeeded')");
        await ExecuteAsync(connection, $"INSERT INTO platform.recovery_items (id, pass_id, service_id, occurrence, session_from, session_to, state, attempts, run_id) VALUES (gen_random_uuid(), '{pass}', '{service}', '2026-11-06', '2026-11-04', '2026-11-06', 'succeeded', 1, gen_random_uuid())");
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(Ct);
    }
}
