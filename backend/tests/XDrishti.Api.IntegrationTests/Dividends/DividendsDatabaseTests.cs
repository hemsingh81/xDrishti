using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Api.IntegrationTests.Dividends;

/// <summary>
/// The two tables of migration AddDividends (P2-14, ADR 0026): their columns and checks, the grants — the runtime role reads, inserts and updates dividends and the decisions about suggested ones and can never delete them — and a database that already
/// holds a ledger when the migration runs.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class DividendsDatabaseTests(ApiFactory factory)
{
    private const string MigrationDatabase = "xd_dividends_migration_probe";

    private const string BeforeMigration = "20261014132439_AddDhanTradeAndLedgerSync";

    private const string AccountId = "00000000-0000-7000-8000-00000000d001";

    private const string Run = "00000000-0000-7000-8000-00000000d101";

    private const string Entry = "00000000-0000-7000-8000-00000000d201";

    private const string Dividend = "00000000-0000-7000-8000-00000000d301";

    private static readonly string[] Tables = ["dividends", "dividend_suggestions"];

    private static readonly string[] Privileges = ["SELECT", "INSERT", "UPDATE"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string SetupSql = $"""
        INSERT INTO accounts.broker_accounts (id, slug, name, broker, client_id, api_key_issued_on, color, created_at, updated_at, data_enabled, data_primary, data_standby, live_feed_budget)
        VALUES ('{AccountId}', 'probe-div', 'Probe', 'Dhan', '1000000001', current_date, '#1A2B3C', now(), now(), false, false, false, 200);
        INSERT INTO portfolio.broker_sync_runs (id, account_id, kind, trigger, from_date, to_date, status, counts, started_by, started_at)
        VALUES ('{Run}', '{AccountId}', 'ledger', 'manual', '2026-10-05', '2026-10-12', 'succeeded', jsonb_build_object(), 'o', now());
        INSERT INTO portfolio.ledger_entries (id, account_id, entry_date, voucher_no, description, narration, amount, class, external_id, run_id, created_at)
        VALUES ('{Entry}', '{AccountId}', '2026-10-05', 'V1', 'RECEIPT', 'DIVIDEND', 10, 'dividend', 'abc', '{Run}', now());
        """;

    /// <summary>An insert of a dividend of the probe account; each parameter is the SQL text of a column's value.</summary>
    private static string DividendSql(string id = Dividend, string amount = "100", string perShare = "NULL", string source = "'manual'", string voidedAt = "NULL", string voidedBy = "NULL") =>
        $"""
        INSERT INTO portfolio.dividends (id, account_id, pay_date, amount, per_share, source, created_by, created_at, voided_at, voided_by)
        VALUES ('{id}', '{AccountId}', '2026-10-05', {amount}, {perShare}, {source}, 'o', now(), {voidedAt}, {voidedBy})
        """;

    private static string DecisionSql(string state, string dividendId) =>
        $"INSERT INTO portfolio.dividend_suggestions (id, account_id, state, dividend_id, decided_by, decided_at) VALUES ('{Entry}', '{AccountId}', {state}, {dividendId}, 'o', now())";

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
        await AppRoleProbe.ExecuteAsync(connection, SetupSql, Ct);
        return connection;
    }

    [Fact]
    public async Task The_tables_have_the_columns_of_the_contract()
    {
        await using var connection = new NpgsqlConnection(factory.AdminConnectionString);
        await connection.OpenAsync(Ct);

        (await ColumnsAsync(connection, "dividends")).ShouldBe(
        [
            "id", "account_id", "holding_id", "instrument_id", "isin", "trading_symbol", "pay_date", "amount", "per_share", "note", "source", "created_by", "created_at", "voided_at", "voided_by",
            "batch_id", "external_id", "quantity", "scrip_name",
        ]);
        (await ColumnsAsync(connection, "dividend_suggestions")).ShouldBe(["id", "account_id", "state", "dividend_id", "decided_by", "decided_at"]);
    }

    public static TheoryData<string, string, bool> BadRows => new()
    {
        { DividendSql(amount: "0"), "ck_dividends_amount", false },
        { DividendSql(amount: "-5"), "ck_dividends_amount", false },
        { DividendSql(perShare: "0"), "ck_dividends_per_share", false },
        { DividendSql(source: "'import'"), "ck_dividends_source", false },
        { DividendSql(voidedAt: "now()"), "ck_dividends_voided", false },
        { DividendSql(voidedBy: "'o'"), "ck_dividends_voided", false },
        { DecisionSql("'maybe'", "NULL"), "ck_dividend_suggestions_state", true },
        { DecisionSql("'accepted'", "NULL"), "ck_dividend_suggestions_dividend", true },
        { DecisionSql("'dismissed'", $"'{Dividend}'"), "ck_dividend_suggestions_dividend", true },
    };

    [Theory]
    [MemberData(nameof(BadRows))]
    public async Task The_database_refuses_a_row_the_rules_never_produce(string sql, string constraint, bool dividendExists)
    {
        await using var connection = await PreparedAsync();
        if (dividendExists)
        {
            await AppRoleProbe.ExecuteAsync(connection, DividendSql(), Ct);
        }

        var refused = await Should.ThrowAsync<PostgresException>(() => AppRoleProbe.ExecuteAsync(connection, sql, Ct));

        (refused.SqlState, refused.ConstraintName).ShouldBe((PostgresErrorCodes.CheckViolation, constraint));
    }

    [Fact]
    public async Task A_ledger_entry_has_one_decision_and_it_must_exist()
    {
        await using var connection = await PreparedAsync();
        await AppRoleProbe.ExecuteAsync(connection, DividendSql(), Ct);
        await AppRoleProbe.ExecuteAsync(connection, DecisionSql("'accepted'", $"'{Dividend}'"), Ct);

        var twice = await Should.ThrowAsync<PostgresException>(() => AppRoleProbe.ExecuteAsync(connection, DecisionSql("'dismissed'", "NULL"), Ct));
        var unknown = await Should.ThrowAsync<PostgresException>(() => AppRoleProbe.ExecuteAsync(connection, DecisionSql("'dismissed'", "NULL").Replace(Entry, Guid.NewGuid().ToString(), StringComparison.Ordinal), Ct));

        (twice.SqlState, twice.ConstraintName).ShouldBe((PostgresErrorCodes.UniqueViolation, "pk_dividend_suggestions"));
        (unknown.SqlState, unknown.ConstraintName).ShouldBe((PostgresErrorCodes.ForeignKeyViolation, "fk_dividend_suggestions_ledger_entries_id"));
    }

    [Fact]
    public async Task The_app_role_can_read_insert_and_update_dividends_and_decisions_but_never_delete_them()
    {
        await AppRoleProbe.RunAsync(
            factory,
            async connection =>
            {
                await AppRoleProbe.ExecuteAsync(connection, SetupSql, Ct);
                await AppRoleProbe.ExecuteAsync(connection, DividendSql(), Ct);
                await AppRoleProbe.ExecuteAsync(connection, "UPDATE portfolio.dividends SET voided_at = now(), voided_by = 'o'", Ct);
                await AppRoleProbe.ExecuteAsync(connection, DecisionSql("'accepted'", $"'{Dividend}'"), Ct);
                await AppRoleProbe.ExecuteAsync(connection, "UPDATE portfolio.dividend_suggestions SET decided_by = 'p'", Ct);
                (await AppRoleProbe.ScalarAsync(connection, "SELECT count(*) FROM portfolio.dividends", Ct)).ShouldBe(1L);

                var dividends = await Should.ThrowAsync<PostgresException>(() => AppRoleProbe.ExecuteAsync(connection, "DELETE FROM portfolio.dividends", Ct));
                var decisions = await Should.ThrowAsync<PostgresException>(() => AppRoleProbe.ExecuteAsync(connection, "DELETE FROM portfolio.dividend_suggestions", Ct));
                (dividends.SqlState, decisions.SqlState).ShouldBe((PostgresErrorCodes.InsufficientPrivilege, PostgresErrorCodes.InsufficientPrivilege));
                foreach (var table in Tables)
                {
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
    public async Task A_database_that_already_holds_a_ledger_migrates_and_keeps_it_and_rolling_back_drops_only_the_new_tables()
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
            await AppRoleProbe.ExecuteAsync(connection, SetupSql, Ct);

            await migrator.MigrateAsync(null, Ct);

            (await AppRoleProbe.ScalarAsync(connection, "SELECT count(*) FROM portfolio.ledger_entries WHERE class = 'dividend'", Ct)).ShouldBe(1L);
            (await AppRoleProbe.ScalarAsync(connection, "SELECT count(*) FROM portfolio.dividends", Ct)).ShouldBe(0L);
            (await AppRoleProbe.ScalarAsync(connection, "SELECT count(*) FROM portfolio.dividend_suggestions", Ct)).ShouldBe(0L);

            await migrator.MigrateAsync(BeforeMigration, Ct);

            (await AppRoleProbe.ScalarAsync(connection, "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'portfolio' AND table_name IN ('dividends', 'dividend_suggestions')", Ct)).ShouldBe(0L);
            (await AppRoleProbe.ScalarAsync(connection, "SELECT count(*) FROM portfolio.ledger_entries", Ct)).ShouldBe(1L);
        }
        finally
        {
            await using var connection = new NpgsqlConnection(admin.ConnectionString);
            await connection.OpenAsync(Ct);
            await AppRoleProbe.ExecuteAsync(connection, $"DROP DATABASE IF EXISTS {MigrationDatabase} WITH (FORCE)", Ct);
        }
    }
}
