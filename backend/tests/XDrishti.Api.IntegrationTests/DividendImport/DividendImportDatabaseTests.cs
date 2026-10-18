using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Api.IntegrationTests.DividendImport;

/// <summary>
/// The tables of migration AddDividendImport (P2-15, ADR 0028): their columns and checks, the identity unique among the dividends that are not voided, the grants — the runtime role reads, inserts and updates batches, aliases and dividends and can never
/// delete them — and a database that already holds dividends when the migration runs and rolls back.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class DividendImportDatabaseTests(ApiFactory factory) : IAsyncLifetime
{
    // These tests write accounts straight into the tables and reset at the start of the test that needs it; the rows must not outlive the class.
    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync() => await factory.ResetAccountsAsync();

    private const string MigrationDatabase = "xd_dividend_import_migration_probe";

    private const string BeforeMigration = "20261014163207_AddDividends";

    private const string AccountId = "00000000-0000-7000-8000-00000000e001";

    private const string Batch = "00000000-0000-7000-8000-00000000e101";

    private const string Dividend = "00000000-0000-7000-8000-00000000e301";

    private const string Alias = "00000000-0000-7000-8000-00000000e401";

    private static readonly string[] Privileges = ["SELECT", "INSERT", "UPDATE"];

    private static readonly string[] Tables = ["dividends", "dividend_batches", "dividend_aliases"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string AccountSql = $"""
        INSERT INTO accounts.broker_accounts (id, slug, name, broker, client_id, api_key_issued_on, color, created_at, updated_at, data_enabled, data_primary, data_standby, live_feed_budget)
        VALUES ('{AccountId}', 'probe-divimp', 'Probe', 'Dhan', '1000000001', current_date, '#1A2B3C', now(), now(), false, false, false, 200);
        """;

    private const string BatchSql = $"""
        INSERT INTO portfolio.dividend_batches (id, account_id, file_name, sha256, parsed, imported, duplicates, bad, amount, status, created_by, created_at)
        VALUES ('{Batch}', '{AccountId}', 'f.csv', '{Sha}', 2, 2, 0, 0, 200, 'imported', 'o', now());
        """;

    private const string Sha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private static string DividendSql(string id = Dividend, string source = "'csv'", string externalId = "'d:abc'", string batchId = $"'{Batch}'", string quantity = "25", string voidedAt = "NULL", string voidedBy = "NULL") =>
        $"""
        INSERT INTO portfolio.dividends (id, account_id, pay_date, amount, source, created_by, created_at, voided_at, voided_by, batch_id, external_id, quantity, scrip_name)
        VALUES ('{id}', '{AccountId}', '2026-10-05', 100, {source}, 'o', now(), {voidedAt}, {voidedBy}, {batchId}, {externalId}, {quantity}, 'Some Name')
        """;

    private static string BatchRow(string id, string status = "'imported'", string undoneAt = "NULL", string counts = "2, 2, 0, 0, 200") =>
        $"INSERT INTO portfolio.dividend_batches (id, account_id, file_name, sha256, parsed, imported, duplicates, bad, amount, status, created_by, created_at, undone_at) VALUES ('{id}', '{AccountId}', 'f.csv', '{Sha}', {counts}, {status}, 'o', now(), {undoneAt})";

    private static async Task<string[]> ColumnsAsync(NpgsqlConnection connection, string table)
    {
        var columns = new List<string>();
        await using var command = new NpgsqlCommand($"SELECT column_name FROM information_schema.columns WHERE table_schema = 'portfolio' AND table_name = '{table}' ORDER BY ordinal_position", connection);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        while (await reader.ReadAsync(Ct))
        {
            columns.Add(reader.GetString(0));
        }

        return [.. columns];
    }

    private async Task<NpgsqlConnection> PreparedAsync()
    {
        await factory.ResetAccountsAsync();
        var connection = new NpgsqlConnection(factory.AdminConnectionString);
        await connection.OpenAsync(Ct);
        await AppRoleProbe.ExecuteAsync(connection, AccountSql + BatchSql, Ct);
        return connection;
    }

    [Fact]
    public async Task The_tables_have_the_columns_of_the_contract()
    {
        await using var connection = new NpgsqlConnection(factory.AdminConnectionString);
        await connection.OpenAsync(Ct);

        (await ColumnsAsync(connection, "dividend_batches")).ShouldBe(["id", "account_id", "file_name", "sha256", "parsed", "imported", "duplicates", "bad", "amount", "status", "created_by", "created_at", "undone_at"]);
        (await ColumnsAsync(connection, "dividend_aliases")).ShouldBe(["id", "account_id", "name_key", "holding_id", "instrument_id", "created_by", "created_at", "updated_at"]);
        (await ColumnsAsync(connection, "dividends")).ShouldContain("batch_id");
    }

    public static TheoryData<string, string> BadRows => new()
    {
        { DividendSql(source: "'csv'", externalId: "NULL"), "ck_dividends_file" },
        { DividendSql(source: "'csv'", batchId: "NULL"), "ck_dividends_file" },
        { DividendSql(source: "'manual'"), "ck_dividends_file" },
        { DividendSql(quantity: "0"), "ck_dividends_quantity" },
        { DividendSql(quantity: "-5"), "ck_dividends_quantity" },
        { DividendSql(source: "'import'", externalId: "NULL", batchId: "NULL"), "ck_dividends_source" },
        { BatchRow("00000000-0000-7000-8000-00000000e102", status: "'maybe'"), "ck_dividend_batches_status" },
        { BatchRow("00000000-0000-7000-8000-00000000e102", status: "'undone'"), "ck_dividend_batches_undone" },
        { BatchRow("00000000-0000-7000-8000-00000000e102", undoneAt: "now()"), "ck_dividend_batches_undone" },
        { BatchRow("00000000-0000-7000-8000-00000000e102", counts: "-1, 2, 0, 0, 200"), "ck_dividend_batches_counts" },
        { BatchRow("00000000-0000-7000-8000-00000000e102", counts: "2, 2, 0, 0, -1"), "ck_dividend_batches_counts" },
        { $"INSERT INTO portfolio.dividend_aliases (id, account_id, name_key, holding_id, instrument_id, created_by, created_at, updated_at) VALUES ('{Alias}', '{AccountId}', 'x', gen_random_uuid(), gen_random_uuid(), 'o', now(), now())", "ck_dividend_aliases_target" },
    };

    [Theory]
    [MemberData(nameof(BadRows))]
    public async Task The_database_refuses_a_row_the_rules_never_produce(string sql, string constraint)
    {
        await using var connection = await PreparedAsync();

        var refused = await Should.ThrowAsync<PostgresException>(() => AppRoleProbe.ExecuteAsync(connection, sql, Ct));

        (refused.SqlState, refused.ConstraintName).ShouldBe((PostgresErrorCodes.CheckViolation, constraint));
    }

    [Fact]
    public async Task A_dividend_must_belong_to_an_existing_batch()
    {
        await using var connection = await PreparedAsync();

        var refused = await Should.ThrowAsync<PostgresException>(() => AppRoleProbe.ExecuteAsync(connection, DividendSql(batchId: $"'{Guid.NewGuid()}'"), Ct));

        (refused.SqlState, refused.ConstraintName).ShouldBe((PostgresErrorCodes.ForeignKeyViolation, "fk_dividends_dividend_batches_batch_id"));
    }

    [Fact]
    public async Task An_identity_is_unique_per_account_among_the_dividends_that_are_not_voided_and_a_voided_ones_is_free()
    {
        await using var connection = await PreparedAsync();
        await AppRoleProbe.ExecuteAsync(connection, DividendSql(), Ct);
        var other = "00000000-0000-7000-8000-00000000e302";

        var twice = await Should.ThrowAsync<PostgresException>(() => AppRoleProbe.ExecuteAsync(connection, DividendSql(other), Ct));
        await AppRoleProbe.ExecuteAsync(connection, "UPDATE portfolio.dividends SET voided_at = now(), voided_by = 'o'", Ct);
        await AppRoleProbe.ExecuteAsync(connection, DividendSql(other), Ct);
        await AppRoleProbe.ExecuteAsync(connection, DividendSql("00000000-0000-7000-8000-00000000e303", externalId: "'d:other'"), Ct);

        (twice.SqlState, twice.ConstraintName).ShouldBe((PostgresErrorCodes.UniqueViolation, "ux_dividends_account_external_active"));
        (await AppRoleProbe.ScalarAsync(connection, "SELECT count(*) FROM portfolio.dividends", Ct)).ShouldBe(3L);
    }

    [Fact]
    public async Task One_alias_per_account_and_name()
    {
        await using var connection = await PreparedAsync();
        const string insert = $"INSERT INTO portfolio.dividend_aliases (id, account_id, name_key, created_by, created_at, updated_at) VALUES ('{Alias}', '{AccountId}', 'orion steel', 'o', now(), now())";
        await AppRoleProbe.ExecuteAsync(connection, insert, Ct);

        var twice = await Should.ThrowAsync<PostgresException>(() => AppRoleProbe.ExecuteAsync(connection, insert.Replace(Alias, Guid.NewGuid().ToString(), StringComparison.Ordinal), Ct));

        (twice.SqlState, twice.ConstraintName).ShouldBe((PostgresErrorCodes.UniqueViolation, "ux_dividend_aliases_account_name"));
    }

    [Fact]
    public async Task A_batch_or_an_alias_saved_from_a_stale_read_is_refused_by_the_row_version()
    {
        await using (var connection = await PreparedAsync())
        {
            await AppRoleProbe.ExecuteAsync(connection, $"INSERT INTO portfolio.dividend_aliases (id, account_id, name_key, created_by, created_at, updated_at) VALUES ('{Alias}', '{AccountId}', 'orion steel', 'o', now(), now())", Ct);
        }

        using var first = factory.Services.CreateScope();
        using var second = factory.Services.CreateScope();
        var one = first.ServiceProvider.GetRequiredService<XDrishtiDbContext>();
        var two = second.ServiceProvider.GetRequiredService<XDrishtiDbContext>();
        var batchId = Guid.Parse(Batch);
        var aliasId = Guid.Parse(Alias);
        var at = DateTimeOffset.UtcNow;
        var batchOne = await one.DividendBatches.SingleAsync(b => b.Id == batchId, Ct);
        var batchTwo = await two.DividendBatches.SingleAsync(b => b.Id == batchId, Ct);
        var aliasOne = await one.DividendAliases.SingleAsync(a => a.Id == aliasId, Ct);
        var aliasTwo = await two.DividendAliases.SingleAsync(a => a.Id == aliasId, Ct);

        batchOne.Undo(at);
        aliasOne.Retarget(null, Guid.NewGuid(), at);
        await one.SaveChangesAsync(Ct);
        batchTwo.Undo(at);
        await Should.ThrowAsync<DbUpdateConcurrencyException>(async () => await two.SaveChangesAsync(Ct));
        two.ChangeTracker.Clear();
        aliasTwo = await two.DividendAliases.SingleAsync(a => a.Id == aliasId, Ct);
        aliasTwo.Retarget(null, Guid.NewGuid(), at);
        await two.SaveChangesAsync(Ct);
    }

    [Fact]
    public async Task The_app_role_can_read_insert_and_update_batches_aliases_and_dividends_but_never_delete_them()
    {
        await AppRoleProbe.RunAsync(
            factory,
            async connection =>
            {
                await AppRoleProbe.ExecuteAsync(connection, AccountSql + BatchSql, Ct);
                await AppRoleProbe.ExecuteAsync(connection, DividendSql(), Ct);
                await AppRoleProbe.ExecuteAsync(connection, $"INSERT INTO portfolio.dividend_aliases (id, account_id, name_key, created_by, created_at, updated_at) VALUES ('{Alias}', '{AccountId}', 'orion steel', 'o', now(), now())", Ct);
                await AppRoleProbe.ExecuteAsync(connection, "UPDATE portfolio.dividends SET voided_at = now(), voided_by = 'o'", Ct);
                await AppRoleProbe.ExecuteAsync(connection, "UPDATE portfolio.dividend_batches SET status = 'undone', undone_at = now()", Ct);
                await AppRoleProbe.ExecuteAsync(connection, "UPDATE portfolio.dividend_aliases SET holding_id = NULL, updated_at = now()", Ct);

                foreach (var table in Tables)
                {
                    var refused = await Should.ThrowAsync<PostgresException>(() => AppRoleProbe.ExecuteAsync(connection, $"DELETE FROM portfolio.{table}", Ct));
                    refused.SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege, table);
                    foreach (var privilege in Privileges)
                    {
                        (await AppRoleProbe.ScalarAsync(connection, $"SELECT has_table_privilege('xd_app', 'portfolio.{table}', '{privilege}')", Ct)).ShouldBe(true, $"{table} {privilege}");
                    }

                    (await AppRoleProbe.ScalarAsync(connection, $"SELECT has_table_privilege('xd_app', 'portfolio.{table}', 'DELETE')", Ct)).ShouldBe(false, $"{table} DELETE");
                }
            },
            Ct);
    }

    [Fact]
    public async Task A_database_that_already_holds_dividends_migrates_and_keeps_them_and_rolling_back_drops_the_file_dividends_the_tables_and_the_columns()
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
            await AppRoleProbe.ExecuteAsync(connection, "INSERT INTO portfolio.dividends (id, account_id, pay_date, amount, source, created_by, created_at) VALUES ('00000000-0000-7000-8000-00000000e501', '" + AccountId + "', '2026-10-05', 10, 'manual', 'o', now())", Ct);

            await migrator.MigrateAsync(null, Ct);

            (await AppRoleProbe.ScalarAsync(connection, "SELECT count(*) FROM portfolio.dividends WHERE source = 'manual' AND batch_id IS NULL AND external_id IS NULL AND quantity IS NULL AND scrip_name IS NULL", Ct)).ShouldBe(1L);
            await AppRoleProbe.ExecuteAsync(connection, BatchSql, Ct);
            await AppRoleProbe.ExecuteAsync(connection, DividendSql(), Ct);
            await AppRoleProbe.ExecuteAsync(connection, DividendSql("00000000-0000-7000-8000-00000000e302", voidedAt: "now()", voidedBy: "'o'"), Ct);
            (await AppRoleProbe.ScalarAsync(connection, "SELECT count(*) FROM portfolio.dividends", Ct)).ShouldBe(3L);

            await migrator.MigrateAsync(BeforeMigration, Ct);

            (await AppRoleProbe.ScalarAsync(connection, "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'portfolio' AND table_name IN ('dividend_batches', 'dividend_aliases')", Ct)).ShouldBe(0L);
            (await AppRoleProbe.ScalarAsync(connection, "SELECT count(*) FROM information_schema.columns WHERE table_schema = 'portfolio' AND table_name = 'dividends' AND column_name IN ('batch_id', 'external_id', 'quantity', 'scrip_name')", Ct)).ShouldBe(0L);
            (await AppRoleProbe.ScalarAsync(connection, "SELECT count(*) FROM portfolio.dividends", Ct)).ShouldBe(1L); // the voided file dividend went with the live one: the history of imports and undos is lost
            var csv = await Should.ThrowAsync<PostgresException>(() => AppRoleProbe.ExecuteAsync(connection, "INSERT INTO portfolio.dividends (id, account_id, pay_date, amount, source, created_by, created_at) VALUES (gen_random_uuid(), '" + AccountId + "', '2026-10-05', 10, 'csv', 'o', now())", Ct));
            csv.ConstraintName.ShouldBe("ck_dividends_source");
        }
        finally
        {
            await using var connection = new NpgsqlConnection(admin.ConnectionString);
            await connection.OpenAsync(Ct);
            await AppRoleProbe.ExecuteAsync(connection, $"DROP DATABASE IF EXISTS {MigrationDatabase} WITH (FORCE)", Ct);
        }
    }
}
