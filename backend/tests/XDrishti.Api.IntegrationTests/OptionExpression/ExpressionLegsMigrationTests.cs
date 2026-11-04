using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.IntegrationTests.Research;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Api.IntegrationTests.OptionExpression;

/// <summary>
/// P14-10 criterion 7: the migration <c>AddExpressionLegs</c> creates <c>research.expression_legs</c> with the stated columns and key, agrees with the snapshot, widens the four checks of the trade rows only as a structure needs (three fates, the condor side, a zero exit value, a condor with no cash twin) and nothing more, and is reversible
/// (<c>Down</c> drops the legs, deletes nothing and puts the original checks back for every later row).
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class ExpressionLegsMigrationTests(ApiFactory factory)
{
    private const string Trades = "20261103183027_AddExpressionTrades";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<string?> StateAsync(NpgsqlConnection connection, string sql)
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

    private static async Task<List<string>> ListAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        var rows = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(Ct);
        while (await reader.ReadAsync(Ct))
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }

    private static readonly string Run = new('a', 64);

    private static string Header(string run) =>
        $"INSERT INTO research.expression_runs (run_id, cash_run_key, profile_hash, remark_version, inputs, trades, paired, unpaired, bs_fallback_share, labels) VALUES ('{run}', '{new string('c', 64)}', '{new string('d', 64)}', 'expr-1', '{{}}', 9, 4, '{{}}', 0, ARRAY['x'])";

    private static string Trade(string key, string fate = "paired", string side = "CE", string exit = "3000", string cash = "2", string labels = "ARRAY['structure: debit_spread']", string r = "1.2") =>
        fate == "paired"
            ? $"INSERT INTO research.expression_trades VALUES ('{Run}', '{key}', '{fate}', '{side}', 'W', 1, '2025-06-12', 'derived', 2200000, '2025-06-10 03:50+00', '2025-06-10 04:30+00', 6000, {exit}, 10.5, 100.00, NULL, {r}, {cash}, 'target', 75, 'sizable', {labels})"
            : $"INSERT INTO research.expression_trades VALUES ('{Run}', '{key}', '{fate}', '{side}', NULL, NULL, NULL, 'none', NULL, '2025-06-10 03:50+00', '2025-06-10 04:30+00', NULL, NULL, NULL, NULL, NULL, NULL, NULL, 'target', NULL, 'n/a', ARRAY[]::text[])";

    private static string Leg(string key, int leg = 0, string side = "CE", string position = "long", string exit = "100", string entry = "200") =>
        $"INSERT INTO research.expression_legs VALUES ('{Run}', '{key}', {leg}, '{side}', '{position}', 2200000, {entry}, {exit}, ARRAY[]::text[])";

    [Fact]
    public async Task The_legs_table_has_the_stated_columns_the_checks_widen_only_as_a_structure_needs_and_Down_restores_the_originals()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_expression_legs_probe", Ct, migrate: false);
        using var scope = scratch.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>();
        var migrator = db.GetService<IMigrator>();
        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);
        await migrator.MigrateAsync(Trades, Ct);
        (await ListAsync(connection, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'research' AND table_name = 'expression_legs'")).ShouldBeEmpty();

        await migrator.MigrateAsync(null, Ct);

        (await ListAsync(connection, "SELECT column_name || '|' || data_type || '|' || coalesce(character_maximum_length::text, '') || '|' || is_nullable FROM information_schema.columns WHERE table_schema = 'research' AND table_name = 'expression_legs' ORDER BY 1")).ShouldBe(
            ["entry_premium_px|integer||NO", "exit_premium_px|integer||NO", "labels|ARRAY||NO", "leg|smallint||NO", "position|character|5|NO", "run_id|character|64|NO", "side|character|2|NO", "strike_px|integer||NO", "trade_key|character varying|200|NO"]);
        (await ListAsync(connection, "SELECT pg_get_constraintdef(oid) FROM pg_constraint WHERE conname = 'pk_expression_legs'")).Single().ShouldBe("PRIMARY KEY (run_id, trade_key, leg)");
        db.Database.HasPendingModelChanges().ShouldBeFalse("the model snapshot agrees with the model");

        (await StateAsync(connection, Header(Run))).ShouldBeNull();
        (await StateAsync(connection, Trade("debit", exit: "0"))).ShouldBeNull();
        (await StateAsync(connection, Trade("condor", side: "IC", cash: "NULL", labels: "ARRAY['no_cash_twin']"))).ShouldBeNull();
        (await StateAsync(connection, Trade("nolabel", side: "IC", cash: "NULL"))).ShouldBe(PostgresErrorCodes.CheckViolation, "a paired row with no cash R must say it has no cash twin");
        (await StateAsync(connection, Trade("n1", fate: "not_range"))).ShouldBeNull();
        (await StateAsync(connection, Trade("n2", fate: "structure_invalid"))).ShouldBeNull();
        (await StateAsync(connection, Trade("n3", fate: "lot_unknown"))).ShouldBeNull();
        (await StateAsync(connection, Trade("n5", fate: "structure_inconsistent"))).ShouldBeNull();
        (await StateAsync(connection, Trade("n4", fate: "wished"))).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await StateAsync(connection, Trade("bad", side: "XX"))).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await StateAsync(connection, Trade("neg", exit: "-1"))).ShouldBe(PostgresErrorCodes.CheckViolation, "an exit value is never negative");
        (await StateAsync(connection, Trade("nan", r: "'NaN'"))).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await StateAsync(connection, Leg("debit", 0))).ShouldBeNull();
        (await StateAsync(connection, Leg("debit", 1, position: "short", exit: "0"))).ShouldBeNull();
        (await StateAsync(connection, Leg("debit", 0))).ShouldBe(PostgresErrorCodes.UniqueViolation);
        (await StateAsync(connection, Leg("debit", 4))).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await StateAsync(connection, Leg("debit", 2, side: "IC"))).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await StateAsync(connection, Leg("debit", 2, position: "flat"))).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await StateAsync(connection, Leg("debit", 2, entry: "0"))).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await StateAsync(connection, Leg("debit", 2, exit: "-1"))).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await StateAsync(connection, Leg("ghost", 0))).ShouldBe(PostgresErrorCodes.ForeignKeyViolation, "a leg belongs to a trade row of the run");

        await migrator.MigrateAsync(Trades, Ct);

        (await ListAsync(connection, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'research' AND table_name = 'expression_legs'")).ShouldBeEmpty();
        (await ListAsync(connection, "SELECT count(*)::text FROM research.expression_trades")).Single().ShouldBe("6", "no stored row is deleted or rewritten by Down");
        (await StateAsync(connection, Trade("again", side: "IC", cash: "NULL", labels: "ARRAY['no_cash_twin']"))).ShouldBe(PostgresErrorCodes.CheckViolation, "the original checks are back");
        (await StateAsync(connection, Trade("zero", exit: "0"))).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await StateAsync(connection, Trade("fate", fate: "not_range"))).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await StateAsync(connection, Trade("ok"))).ShouldBeNull();
    }
}
