using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Api.IntegrationTests.Trades;

/// <summary>
/// The two tables of migration AddTradeTransactions (P2-03): their columns, checks and the partial unique index, the grants — the runtime role reads, inserts and updates trades and batches and can never delete them, while the
/// lots keep their DELETE (a rebuild replaces them) — and a database that already holds lots when the migration runs.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class TradesDatabaseTests(ApiFactory factory)
{
    private const string MigrationDatabase = "xd_trades_migration_probe";

    private const string BeforeMigration = "20261013131938_AddActionCenterAndPriceAlerts";

    private const string Account = "00000000-0000-7000-8000-00000000a001";

    private const string Bucket = "00000000-0000-7000-8000-0000000000b1";

    private const string Holding = "00000000-0000-7000-8000-0000000000c1";

    private static readonly string[] Tables = ["transactions", "trade_batches"];

    private static readonly string[] Privileges = ["SELECT", "INSERT", "UPDATE"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string AccountSql = $"""
        INSERT INTO accounts.broker_accounts (id, slug, name, broker, client_id, api_key_issued_on, color, created_at, updated_at, data_enabled, data_primary, data_standby, live_feed_budget)
        VALUES ('{Account}', 'probe', 'Probe', 'Dhan', '1000000001', current_date, '#1A2B3C', now(), now(), false, false, false, 200);
        """;

    /// <summary>An insert of a trade of the probe account; each parameter is the SQL text of a column's value.</summary>
    private static string Trade(string id, string external, string side = "'buy'", string source = "'csv'", string qty = "10", string price = "90", string charges = "0", string voidedAt = "NULL", string voidedBy = "NULL") =>
        $"""
        INSERT INTO portfolio.transactions (id, account_id, trading_symbol, exchange, side, trade_date, qty, price, charges, source, seq, external_id, created_by, created_at, voided_at, voided_by)
        VALUES ('{id}', '{Account}', 'EXMPL', 'NSE', {side}, '2026-10-05', {qty}, {price}, {charges}, {source}, 1, '{external}', 'o', now(), {voidedAt}, {voidedBy})
        """;

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
    public async Task The_tables_have_the_columns_of_the_contract_and_the_partial_unique_index()
    {
        await using var connection = new NpgsqlConnection(factory.AdminConnectionString);
        await connection.OpenAsync(Ct);

        (await ColumnsAsync(connection, "transactions")).ShouldBe(
        [
            "id", "account_id", "instrument_id", "isin", "trading_symbol", "exchange", "side", "trade_date", "trade_time", "qty", "price", "charges", "source", "batch_id", "seq", "external_id", "created_by", "created_at", "voided_at", "voided_by", "run_id", "superseded_by",
        ]);
        (await ColumnsAsync(connection, "trade_batches")).ShouldBe(["id", "account_id", "file_name", "sha256", "parsed", "imported", "duplicates", "skipped", "bad", "status", "created_by", "created_at", "undone_at"]);
        var index = (await AppRoleProbe.ScalarAsync(connection, "SELECT indexdef FROM pg_indexes WHERE schemaname = 'portfolio' AND indexname = 'ux_transactions_account_external_active'", Ct))!.ToString()!;
        index.ShouldContain("WHERE (voided_at IS NULL)");
    }

    public static TheoryData<string, string> BadTrades => new()
    {
        { Trade("00000000-0000-7000-8000-00000000f001", "t:1", side: "'hold'"), "ck_transactions_side" },
        { Trade("00000000-0000-7000-8000-00000000f001", "t:1", source: "'api'"), "ck_transactions_source" },
        { Trade("00000000-0000-7000-8000-00000000f001", "t:1", qty: "0"), "ck_transactions_qty" },
        { Trade("00000000-0000-7000-8000-00000000f001", "t:1", price: "0"), "ck_transactions_price" },
        { Trade("00000000-0000-7000-8000-00000000f001", "t:1", charges: "-1"), "ck_transactions_charges" },
        { Trade("00000000-0000-7000-8000-00000000f001", "t:1", voidedAt: "now()"), "ck_transactions_voided" },
        { Trade("00000000-0000-7000-8000-00000000f001", "t:1", voidedBy: "'o'"), "ck_transactions_voided" },
    };

    [Theory]
    [MemberData(nameof(BadTrades))]
    public async Task The_database_refuses_a_trade_the_rules_never_produce(string sql, string constraint)
    {
        await factory.ResetAccountsAsync();
        await using var connection = new NpgsqlConnection(factory.AdminConnectionString);
        await connection.OpenAsync(Ct);
        await AppRoleProbe.ExecuteAsync(connection, AccountSql, Ct);

        var refused = await Should.ThrowAsync<PostgresException>(() => AppRoleProbe.ExecuteAsync(connection, sql, Ct));

        (refused.SqlState, refused.ConstraintName).ShouldBe((PostgresErrorCodes.CheckViolation, constraint));
    }

    [Fact]
    public async Task A_trade_identity_is_unique_among_the_trades_that_are_not_voided_only()
    {
        await factory.ResetAccountsAsync();
        await using var connection = new NpgsqlConnection(factory.AdminConnectionString);
        await connection.OpenAsync(Ct);
        await AppRoleProbe.ExecuteAsync(connection, AccountSql, Ct);
        var first = Guid.NewGuid().ToString();
        await AppRoleProbe.ExecuteAsync(connection, Trade(first, "t:1"), Ct);

        var twin = await Should.ThrowAsync<PostgresException>(() => AppRoleProbe.ExecuteAsync(connection, Trade(Guid.NewGuid().ToString(), "t:1"), Ct));
        await AppRoleProbe.ExecuteAsync(connection, $"UPDATE portfolio.transactions SET voided_at = now(), voided_by = 'o' WHERE id = '{first}'", Ct);
        await AppRoleProbe.ExecuteAsync(connection, Trade(Guid.NewGuid().ToString(), "t:1"), Ct);
        await AppRoleProbe.ExecuteAsync(connection, Trade(Guid.NewGuid().ToString(), "t:1", voidedAt: "now()", voidedBy: "'o'"), Ct);

        (twin.SqlState, twin.ConstraintName).ShouldBe((PostgresErrorCodes.UniqueViolation, "ux_transactions_account_external_active"));
        (await AppRoleProbe.ScalarAsync(connection, "SELECT count(*) FROM portfolio.transactions WHERE external_id = 't:1'", Ct)).ShouldBe(3L);
    }

    [Fact]
    public async Task The_lot_source_trade_is_allowed_and_other_sources_still_are_not()
    {
        await factory.ResetAccountsAsync();
        await using var connection = new NpgsqlConnection(factory.AdminConnectionString);
        await connection.OpenAsync(Ct);
        await AppRoleProbe.ExecuteAsync(connection, AccountSql + $"""
            INSERT INTO accounts.portfolio_buckets (id, name, created_at) VALUES ('{Bucket}', 'B', now());
            INSERT INTO portfolio.holdings (id, account_id, bucket_id, exchange, security_id, trading_symbol, name, qty, avg_cost, last_price, status, first_seen_at, synced_at)
            VALUES ('{Holding}', '{Account}', '{Bucket}', 'ALL', '1', 'S', 'S', 10, 1, 1, 'open', now(), now());
            """, Ct);
        const string Lot = "INSERT INTO portfolio.holding_lots (id, holding_id, acquired_on, qty, unit_cost, source, uses_default_date, created_at, estimated) VALUES (gen_random_uuid(), '" + Holding + "', '2025-01-01', 1, 1, '{0}', false, now(), false)";

        await AppRoleProbe.ExecuteAsync(connection, string.Format(System.Globalization.CultureInfo.InvariantCulture, Lot, "trade"), Ct);
        var refused = await Should.ThrowAsync<PostgresException>(() => AppRoleProbe.ExecuteAsync(connection, string.Format(System.Globalization.CultureInfo.InvariantCulture, Lot, "bought"), Ct));

        refused.ConstraintName.ShouldBe("ck_holding_lots_source");
    }

    [Fact]
    public async Task The_app_role_can_read_insert_and_update_trades_and_batches_but_never_delete_them_while_lots_keep_their_delete()
    {
        await AppRoleProbe.RunAsync(
            factory,
            async connection =>
            {
                await AppRoleProbe.ExecuteAsync(connection, AccountSql + $"""
                    INSERT INTO accounts.portfolio_buckets (id, name, created_at) VALUES ('{Bucket}', 'B', now());
                    INSERT INTO portfolio.holdings (id, account_id, bucket_id, exchange, security_id, trading_symbol, name, qty, avg_cost, last_price, status, first_seen_at, synced_at)
                    VALUES ('{Holding}', '{Account}', '{Bucket}', 'ALL', '1', 'S', 'S', 10, 1, 1, 'open', now(), now());
                    INSERT INTO portfolio.holding_lots (id, holding_id, acquired_on, qty, unit_cost, source, uses_default_date, created_at, estimated) VALUES (gen_random_uuid(), '{Holding}', '2025-01-01', 1, 1, 'trade', false, now(), false);
                    INSERT INTO portfolio.trade_batches (id, account_id, file_name, sha256, parsed, imported, duplicates, skipped, bad, status, created_by, created_at)
                    VALUES ('00000000-0000-7000-8000-00000000ba01', '{Account}', 'a.csv', '{new string('a', 64)}', 1, 1, 0, 0, 0, 'imported', 'o', now());
                    """, Ct);
                await AppRoleProbe.ExecuteAsync(connection, Trade("00000000-0000-7000-8000-00000000f001", "t:1"), Ct);
                await AppRoleProbe.ExecuteAsync(connection, "UPDATE portfolio.transactions SET voided_at = now(), voided_by = 'o'", Ct);
                await AppRoleProbe.ExecuteAsync(connection, "UPDATE portfolio.trade_batches SET status = 'undone', undone_at = now()", Ct);
                (await AppRoleProbe.ScalarAsync(connection, "SELECT count(*) FROM portfolio.transactions", Ct)).ShouldBe(1L);

                var trades = await Should.ThrowAsync<PostgresException>(() => AppRoleProbe.ExecuteAsync(connection, "DELETE FROM portfolio.transactions", Ct));
                var batches = await Should.ThrowAsync<PostgresException>(() => AppRoleProbe.ExecuteAsync(connection, "DELETE FROM portfolio.trade_batches", Ct));
                (trades.SqlState, batches.SqlState).ShouldBe((PostgresErrorCodes.InsufficientPrivilege, PostgresErrorCodes.InsufficientPrivilege));
                await AppRoleProbe.ExecuteAsync(connection, "DELETE FROM portfolio.holding_lots", Ct);
                foreach (var table in Tables)
                {
                    foreach (var privilege in Privileges)
                    {
                        (await AppRoleProbe.ScalarAsync(connection, $"SELECT has_table_privilege('xd_app', 'portfolio.{table}', '{privilege}')", Ct)).ShouldBe(true, $"{table} {privilege}");
                    }
                }
            },
            Ct);
    }

    [Fact]
    public async Task A_database_that_already_holds_lots_migrates_and_keeps_them()
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
            await AppRoleProbe.ExecuteAsync(connection, AccountSql + $"""
                INSERT INTO accounts.portfolio_buckets (id, name, created_at) VALUES ('{Bucket}', 'B', now());
                INSERT INTO portfolio.holdings (id, account_id, bucket_id, exchange, security_id, trading_symbol, name, qty, avg_cost, last_price, status, first_seen_at, synced_at)
                VALUES ('{Holding}', '{Account}', '{Bucket}', 'ALL', '1', 'S', 'S', 10, 1, 1, 'open', now(), now());
                INSERT INTO portfolio.holding_lots (id, holding_id, acquired_on, qty, unit_cost, source, uses_default_date, created_at, estimated) VALUES (gen_random_uuid(), '{Holding}', '2025-01-01', 10, 1, 'import', true, now(), false);
                """, Ct);

            await migrator.MigrateAsync(null, Ct);

            (await AppRoleProbe.ScalarAsync(connection, "SELECT count(*) FROM portfolio.holding_lots WHERE source = 'import' AND uses_default_date", Ct)).ShouldBe(1L);
            (await AppRoleProbe.ScalarAsync(connection, "SELECT count(*) FROM portfolio.transactions", Ct)).ShouldBe(0L);
            (await AppRoleProbe.ScalarAsync(connection, "SELECT count(*) FROM portfolio.trade_batches", Ct)).ShouldBe(0L);
        }
        finally
        {
            await using var connection = new NpgsqlConnection(admin.ConnectionString);
            await connection.OpenAsync(Ct);
            await AppRoleProbe.ExecuteAsync(connection, $"DROP DATABASE IF EXISTS {MigrationDatabase} WITH (FORCE)", Ct);
        }
    }

    [Fact]
    public async Task Rolling_the_migration_back_turns_the_lots_from_trades_into_imported_lots_so_the_old_check_holds()
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
            await migrator.MigrateAsync(null, Ct);
            await using var connection = new NpgsqlConnection(scratch.ConnectionString);
            await connection.OpenAsync(Ct);
            await AppRoleProbe.ExecuteAsync(connection, AccountSql + $"""
                INSERT INTO accounts.portfolio_buckets (id, name, created_at) VALUES ('{Bucket}', 'B', now());
                INSERT INTO portfolio.holdings (id, account_id, bucket_id, exchange, security_id, trading_symbol, name, qty, avg_cost, last_price, status, first_seen_at, synced_at)
                VALUES ('{Holding}', '{Account}', '{Bucket}', 'ALL', '1', 'S', 'S', 10, 1, 1, 'open', now(), now());
                INSERT INTO portfolio.holding_lots (id, holding_id, acquired_on, qty, unit_cost, source, uses_default_date, created_at, estimated) VALUES (gen_random_uuid(), '{Holding}', '2025-01-01', 10, 1, 'trade', false, now(), false);
                """, Ct);

            await migrator.MigrateAsync(BeforeMigration, Ct);

            (await AppRoleProbe.ScalarAsync(connection, "SELECT source FROM portfolio.holding_lots", Ct)).ShouldBe("import");
            (await AppRoleProbe.ScalarAsync(connection, "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'portfolio' AND table_name IN ('transactions', 'trade_batches')", Ct)).ShouldBe(0L);
        }
        finally
        {
            await using var connection = new NpgsqlConnection(admin.ConnectionString);
            await connection.OpenAsync(Ct);
            await AppRoleProbe.ExecuteAsync(connection, $"DROP DATABASE IF EXISTS {MigrationDatabase} WITH (FORCE)", Ct);
        }
    }
}
