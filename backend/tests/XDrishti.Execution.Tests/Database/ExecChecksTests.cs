using Npgsql;
using XDrishti.Execution.Domain;
using XDrishti.Execution.Domain.Checks;
using XDrishti.Execution.Persistence;
using XDrishti.Execution.Tests.Checks;

namespace XDrishti.Execution.Tests.Database;

/// <summary>P12-03: the insert-only check record (runs, results, confirmations), its constraints and grants, and the store as the role xd_exec.</summary>
[Collection(ExecDatabaseSuite.Name)]
public sealed class ExecChecksTests(ExecDatabase db)
{
    private static readonly DateOnly Day = new(2026, 10, 29);
    private static long _next = 9000;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<long> NewIntentAsync(NpgsqlDataSource data)
    {
        var id = Interlocked.Increment(ref _next);
        var intent = ExecIntent.Create(id, Guid.NewGuid(), db.AccountId, id, ExecSide.Buy, "TCS", "11536", 10, 100m, 98m, 104m, ExecStructure.Super).Value;
        (await new PgExecEventStore(data).StartAsync(intent, Day, ExecEnvironment.Sandbox, "system:test", Ct)).IsSuccess.ShouldBeTrue();
        return id;
    }

    private async Task<long> CountAsync(string sql) => Convert.ToInt64(await ExecDatabase.ScalarAsync(db.Owner, sql), System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public async Task A_run_is_stored_with_all_its_results_in_order_and_read_back_as_the_latest_full_run()
    {
        await using var data = NpgsqlDataSource.Create(db.Exec);
        var intent = await NewIntentAsync(data);
        var store = new PgCheckStore(data);
        var full = Facts.Good().Run();

        var id = await store.AppendRunAsync(intent, full, Ct);
        await store.AppendRunAsync(intent, Facts.Good().Run(CheckRunKind.Fast), Ct);

        (await CountAsync($"SELECT count(*) FROM execution.check_results WHERE run_id = {id}")).ShouldBe(34);
        (await CountAsync($"SELECT count(*) FROM execution.check_results WHERE run_id = {id} AND ord = 0 AND code = 'gate_open' AND class = 'hard' AND outcome = 'pass'")).ShouldBe(1);
        (await CountAsync($"SELECT count(*) FROM execution.check_results WHERE run_id = {id} AND ord = 33 AND code = 'data_quality' AND class = 'soft'")).ShouldBe(1);
        var latest = await store.LatestFullRunAsync(intent, Ct);
        latest.ShouldNotBeNull();
        (latest.Id, latest.Verdict, latest.ValidUntil).ShouldBe((id, CheckVerdict.Cleared, full.ValidUntil));
        (await store.LatestFullRunAsync(intent + 100000, Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task A_rejected_run_with_non_finite_numbers_is_stored_and_reads_back_rejected()
    {
        await using var data = NpgsqlDataSource.Create(db.Exec);
        var intent = await NewIntentAsync(data);
        var f = Facts.Good();
        var bad = (f with { I = f.I with { Entry = double.NaN } }).Run();

        await new PgCheckStore(data).AppendRunAsync(intent, bad, Ct);

        (await new PgCheckStore(data).LatestFullRunAsync(intent, Ct))!.Verdict.ShouldBe(CheckVerdict.Rejected);
        (await CountAsync($"SELECT count(*) FROM execution.check_results r JOIN execution.check_runs c ON c.id = r.run_id WHERE c.intent_id = {intent} AND r.outcome = 'unavailable'")).ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task A_confirmation_is_stored_with_a_run_and_read_back_and_the_table_refuses_anything_that_is_not_the_owner()
    {
        await using var data = NpgsqlDataSource.Create(db.Exec);
        var intent = await NewIntentAsync(data);
        var store = new PgCheckStore(data);
        var f = Facts.Good();
        var ban = f with { M = f.M with { InFnoBan = true } };
        var confirmation = new CheckConfirmation("owner:hem", "reviewed the ban", [CheckCodes.FnoBan], Facts.Now);

        var runId = await store.AppendRunAsync(intent, ban.Run(CheckRunKind.Full, confirmation), Ct);

        var read = (await store.LatestConfirmationAsync(intent, Ct))!;
        (read.Actor, read.Reason).ShouldBe((confirmation.Actor, confirmation.Reason));
        read.Codes.ShouldBe([CheckCodes.FnoBan]);
        var sqlBase = $"INSERT INTO execution.check_confirmations (run_id, actor, reason, codes, at) VALUES ({runId}, ";
        (await ExecDatabase.SqlStateAsync(db.Exec, sqlBase + "'owner:hem', 'again', ARRAY['fno_ban'], now())")).ShouldBe("23505");
        var second = await store.AppendRunAsync(intent, Facts.Good().Run(), Ct);
        var insert = $"INSERT INTO execution.check_confirmations (run_id, actor, reason, codes, at) VALUES ({second}, ";
        (await ExecDatabase.SqlStateAsync(db.Exec, insert + "'auto:rule', 'x', ARRAY['fno_ban'], now())")).ShouldBe("23514");
        (await ExecDatabase.SqlStateAsync(db.Exec, insert + "'owner:', 'x', ARRAY['fno_ban'], now())")).ShouldBe("23514");
        (await ExecDatabase.SqlStateAsync(db.Exec, insert + "'owner:hem', '  ', ARRAY['fno_ban'], now())")).ShouldBe("23514");
        (await ExecDatabase.SqlStateAsync(db.Exec, insert + "'owner:hem', 'x', ARRAY[]::text[], now())")).ShouldBe("23514");
        (await ExecDatabase.SqlStateAsync(db.Exec, insert + "'owner:hem', 'x', ARRAY['fno_ban'], now())")).ShouldBeNull();
    }

    [Fact]
    public async Task The_shape_of_a_run_row_and_of_a_result_row_is_enforced()
    {
        await using var data = NpgsqlDataSource.Create(db.Exec);
        var intent = await NewIntentAsync(data);
        const string Hash = "0123456789abcdef";
        string Run(string kind, string intentId, string sheet, string verdict = "rejected", string hash = Hash) =>
            $"INSERT INTO execution.check_runs (kind, intent_id, sheet_id, at, verdict, settings_hash, inputs_hash, valid_until) VALUES ('{kind}', {intentId}, {sheet}, now(), '{verdict}', '{hash}', '{Hash}', now())";

        (await ExecDatabase.SqlStateAsync(db.Exec, Run("full", intent.ToString(System.Globalization.CultureInfo.InvariantCulture), "NULL"))).ShouldBeNull();
        (await ExecDatabase.SqlStateAsync(db.Exec, Run("full", "NULL", "NULL"))).ShouldBe("23514");
        (await ExecDatabase.SqlStateAsync(db.Exec, Run("full", intent.ToString(System.Globalization.CultureInfo.InvariantCulture), "gen_random_uuid()"))).ShouldBe("23514");
        (await ExecDatabase.SqlStateAsync(db.Exec, Run("advisory", intent.ToString(System.Globalization.CultureInfo.InvariantCulture), "NULL"))).ShouldBe("23514");
        (await ExecDatabase.SqlStateAsync(db.Exec, Run("advisory", "NULL", "gen_random_uuid()"))).ShouldBeNull();
        (await ExecDatabase.SqlStateAsync(db.Exec, Run("other", intent.ToString(System.Globalization.CultureInfo.InvariantCulture), "NULL"))).ShouldBe("23514");
        (await ExecDatabase.SqlStateAsync(db.Exec, Run("full", intent.ToString(System.Globalization.CultureInfo.InvariantCulture), "NULL", "maybe"))).ShouldBe("23514");
        (await ExecDatabase.SqlStateAsync(db.Exec, Run("full", intent.ToString(System.Globalization.CultureInfo.InvariantCulture), "NULL", hash: "NOT HEX"))).ShouldBe("23514");

        var runId = await new PgCheckStore(data).AppendRunAsync(intent, Facts.Good().Run(), Ct);
        string Result(string code, string outcome, string cls = "hard") =>
            $"INSERT INTO execution.check_results (run_id, ord, code, class, outcome, detail, source) VALUES ({runId}, 100, '{code}', '{cls}', '{outcome}', 'd', 's')";
        (await ExecDatabase.SqlStateAsync(db.Exec, Result("Bad Code", "pass"))).ShouldBe("23514");
        (await ExecDatabase.SqlStateAsync(db.Exec, Result("gate_open", "maybe"))).ShouldBe("23514");
        (await ExecDatabase.SqlStateAsync(db.Exec, Result("gate_open", "pass", "mid"))).ShouldBe("23514");
        (await ExecDatabase.SqlStateAsync(db.Exec, Result("gate_open", "unavailable"))).ShouldBeNull();
    }

    [Theory]
    [InlineData("check_runs", "UPDATE execution.check_runs SET verdict = 'cleared'")]
    [InlineData("check_runs", "DELETE FROM execution.check_runs")]
    [InlineData("check_runs", "TRUNCATE execution.check_runs")]
    [InlineData("check_results", "UPDATE execution.check_results SET outcome = 'pass'")]
    [InlineData("check_results", "DELETE FROM execution.check_results")]
    [InlineData("check_results", "TRUNCATE execution.check_results")]
    [InlineData("check_confirmations", "UPDATE execution.check_confirmations SET reason = 'x'")]
    [InlineData("check_confirmations", "DELETE FROM execution.check_confirmations")]
    [InlineData("check_confirmations", "TRUNCATE execution.check_confirmations")]
    public async Task The_record_is_insert_only_for_the_owner_the_execution_role_and_the_api_role(string table, string statement)
    {
        await using var data = NpgsqlDataSource.Create(db.Exec);
        var intent = await NewIntentAsync(data);
        var f = Facts.Good();
        await new PgCheckStore(data).AppendRunAsync(intent, (f with { M = f.M with { InFnoBan = true } }).Run(CheckRunKind.Full, new CheckConfirmation("owner:hem", "ok", [CheckCodes.FnoBan], Facts.Now)), Ct);

        // A table that other tables reference refuses TRUNCATE first on that ground (0A000): the refusal is the same.
        (await ExecDatabase.SqlStateAsync(db.Owner, statement)).ShouldBeOneOf(["23514", "0A000"], table + " owner");
        (await ExecDatabase.SqlStateAsync(db.Owner, statement.Replace("TRUNCATE ", "TRUNCATE ONLY ", StringComparison.Ordinal))).ShouldBeOneOf(["23514", "0A000"], table + " owner");
        (await ExecDatabase.SqlStateAsync(db.Exec, statement)).ShouldBe("42501", table + " xd_exec");
        (await ExecDatabase.SqlStateAsync(db.App, statement)).ShouldBe("42501", table + " xd_app");
        (await ExecDatabase.SqlStateAsync(db.App, $"SELECT count(*) FROM execution.{table}")).ShouldBeNull();
        (await ExecDatabase.SqlStateAsync(db.App, $"INSERT INTO execution.{table} DEFAULT VALUES")).ShouldBe("42501");
    }
}
