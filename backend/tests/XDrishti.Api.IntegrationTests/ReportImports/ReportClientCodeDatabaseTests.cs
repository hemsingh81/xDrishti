using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Api.IntegrationTests.ReportImports;

/// <summary>
/// The table of migration AddReportClientCodeHash (P2-17 criterion 12, ADR 0029 section 8): one hash of the client code per account, 64 characters, an existing account; the runtime role reads and inserts it and can neither change nor delete it; the migration runs on a database
/// that already holds accounts and rolls back to the report imports.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class ReportClientCodeDatabaseTests(ApiFactory factory) : IAsyncLifetime
{
    // These tests write accounts straight into the tables and reset at the start of the test that needs it; the rows must not outlive the class.
    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync() => await factory.ResetAccountsAsync();

    private const string MigrationDatabase = "xd_report_client_code_migration_probe";

    private const string BeforeMigration = "20261015062939_AddReportImports";

    private const string AccountId = "00000000-0000-7000-8000-00000000f301";

    private const string Hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private static readonly string[] Refused = ["UPDATE", "DELETE"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string AccountSql = $"""
        INSERT INTO accounts.broker_accounts (id, slug, name, broker, client_id, api_key_issued_on, color, created_at, updated_at, data_enabled, data_primary, data_standby, live_feed_budget)
        VALUES ('{AccountId}', 'probe-codes', 'Probe', 'Dhan', '1000000001', current_date, '#1A2B3C', now(), now(), false, false, false, 200);
        """;

    private static string CodeSql(string hash = Hash, string account = AccountId) =>
        $"INSERT INTO accounts.report_client_codes (account_id, hash, remembered_at) VALUES ('{account}', '{hash}', now())";

    private async Task<NpgsqlConnection> PreparedAsync()
    {
        await factory.ResetAccountsAsync();
        var connection = new NpgsqlConnection(factory.AdminConnectionString);
        await connection.OpenAsync(Ct);
        await AppRoleProbe.ExecuteAsync(connection, AccountSql, Ct);
        return connection;
    }

    [Fact]
    public async Task The_table_has_the_columns_of_the_contract_and_keeps_one_hash_per_account()
    {
        await using var connection = await PreparedAsync();
        var columns = new List<string>();
        await using (var command = new NpgsqlCommand("SELECT column_name FROM information_schema.columns WHERE table_schema = 'accounts' AND table_name = 'report_client_codes' ORDER BY ordinal_position", connection))
        await using (var reader = await command.ExecuteReaderAsync(Ct))
        {
            while (await reader.ReadAsync(Ct))
            {
                columns.Add(reader.GetString(0));
            }
        }

        await AppRoleProbe.ExecuteAsync(connection, CodeSql(), Ct);
        var twice = await Should.ThrowAsync<PostgresException>(() => AppRoleProbe.ExecuteAsync(connection, CodeSql("b" + Hash[1..]), Ct));

        columns.ShouldBe(["account_id", "hash", "remembered_at"]);
        (twice.SqlState, twice.ConstraintName).ShouldBe((PostgresErrorCodes.UniqueViolation, "pk_report_client_codes"));
    }

    [Fact]
    public async Task The_database_refuses_a_hash_of_another_length_and_a_code_of_a_missing_account()
    {
        await using var connection = await PreparedAsync();

        var plain = await Should.ThrowAsync<PostgresException>(() => AppRoleProbe.ExecuteAsync(connection, CodeSql("AB12CD34EF"), Ct));
        var orphan = await Should.ThrowAsync<PostgresException>(() => AppRoleProbe.ExecuteAsync(connection, CodeSql(account: Guid.NewGuid().ToString()), Ct));

        (plain.SqlState, plain.ConstraintName).ShouldBe((PostgresErrorCodes.CheckViolation, "ck_report_client_codes_hash"));
        (orphan.SqlState, orphan.ConstraintName).ShouldBe((PostgresErrorCodes.ForeignKeyViolation, "fk_report_client_codes_broker_accounts_account_id"));
    }

    [Fact]
    public async Task The_app_role_reads_and_inserts_the_hash_and_can_neither_change_nor_delete_it()
    {
        await AppRoleProbe.RunAsync(
            factory,
            async connection =>
            {
                await AppRoleProbe.ExecuteAsync(connection, AccountSql, Ct);
                await AppRoleProbe.ExecuteAsync(connection, CodeSql(), Ct);

                (await AppRoleProbe.ScalarAsync(connection, "SELECT count(*) FROM accounts.report_client_codes", Ct)).ShouldBe(1L);
                foreach (var privilege in Refused)
                {
                    var refused = await Should.ThrowAsync<PostgresException>(() => AppRoleProbe.ExecuteAsync(connection, privilege == "UPDATE" ? "UPDATE accounts.report_client_codes SET hash = hash" : "DELETE FROM accounts.report_client_codes", Ct));
                    refused.SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege, privilege);
                    (await AppRoleProbe.ScalarAsync(connection, $"SELECT has_table_privilege('xd_app', 'accounts.report_client_codes', '{privilege}')", Ct)).ShouldBe(false, privilege);
                }

                (await AppRoleProbe.ScalarAsync(connection, "SELECT has_table_privilege('xd_app', 'accounts.report_client_codes', 'SELECT')", Ct)).ShouldBe(true);
                (await AppRoleProbe.ScalarAsync(connection, "SELECT has_table_privilege('xd_app', 'accounts.report_client_codes', 'INSERT')", Ct)).ShouldBe(true);
            },
            Ct);
    }

    [Fact]
    public async Task A_database_that_already_holds_accounts_migrates_and_rolling_back_drops_only_the_table()
    {
        var admin = new NpgsqlConnectionStringBuilder(factory.AdminConnectionString);
        var scratch = new NpgsqlConnectionStringBuilder(factory.AdminConnectionString) { Database = MigrationDatabase, Pooling = false };
        await using (var connection = new NpgsqlConnection(admin.ConnectionString))
        {
            await connection.OpenAsync(Ct);
            await AppRoleProbe.ExecuteAsync(connection, $"DROP DATABASE IF EXISTS {MigrationDatabase} WITH (FORCE)", Ct);
            await AppRoleProbe.ExecuteAsync(connection, $"CREATE DATABASE {MigrationDatabase}", Ct);
        }

        try
        {
            await using var host = factory.WithWebHostBuilder(b => b.UseSetting("Database:ConnectionString", scratch.ConnectionString));
            using var scope = host.Services.CreateScope();
            var migrator = scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>().GetService<IMigrator>();
            await migrator.MigrateAsync(BeforeMigration, Ct);
            await using var connection = new NpgsqlConnection(scratch.ConnectionString);
            await connection.OpenAsync(Ct);
            await AppRoleProbe.ExecuteAsync(connection, AccountSql, Ct);

            await migrator.MigrateAsync(null, Ct);

            (await AppRoleProbe.ScalarAsync(connection, "SELECT count(*) FROM accounts.report_client_codes", Ct)).ShouldBe(0L);
            await AppRoleProbe.ExecuteAsync(connection, CodeSql(), Ct);

            await migrator.MigrateAsync(BeforeMigration, Ct);

            (await AppRoleProbe.ScalarAsync(connection, "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'accounts' AND table_name = 'report_client_codes'", Ct)).ShouldBe(0L);
            (await AppRoleProbe.ScalarAsync(connection, "SELECT count(*) FROM accounts.broker_accounts", Ct)).ShouldBe(1L);
        }
        finally
        {
            await using var connection = new NpgsqlConnection(admin.ConnectionString);
            await connection.OpenAsync(Ct);
            await AppRoleProbe.ExecuteAsync(connection, $"DROP DATABASE IF EXISTS {MigrationDatabase} WITH (FORCE)", Ct);
        }
    }
}
