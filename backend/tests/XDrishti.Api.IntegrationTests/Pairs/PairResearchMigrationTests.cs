using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.IntegrationTests.Research;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Api.IntegrationTests.Pairs;

/// <summary>P14-13 criterion 8: the migration <c>AddPairResearch</c> applies on a store built up to the migration before it, creates the two tables with the stated columns and keys, agrees with the model snapshot and is reversible (<c>Down</c> drops both and nothing else).</summary>
[Collection(ApiTestSuite.Name)]
public sealed class PairResearchMigrationTests(ApiFactory factory)
{
    private const string Before = "20261103120000_AddDailyEod";

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
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_pair_research_probe", Ct, migrate: false);
        using var scope = scratch.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>();
        var migrator = db.GetService<IMigrator>();
        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);
        await migrator.MigrateAsync(Before, Ct);
        (await ListAsync(connection, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'research' AND table_name LIKE 'pair_%'")).ShouldBeEmpty();

        await migrator.MigrateAsync(null, Ct);

        (await ListAsync(connection, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'research' AND table_name LIKE 'pair_%' ORDER BY 1")).ShouldBe(["pair_formations", "pair_trades"]);
        (await ListAsync(connection, Columns, ("t", "pair_formations"))).Order(StringComparer.Ordinal).ShouldBe(
            new[]
            {
                C("formation_id", "character", 64), C("family", "character varying", 32), C("quarter", "character varying", 8), C("window_from", "date"), C("window_to", "date"), C("candidates", "integer"), C("tested", "integer"), C("unavailable", "integer"),
                C("selected", "integer"), C("detail", "jsonb"), C("created_at", "timestamp with time zone"),
            }.Order(StringComparer.Ordinal));
        (await ListAsync(connection, Columns, ("t", "pair_trades"))).Order(StringComparer.Ordinal).ShouldBe(
            new[]
            {
                C("run_id", "character", 64), C("pair_key", "character varying", 200), C("ordinal", "integer"), C("entry_ts", "timestamp with time zone"), C("exit_ts", "timestamp with time zone"), C("side", "character varying", 12), C("z_entry", "double precision"),
                C("z_exit", "double precision", nullable: true), C("exit_reason", "character varying", 16), C("qty_y", "integer"), C("qty_x", "integer"), C("gross_inr", "numeric"), C("net_inr", "numeric", nullable: true), C("r", "double precision"), C("labels", "ARRAY"),
            }.Order(StringComparer.Ordinal));
        (await ListAsync(connection, "SELECT pg_get_constraintdef(oid) FROM pg_constraint WHERE conname = 'pk_pair_trades'")).Single().ShouldBe("PRIMARY KEY (run_id, pair_key, ordinal)");
        (await ListAsync(connection, "SELECT pg_get_constraintdef(oid) FROM pg_constraint WHERE conname = 'pk_pair_formations'")).Single().ShouldBe("PRIMARY KEY (formation_id)");
        db.Database.HasPendingModelChanges().ShouldBeFalse("the model snapshot agrees with the model");

        await migrator.MigrateAsync(Before, Ct);

        (await ListAsync(connection, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'research' AND table_name LIKE 'pair_%'")).ShouldBeEmpty();
        (await ListAsync(connection, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'research' AND table_name = 'extension_evaluations'")).ShouldBe(["extension_evaluations"], "Down removes the two tables and nothing else");

        await migrator.MigrateAsync(null, Ct);

        (await ListAsync(connection, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'research' AND table_name LIKE 'pair_%' ORDER BY 1")).ShouldBe(["pair_formations", "pair_trades"]);
    }

    [Fact]
    public async Task The_checks_refuse_a_bad_id_a_count_that_does_not_add_up_a_non_finite_number_a_zero_quantity_and_a_wrong_side_or_reason()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_pair_research_checks_probe", Ct);
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

        string Formation(string formation, int candidates, int tested, int unavailable, int selected) =>
            $"INSERT INTO research.pair_formations (formation_id, family, quarter, window_from, window_to, candidates, tested, unavailable, selected, detail) VALUES ('{formation}', 'pairs:intraday-1', '2026Q4', '2025-09-01', '2026-09-30', {candidates}, {tested}, {unavailable}, {selected}, '{{}}')";

        string Trade(string run, int ordinal = 1, string side = "short_spread", string reason = "exit", int qty = 10, string r = "0.5", string net = "NULL") =>
            $"INSERT INTO research.pair_trades VALUES ('{run}', 'NSE:A|NSE:B', {ordinal}, '2026-10-01 05:30+00', '2026-10-01 05:40+00', '{side}', 2.2, NULL, '{reason}', {qty}, {qty}, 743.25, {net}, {r}, ARRAY['gross'])";

        (await StateAsync(Formation(new string('A', 64), 3, 3, 0, 1))).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await StateAsync(Formation(id, 3, 2, 0, 1))).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await StateAsync(Formation(id, 3, 3, 0, 4))).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await StateAsync(Formation(id, 3, 2, 1, 1))).ShouldBeNull();
        (await StateAsync(Formation(id, 3, 2, 1, 1))).ShouldBe(PostgresErrorCodes.UniqueViolation);
        (await StateAsync(Trade(new string('B', 64)))).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await StateAsync(Trade(id, ordinal: 0))).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await StateAsync(Trade(id, side: "sideways"))).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await StateAsync(Trade(id, reason: "hope"))).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await StateAsync(Trade(id, qty: 0))).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await StateAsync(Trade(id, r: "'NaN'"))).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await StateAsync(Trade(id, r: "'Infinity'"))).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await StateAsync(Trade(id))).ShouldBeNull();
        (await StateAsync(Trade(id))).ShouldBe(PostgresErrorCodes.UniqueViolation);
        (await StateAsync(Trade(id, ordinal: 2, net: "700.10"))).ShouldBeNull();
    }
}
