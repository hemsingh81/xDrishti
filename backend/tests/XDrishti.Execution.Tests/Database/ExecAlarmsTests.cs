using Npgsql;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain;
using XDrishti.Execution.Domain.Protection;
using XDrishti.Execution.Persistence;

namespace XDrishti.Execution.Tests.Database;

/// <summary>P12-04 criterion 11: the insert-only alarm record (raise, clear), its constraints and grants, and the store as the role xd_exec.</summary>
[Collection(ExecDatabaseSuite.Name)]
public sealed class ExecAlarmsTests(ExecDatabase db)
{
    private static readonly DateTimeOffset At = new(2026, 10, 29, 4, 0, 0, TimeSpan.Zero);
    private static long _next = 19000;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<long> NewIntentAsync(NpgsqlDataSource data)
    {
        var id = Interlocked.Increment(ref _next);
        var intent = ExecIntent.Create(id, Guid.NewGuid(), db.AccountId, id, ExecSide.Buy, "TCS", "11536", 10, 100m, 98m, 104m, ExecStructure.Super).Value;
        (await new PgExecEventStore(data).StartAsync(intent, new DateOnly(2026, 10, 29), ExecEnvironment.Sandbox, "system:test", Ct)).IsSuccess.ShouldBeTrue();
        return id;
    }

    private async Task<long> CountAsync(string sql) => Convert.ToInt64(await ExecDatabase.ScalarAsync(db.Owner, sql), System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public async Task A_raise_is_one_row_a_repeat_while_open_adds_nothing_and_a_clear_reopens_the_door()
    {
        await using var data = NpgsqlDataSource.Create(db.Exec);
        var intent = await NewIntentAsync(data);
        var store = new PgAlarmStore(data);
        var draft = new AlarmDraft(AlarmCodes.ProtectionBreach, db.AccountId, intent, "4 of 10 shares without a stop", At);

        (await store.RaiseAsync(draft, Ct)).ShouldBeTrue();
        (await store.RaiseAsync(draft, Ct)).ShouldBeFalse();
        (await store.RaiseAsync(draft with { Code = AlarmCodes.Oversold }, Ct)).ShouldBeTrue("another code is another alarm");
        (await CountAsync($"SELECT count(*) FROM execution.alarms WHERE intent_id = {intent}")).ShouldBe(2);
        (await CountAsync($"SELECT count(*) FROM execution.alarms WHERE intent_id = {intent} AND code = 'protection_breach' AND severity = 'critical'")).ShouldBe(1);

        (await store.ClearAsync(AlarmCodes.ProtectionBreach, db.AccountId, intent, "system:protection", AlarmClearHow.Auto, At.AddSeconds(3), Ct)).ShouldBe(1);
        (await store.ClearAsync(AlarmCodes.ProtectionBreach, db.AccountId, intent, "system:protection", AlarmClearHow.Auto, At.AddSeconds(4), Ct)).ShouldBe(0, "nothing open left");
        (await CountAsync($"SELECT count(*) FROM execution.alarm_clears c JOIN execution.alarms a ON a.id = c.alarm_id WHERE a.intent_id = {intent} AND c.how = 'auto' AND c.cleared_by = 'system:protection'")).ShouldBe(1);
        (await store.RaiseAsync(draft, Ct)).ShouldBeTrue("a cleared alarm can be raised again");
    }

    [Fact]
    public async Task An_alarm_about_no_intent_dedupes_on_its_account_and_code_and_null_is_the_same_as_null()
    {
        await using var data = NpgsqlDataSource.Create(db.Exec);
        var store = new PgAlarmStore(data);
        var draft = new AlarmDraft(AlarmCodes.ProtectionUnknown, null, null, "no_snapshot", At);

        (await store.RaiseAsync(draft, Ct)).ShouldBeTrue();
        (await store.RaiseAsync(draft, Ct)).ShouldBeFalse();
        (await store.ClearAsync(AlarmCodes.ProtectionUnknown, null, null, "owner:hem", AlarmClearHow.Owner, At, Ct)).ShouldBe(1);
        (await CountAsync("SELECT count(*) FROM execution.alarm_clears WHERE how = 'owner' AND cleared_by = 'owner:hem'")).ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task A_code_outside_the_twelve_is_refused_by_the_store_and_by_the_database()
    {
        await using var data = NpgsqlDataSource.Create(db.Exec);

        await Should.ThrowAsync<ArgumentException>(() => new PgAlarmStore(data).RaiseAsync(new AlarmDraft("made_up", null, null, "x", At), Ct));
        (await ExecDatabase.SqlStateAsync(db.Exec, "INSERT INTO execution.alarms (at, code, severity, detail) VALUES (now(), 'made_up', 'info', 'x')")).ShouldBe("23514");
        (await ExecDatabase.SqlStateAsync(db.Exec, "INSERT INTO execution.alarms (at, code, severity, detail) VALUES (now(), 'oversold', 'loud', 'x')")).ShouldBe("23514");
        (await ExecDatabase.SqlStateAsync(db.Exec, "INSERT INTO execution.alarms (at, code, severity, intent_id, detail) VALUES (now(), 'oversold', 'critical', 999999999, 'x')")).ShouldBe("23503");
    }

    [Fact]
    public async Task The_alarm_record_is_insert_only_for_everybody_including_the_owner_and_a_clear_has_a_valid_how_and_an_alarm()
    {
        await using var data = NpgsqlDataSource.Create(db.Exec);
        await new PgAlarmStore(data).RaiseAsync(new AlarmDraft(AlarmCodes.ExitFailed, null, null, "x", At), Ct);

        foreach (var connection in new[] { db.Owner, db.Exec })
        {
            (await ExecDatabase.SqlStateAsync(connection, "UPDATE execution.alarms SET detail = 'y'")).ShouldNotBeNull();
            (await ExecDatabase.SqlStateAsync(connection, "DELETE FROM execution.alarms")).ShouldNotBeNull();
            (await ExecDatabase.SqlStateAsync(connection, "TRUNCATE execution.alarms")).ShouldNotBeNull();
        }

        (await ExecDatabase.SqlStateAsync(db.Owner, "UPDATE execution.alarms SET detail = 'y'")).ShouldBe("23514", "the trigger refuses even the owner");
        await new PgAlarmStore(data).ClearAsync(AlarmCodes.ExitFailed, null, null, "system:protection", AlarmClearHow.Auto, At, Ct);
        (await ExecDatabase.SqlStateAsync(db.Owner, "UPDATE execution.alarm_clears SET how = 'owner'")).ShouldBe("23514");
        (await ExecDatabase.SqlStateAsync(db.Owner, "DELETE FROM execution.alarm_clears")).ShouldBe("23514");
        (await ExecDatabase.SqlStateAsync(db.Owner, "TRUNCATE execution.alarms CASCADE")).ShouldBe("23514");
        (await ExecDatabase.SqlStateAsync(db.Exec, "INSERT INTO execution.alarm_clears (alarm_id, at, cleared_by, how) VALUES (-1, now(), 'x', 'auto')")).ShouldBe("23503");
        (await ExecDatabase.SqlStateAsync(db.Exec, "INSERT INTO execution.alarm_clears (alarm_id, at, cleared_by, how) SELECT id, now(), 'x', 'never' FROM execution.alarms LIMIT 1")).ShouldBe("23514");
    }

    [Fact]
    public async Task The_runtime_role_reads_the_alarms_and_writes_none()
    {
        (await ExecDatabase.SqlStateAsync(db.App, "SELECT count(*) FROM execution.alarms")).ShouldBeNull();
        (await ExecDatabase.SqlStateAsync(db.App, "SELECT count(*) FROM execution.alarm_clears")).ShouldBeNull();
        (await ExecDatabase.SqlStateAsync(db.App, "INSERT INTO execution.alarms (at, code, severity, detail) VALUES (now(), 'oversold', 'critical', 'x')")).ShouldBe("42501");
        (await ExecDatabase.SqlStateAsync(db.App, "INSERT INTO execution.alarm_clears (alarm_id, at, cleared_by, how) VALUES (1, now(), 'x', 'auto')")).ShouldBe("42501");
        (await ExecDatabase.SqlStateAsync(db.App, "UPDATE execution.alarms SET detail = 'y'")).ShouldBe("42501");
        (await ExecDatabase.SqlStateAsync(db.App, "DELETE FROM execution.alarms")).ShouldBe("42501");
        (await ExecDatabase.SqlStateAsync(db.App, "TRUNCATE execution.alarms")).ShouldBe("42501");
    }
}
