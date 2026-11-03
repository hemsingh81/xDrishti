using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.IntegrationTests.Research;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Api.IntegrationTests.OptionExpression;

/// <summary>P14-08 criterion 11: the migration <c>AddExpressionTrades</c> applies on a store built up to the migration before it, creates the two tables with the stated columns and keys, agrees with the model snapshot, refuses what the checks refuse and is reversible (<c>Down</c> drops both and nothing else).</summary>
[Collection(ApiTestSuite.Name)]
public sealed class ExpressionMigrationTests(ApiFactory factory)
{
    private const string Before = "20261103130000_AddPairResearch";

    private const string Columns = "SELECT column_name || '|' || data_type || '|' || coalesce(character_maximum_length::text, '') || '|' || is_nullable FROM information_schema.columns WHERE table_schema = 'research' AND table_name = @t";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string C(string name, string type, int? length = null, bool nullable = false) => $"{name}|{type}|{length}|{(nullable ? "YES" : "NO")}";

    private static async Task<List<string>> ListAsync(NpgsqlConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        var rows = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(Ct);
        while (await reader.ReadAsync(Ct))
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }

    [Fact]
    public async Task The_migration_creates_the_two_tables_with_the_stated_columns_and_keys_agrees_with_the_snapshot_and_Down_removes_only_them()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_expression_probe", Ct, migrate: false);
        using var scope = scratch.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>();
        var migrator = db.GetService<IMigrator>();
        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);
        await migrator.MigrateAsync(Before, Ct);
        (await ListAsync(connection, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'research' AND table_name LIKE 'expression_%'")).ShouldBeEmpty();

        await migrator.MigrateAsync(null, Ct);

        (await ListAsync(connection, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'research' AND table_name LIKE 'expression_%' ORDER BY 1")).ShouldBe(["expression_runs", "expression_trades"]);
        (await ListAsync(connection, Columns, ("t", "expression_runs"))).Order(StringComparer.Ordinal).ShouldBe(
            new[]
            {
                C("run_id", "character", 64), C("cash_run_key", "character", 64), C("profile_hash", "character", 64), C("remark_version", "character varying", 16), C("inputs", "jsonb"), C("trades", "integer"), C("paired", "integer"), C("unpaired", "jsonb"),
                C("bs_fallback_share", "double precision", nullable: true), C("labels", "ARRAY"), C("created_at", "timestamp with time zone"),
            }.Order(StringComparer.Ordinal));
        (await ListAsync(connection, Columns, ("t", "expression_trades"))).Order(StringComparer.Ordinal).ShouldBe(
            new[]
            {
                C("run_id", "character", 64), C("trade_key", "character varying", 200), C("fate", "character varying", 24), C("side", "character", 2), C("expiry_flag", "character", 1, nullable: true), C("expiry_code", "smallint", nullable: true), C("expiry", "date", nullable: true),
                C("expiry_source", "character varying", 8), C("strike_px", "integer", nullable: true), C("entry_ts", "timestamp with time zone"), C("exit_ts", "timestamp with time zone"), C("entry_premium_px", "integer", nullable: true), C("exit_premium_px", "integer", nullable: true),
                C("units", "double precision", nullable: true), C("gross_inr", "numeric", nullable: true), C("net_inr", "numeric", nullable: true), C("r_equiv", "double precision", nullable: true), C("cash_r", "double precision", nullable: true), C("exit_reason", "character varying", 24),
                C("lot", "integer", nullable: true), C("sizable", "character varying", 24), C("labels", "ARRAY"),
            }.Order(StringComparer.Ordinal));
        (await ListAsync(connection, "SELECT pg_get_constraintdef(oid) FROM pg_constraint WHERE conname = 'pk_expression_trades'")).Single().ShouldBe("PRIMARY KEY (run_id, trade_key)");
        (await ListAsync(connection, "SELECT pg_get_constraintdef(oid) FROM pg_constraint WHERE conname = 'pk_expression_runs'")).Single().ShouldBe("PRIMARY KEY (run_id)");
        db.Database.HasPendingModelChanges().ShouldBeFalse("the model snapshot agrees with the model");

        await migrator.MigrateAsync(Before, Ct);

        (await ListAsync(connection, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'research' AND table_name LIKE 'expression_%'")).ShouldBeEmpty();
        (await ListAsync(connection, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'research' AND table_name = 'pair_trades'")).ShouldBe(["pair_trades"], "Down removes the two tables and nothing else");

        await migrator.MigrateAsync(null, Ct);

        (await ListAsync(connection, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'research' AND table_name LIKE 'expression_%' ORDER BY 1")).ShouldBe(["expression_runs", "expression_trades"]);
    }

    [Fact]
    public async Task The_checks_make_paired_hold_exactly_when_the_premiums_and_the_r_are_there_and_refuse_a_bad_id_fate_side_and_number()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_expression_checks_probe", Ct);
        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);
        var id = new string('a', 64);

        async Task<string?> StateAsync(string sql)
        {
            try
            {
                await using var command = new NpgsqlCommand(sql, connection);
                await command.ExecuteNonQueryAsync(Ct);
                return null;
            }
            catch (PostgresException failure)
            {
                return failure.SqlState;
            }
        }

        string Run(string run, int trades = 2, int paired = 1, string share = "0.5") =>
            $"INSERT INTO research.expression_runs (run_id, cash_run_key, profile_hash, remark_version, inputs, trades, paired, unpaired, bs_fallback_share, labels) VALUES ('{run}', '{new string('c', 64)}', '{new string('d', 64)}', 'expr-1', '{{}}', {trades}, {paired}, '{{}}', {share}, ARRAY['x'])";

        string Paired(string key, string fate = "paired", string side = "CE", string premium = "6000", string units = "10.5", string r = "1.2", string cash = "2") =>
            $"INSERT INTO research.expression_trades VALUES ('{id}', '{key}', '{fate}', '{side}', 'W', 1, '2025-06-12', 'derived', 2200000, '2025-06-10 03:50+00', '2025-06-10 04:30+00', {premium}, 6500, {units}, 100.00, NULL, {r}, {cash}, 'target', 75, 'sizable', ARRAY['basis: gross'])";

        string Unpaired(string key, string fate = "no_series", string extra = "NULL") =>
            $"INSERT INTO research.expression_trades VALUES ('{id}', '{key}', '{fate}', 'CE', NULL, NULL, NULL, 'none', NULL, '2025-06-10 03:50+00', '2025-06-10 04:30+00', NULL, NULL, NULL, NULL, NULL, {extra}, NULL, 'target', NULL, 'n/a', ARRAY[]::text[])";

        (await StateAsync(Run(new string('A', 64)))).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await StateAsync(Run(id, trades: 2, paired: 3))).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await StateAsync(Run(id, share: "NULL"))).ShouldBe(PostgresErrorCodes.CheckViolation, "a share is there exactly when something paired");
        (await StateAsync(Run(id, share: "1.5"))).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await StateAsync(Run(id))).ShouldBeNull();
        (await StateAsync(Run(id))).ShouldBe(PostgresErrorCodes.UniqueViolation);
        (await StateAsync(Paired("a", fate: "wished"))).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await StateAsync(Paired("b", side: "XX"))).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await StateAsync(Paired("c", premium: "0"))).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await StateAsync(Paired("d", units: "0"))).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await StateAsync(Paired("e", r: "'NaN'"))).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await StateAsync(Paired("f", r: "'Infinity'"))).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await StateAsync(Paired("g", r: "NULL"))).ShouldBe(PostgresErrorCodes.CheckViolation, "paired needs the R");
        (await StateAsync(Paired("h", cash: "NULL"))).ShouldBe(PostgresErrorCodes.CheckViolation, "paired needs the cash R");
        (await StateAsync(Paired("i", fate: "no_series"))).ShouldBe(PostgresErrorCodes.CheckViolation, "an unpaired fate carries no premium and no R");
        (await StateAsync(Unpaired("j", fate: "paired"))).ShouldBe(PostgresErrorCodes.CheckViolation, "paired needs its figures");
        (await StateAsync(Unpaired("k", extra: "1.0"))).ShouldBe(PostgresErrorCodes.CheckViolation, "an unpaired row carries no R");
        (await StateAsync(Paired("l"))).ShouldBeNull();
        (await StateAsync(Paired("l"))).ShouldBe(PostgresErrorCodes.UniqueViolation);
        (await StateAsync(Unpaired("m", fate: "spread_unavailable"))).ShouldBeNull();
        (await StateAsync(Unpaired("n", fate: "risk_invalid"))).ShouldBeNull();
        (await StateAsync(Unpaired("o", fate: "open_at_end"))).ShouldBeNull();
    }
}
