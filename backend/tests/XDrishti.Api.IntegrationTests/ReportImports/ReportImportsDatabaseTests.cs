using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Api.IntegrationTests.ReportImports;

/// <summary>
/// The tables and columns of migration AddReportImports (P2-17, ADR 0029): their checks, the identity unique among the ledger entries that are not voided, the grants - the runtime role reads, inserts and updates cost batches and ledger entries, only reads and
/// inserts the names files taught, and can never delete any of them - and a database that already holds a ledger and trades when the migration runs and rolls back.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class ReportImportsDatabaseTests(ApiFactory factory)
{
    private const string MigrationDatabase = "xd_report_imports_migration_probe";

    private const string BeforeMigration = "20261015052512_AddSyncHealCounters";

    private const string AccountId = "00000000-0000-7000-8000-00000000f001";

    private const string Batch = "00000000-0000-7000-8000-00000000f101";

    private const string Run = "00000000-0000-7000-8000-00000000f201";

    private const string EmptyJson = "{}";

    private static readonly string[] Tables = ["cost_batches", "ledger_entries", "security_aliases"];

    private const string Sha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string AccountSql = $"""
        INSERT INTO accounts.broker_accounts (id, slug, name, broker, client_id, api_key_issued_on, color, created_at, updated_at, data_enabled, data_primary, data_standby, live_feed_budget)
        VALUES ('{AccountId}', 'probe-reports', 'Probe', 'Dhan', '1000000001', current_date, '#1A2B3C', now(), now(), false, false, false, 200);
        """;

    private const string RunSql = $"""
        INSERT INTO portfolio.broker_sync_runs (id, account_id, kind, trigger, from_date, to_date, status, counts, started_by, started_at)
        VALUES ('{Run}', '{AccountId}', 'trades', 'manual', '2026-10-05', '2026-10-12', 'running', '{EmptyJson}', 'o', now());
        """;

    private const string BatchSql = $"""
        INSERT INTO portfolio.cost_batches (id, account_id, file_name, sha256, parsed, imported, duplicates, bad, amount, status, created_by, created_at)
        VALUES ('{Batch}', '{AccountId}', 'dp.xls', '{Sha}', 3, 1, 2, 0, 12.5, 'imported', 'o', now());
        """;

    private static string BrokerEntry(string external = "e1", string runId = $"'{Run}'") =>
        $"""
        INSERT INTO portfolio.ledger_entries (id, account_id, entry_date, voucher_no, description, narration, amount, class, external_id, run_id, created_at, source)
        VALUES (gen_random_uuid(), '{AccountId}', '2026-10-05', 'V1', 'DP CHARGES', 'x', -12.5, 'charges', '{external}', {runId}, now(), 'broker')
        """;

    private static string ImportedEntry(string external = "d:1", string batchId = $"'{Batch}'", string source = "'import'", string quantity = "11", string voidedAt = "NULL", string voidedBy = "NULL", string runId = "NULL") =>
        $"""
        INSERT INTO portfolio.ledger_entries (id, account_id, entry_date, voucher_no, description, narration, amount, class, external_id, run_id, created_at, source, batch_id, isin, quantity, voided_at, voided_by)
        VALUES (gen_random_uuid(), '{AccountId}', '2026-06-04', '', 'DP charges', 'DP charges - Other Fees', -12.5, 'charges', '{external}', {runId}, now(), {source}, {batchId}, 'INE245A01021', {quantity}, {voidedAt}, {voidedBy})
        """;

    private static string TradeSql(string source, string supersededBy = "NULL", string voidedAt = "NULL", string voidedBy = "NULL", string external = "g:1", string price = "23.209999") =>
        $"""
        INSERT INTO portfolio.transactions (id, account_id, isin, trading_symbol, exchange, side, trade_date, qty, price, charges, source, seq, external_id, created_by, created_at, superseded_by, voided_at, voided_by)
        VALUES (gen_random_uuid(), '{AccountId}', 'INE000A01011', 'EXMPL', 'NSE', 'buy', '2026-10-05', 10, {price}, 0, '{source}', 1, '{external}', 'o', now(), {supersededBy}, {voidedAt}, {voidedBy})
        """;

    private async Task<NpgsqlConnection> PreparedAsync()
    {
        await factory.ResetAccountsAsync();
        var connection = new NpgsqlConnection(factory.AdminConnectionString);
        await connection.OpenAsync(Ct);
        await AppRoleProbe.ExecuteAsync(connection, AccountSql + RunSql + BatchSql, Ct);
        return connection;
    }

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

    [Fact]
    public async Task The_tables_and_columns_are_those_of_the_contract()
    {
        await using var connection = new NpgsqlConnection(factory.AdminConnectionString);
        await connection.OpenAsync(Ct);

        (await ColumnsAsync(connection, "cost_batches")).ShouldBe(["id", "account_id", "file_name", "sha256", "parsed", "imported", "duplicates", "bad", "amount", "status", "created_by", "created_at", "undone_at"]);
        (await ColumnsAsync(connection, "security_aliases")).ShouldBe(["id", "account_id", "name_key", "isin", "name", "source", "created_by", "created_at"]);
        (await ColumnsAsync(connection, "ledger_entries")).ShouldBe(
            ["id", "account_id", "entry_date", "voucher_no", "description", "narration", "exchange", "amount", "class", "running_balance", "external_id", "run_id", "created_at", "batch_id", "isin", "quantity", "source", "voided_at", "voided_by"]);
        (await ColumnsAsync(connection, "transactions")).ShouldContain("superseded_by");
        (await AppRoleProbe.ScalarAsync(connection, "SELECT numeric_scale FROM information_schema.columns WHERE table_schema = 'portfolio' AND table_name = 'transactions' AND column_name = 'price'", Ct)).ShouldBe(6);
    }

    public static TheoryData<string, string> BadRows => new()
    {
        { BrokerEntry(runId: "NULL"), "ck_ledger_entries_origin" },
        { ImportedEntry(batchId: "NULL"), "ck_ledger_entries_origin" },
        { ImportedEntry(runId: $"'{Run}'"), "ck_ledger_entries_origin" },
        { ImportedEntry(source: "'file'", batchId: "NULL"), "ck_ledger_entries_source" },
        { ImportedEntry(quantity: "0"), "ck_ledger_entries_quantity" },
        { ImportedEntry(voidedAt: "now()"), "ck_ledger_entries_voided" },
        { ImportedEntry(voidedBy: "'o'"), "ck_ledger_entries_voided" },
        { TradeSql("bill", supersededBy: $"'{Run}'"), "ck_transactions_superseded" },
        { TradeSql("csv", supersededBy: $"'{Run}'", voidedAt: "now()", voidedBy: "'superseded'"), "ck_transactions_superseded" },
        { TradeSql("nope"), "ck_transactions_source" },
        { "INSERT INTO portfolio.cost_batches (id, account_id, file_name, sha256, parsed, imported, duplicates, bad, amount, status, created_by, created_at) VALUES (gen_random_uuid(), '" + AccountId + "', 'f', '" + Sha + "', 1, 1, 0, 0, 1, 'maybe', 'o', now())", "ck_cost_batches_status" },
        { "INSERT INTO portfolio.cost_batches (id, account_id, file_name, sha256, parsed, imported, duplicates, bad, amount, status, created_by, created_at) VALUES (gen_random_uuid(), '" + AccountId + "', 'f', '" + Sha + "', 1, 1, 0, 0, -1, 'imported', 'o', now())", "ck_cost_batches_counts" },
        { "INSERT INTO portfolio.cost_batches (id, account_id, file_name, sha256, parsed, imported, duplicates, bad, amount, status, created_by, created_at) VALUES (gen_random_uuid(), '" + AccountId + "', 'f', '" + Sha + "', 1, 1, 0, 0, 1, 'undone', 'o', now())", "ck_cost_batches_undone" },
        { "INSERT INTO portfolio.security_aliases (id, account_id, name_key, isin, name, source, created_by, created_at) VALUES (gen_random_uuid(), '" + AccountId + "', 'k', 'INE000A01011', 'n', 'web', 'o', now())", "ck_security_aliases_source" },
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
    public async Task The_good_rows_of_every_kind_are_accepted()
    {
        await using var connection = await PreparedAsync();

        await AppRoleProbe.ExecuteAsync(connection, BrokerEntry(), Ct);
        await AppRoleProbe.ExecuteAsync(connection, ImportedEntry(), Ct);
        await AppRoleProbe.ExecuteAsync(connection, ImportedEntry("d:2", voidedAt: "now()", voidedBy: "'o'"), Ct);
        await AppRoleProbe.ExecuteAsync(connection, TradeSql("bill", supersededBy: $"'{Run}'", voidedAt: "now()", voidedBy: "'superseded'"), Ct);
        await AppRoleProbe.ExecuteAsync(connection, TradeSql("bill", external: "g:2"), Ct);

        (await AppRoleProbe.ScalarAsync(connection, "SELECT count(*) FROM portfolio.ledger_entries", Ct)).ShouldBe(3L);
        (await AppRoleProbe.ScalarAsync(connection, "SELECT price FROM portfolio.transactions WHERE external_id = 'g:2'", Ct)).ShouldBe(23.209999m);
    }

    [Fact]
    public async Task An_entry_must_belong_to_an_existing_batch()
    {
        await using var connection = await PreparedAsync();

        var refused = await Should.ThrowAsync<PostgresException>(() => AppRoleProbe.ExecuteAsync(connection, ImportedEntry(batchId: $"'{Guid.NewGuid()}'"), Ct));

        (refused.SqlState, refused.ConstraintName).ShouldBe((PostgresErrorCodes.ForeignKeyViolation, "fk_ledger_entries_cost_batches_batch_id"));
    }

    [Fact]
    public async Task An_identity_is_unique_per_account_among_the_ledger_entries_that_are_not_voided_and_a_voided_ones_is_free()
    {
        await using var connection = await PreparedAsync();
        await AppRoleProbe.ExecuteAsync(connection, ImportedEntry(), Ct);

        var twice = await Should.ThrowAsync<PostgresException>(() => AppRoleProbe.ExecuteAsync(connection, ImportedEntry(), Ct));
        await AppRoleProbe.ExecuteAsync(connection, "UPDATE portfolio.ledger_entries SET voided_at = now(), voided_by = 'o'", Ct);
        await AppRoleProbe.ExecuteAsync(connection, ImportedEntry(), Ct);

        (twice.SqlState, twice.ConstraintName).ShouldBe((PostgresErrorCodes.UniqueViolation, "ux_ledger_entries_account_external"));
        (await AppRoleProbe.ScalarAsync(connection, "SELECT count(*) FROM portfolio.ledger_entries", Ct)).ShouldBe(2L);
    }

    [Fact]
    public async Task One_learned_name_per_account_name_and_isin_but_a_name_may_lead_to_two_isins()
    {
        await using var connection = await PreparedAsync();
        string Insert(string isin) => $"INSERT INTO portfolio.security_aliases (id, account_id, name_key, isin, name, source, created_by, created_at) VALUES (gen_random_uuid(), '{AccountId}', 'orion steel', '{isin}', 'ORION STEEL', 'demat', 'o', now())";
        await AppRoleProbe.ExecuteAsync(connection, Insert("INE902B01012"), Ct);

        var twice = await Should.ThrowAsync<PostgresException>(() => AppRoleProbe.ExecuteAsync(connection, Insert("INE902B01012"), Ct));
        await AppRoleProbe.ExecuteAsync(connection, Insert("INE902B01020"), Ct);

        (twice.SqlState, twice.ConstraintName).ShouldBe((PostgresErrorCodes.UniqueViolation, "ux_security_aliases_account_name_isin"));
    }

    [Fact]
    public async Task The_app_role_reads_inserts_and_updates_cost_batches_and_ledger_entries_and_only_reads_and_inserts_learned_names_and_deletes_none_of_them()
    {
        await AppRoleProbe.RunAsync(
            factory,
            async connection =>
            {
                await AppRoleProbe.ExecuteAsync(connection, AccountSql + RunSql + BatchSql, Ct);
                await AppRoleProbe.ExecuteAsync(connection, ImportedEntry(), Ct);
                await AppRoleProbe.ExecuteAsync(connection, "INSERT INTO portfolio.security_aliases (id, account_id, name_key, isin, name, source, created_by, created_at) VALUES (gen_random_uuid(), '" + AccountId + "', 'k', 'INE000A01011', 'n', 'dp', 'o', now())", Ct);
                await AppRoleProbe.ExecuteAsync(connection, "UPDATE portfolio.ledger_entries SET voided_at = now(), voided_by = 'o'", Ct);
                await AppRoleProbe.ExecuteAsync(connection, "UPDATE portfolio.cost_batches SET status = 'undone', undone_at = now()", Ct);

                foreach (var table in Tables)
                {
                    var refused = await Should.ThrowAsync<PostgresException>(() => AppRoleProbe.ExecuteAsync(connection, $"DELETE FROM portfolio.{table}", Ct));
                    refused.SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege, table);
                    (await AppRoleProbe.ScalarAsync(connection, $"SELECT has_table_privilege('xd_app', 'portfolio.{table}', 'DELETE')", Ct)).ShouldBe(false, $"{table} DELETE");
                    (await AppRoleProbe.ScalarAsync(connection, $"SELECT has_table_privilege('xd_app', 'portfolio.{table}', 'SELECT')", Ct)).ShouldBe(true, $"{table} SELECT");
                    (await AppRoleProbe.ScalarAsync(connection, $"SELECT has_table_privilege('xd_app', 'portfolio.{table}', 'INSERT')", Ct)).ShouldBe(true, $"{table} INSERT");
                }

                (await AppRoleProbe.ScalarAsync(connection, "SELECT has_table_privilege('xd_app', 'portfolio.security_aliases', 'UPDATE')", Ct)).ShouldBe(false);
                (await AppRoleProbe.ScalarAsync(connection, "SELECT has_table_privilege('xd_app', 'portfolio.cost_batches', 'UPDATE')", Ct)).ShouldBe(true);
                (await AppRoleProbe.ScalarAsync(connection, "SELECT has_table_privilege('xd_app', 'portfolio.ledger_entries', 'UPDATE')", Ct)).ShouldBe(true);
            },
            Ct);
    }

    [Fact]
    public async Task A_database_that_already_holds_a_ledger_and_trades_migrates_keeps_them_and_rolling_back_drops_the_new_rows_tables_and_columns()
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
            await AppRoleProbe.ExecuteAsync(connection, AccountSql + RunSql, Ct);
            await AppRoleProbe.ExecuteAsync(connection, BrokerEntryBefore, Ct);
            await AppRoleProbe.ExecuteAsync(connection, TradeBefore, Ct);

            await migrator.MigrateAsync(null, Ct);

            (await AppRoleProbe.ScalarAsync(connection, "SELECT count(*) FROM portfolio.ledger_entries WHERE source = 'broker' AND run_id IS NOT NULL AND batch_id IS NULL AND voided_at IS NULL", Ct)).ShouldBe(1L);
            (await AppRoleProbe.ScalarAsync(connection, "SELECT price FROM portfolio.transactions", Ct)).ShouldBe(90.5m);
            await AppRoleProbe.ExecuteAsync(connection, BatchSql, Ct);
            await AppRoleProbe.ExecuteAsync(connection, ImportedEntry(), Ct);
            await AppRoleProbe.ExecuteAsync(connection, TradeSql("bill"), Ct);

            await migrator.MigrateAsync(BeforeMigration, Ct);

            (await AppRoleProbe.ScalarAsync(connection, "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'portfolio' AND table_name IN ('cost_batches', 'security_aliases')", Ct)).ShouldBe(0L);
            (await AppRoleProbe.ScalarAsync(connection, "SELECT count(*) FROM information_schema.columns WHERE table_schema = 'portfolio' AND table_name = 'ledger_entries' AND column_name IN ('source', 'batch_id', 'isin', 'quantity', 'voided_at', 'voided_by')", Ct)).ShouldBe(0L);
            (await AppRoleProbe.ScalarAsync(connection, "SELECT count(*) FROM portfolio.ledger_entries", Ct)).ShouldBe(1L); // the imported cost went; the one read from Dhan stayed
            (await AppRoleProbe.ScalarAsync(connection, "SELECT count(*) FROM portfolio.transactions", Ct)).ShouldBe(1L); // the bill-level trade went
            var bill = await Should.ThrowAsync<PostgresException>(() => AppRoleProbe.ExecuteAsync(connection, TradeSql("bill", external: "g:9").Replace(", superseded_by, voided_at, voided_by)", ")", StringComparison.Ordinal).Replace(", NULL, NULL, NULL)", ")", StringComparison.Ordinal), Ct));
            bill.ConstraintName.ShouldBe("ck_transactions_source");
        }
        finally
        {
            await using var connection = new NpgsqlConnection(admin.ConnectionString);
            await connection.OpenAsync(Ct);
            await AppRoleProbe.ExecuteAsync(connection, $"DROP DATABASE IF EXISTS {MigrationDatabase} WITH (FORCE)", Ct);
        }
    }

    private const string BrokerEntryBefore = $"""
        INSERT INTO portfolio.ledger_entries (id, account_id, entry_date, voucher_no, description, narration, amount, class, external_id, run_id, created_at)
        VALUES (gen_random_uuid(), '{AccountId}', '2026-10-05', 'V1', 'DP CHARGES', 'x', -12.5, 'charges', 'e0', '{Run}', now())
        """;

    private const string TradeBefore = $"""
        INSERT INTO portfolio.transactions (id, account_id, isin, trading_symbol, exchange, side, trade_date, qty, price, charges, source, seq, external_id, created_by, created_at)
        VALUES (gen_random_uuid(), '{AccountId}', 'INE000A01011', 'EXMPL', 'NSE', 'buy', '2026-10-05', 10, 90.5, 0, 'broker', 1, 't:20261005:T1', 'o', now())
        """;
}
