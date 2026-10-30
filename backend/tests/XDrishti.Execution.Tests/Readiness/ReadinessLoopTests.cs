using Microsoft.Extensions.Time.Testing;
using XDrishti.Execution.Application;
using XDrishti.Execution.Application.Halts;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Application.Readiness;
using XDrishti.Execution.Domain.Readiness;

namespace XDrishti.Execution.Tests.Readiness;

/// <summary>P12-11 criteria 1, 4, 6 and 8 on fakes with a fake clock: when it reads, when it stores, the 09:10:30 mark (late and unavailable when the service was not running), the halts that clear on ready and the rejections that only an attestation lifts.</summary>
public sealed class ReadinessLoopTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly FakeTimeProvider _clock = new(Ist.At(27, 7, 0));
    private readonly MemReadiness _world = new MemReadiness().WithAttestation();
    private readonly InMemoryHaltStore _halts = new();

    private ReadinessLoop Loop() => new(_world, _world, _world, _world, _halts, _world, _clock, new HaltAutoClearService(_halts, _clock));

    private Task<int> RunAt(ReadinessLoop loop, DateTimeOffset at, bool force = false)
    {
        _clock.SetUtcNow(at);
        return loop.RunOnceAsync(force, Ct);
    }

    [Fact]
    public async Task A_good_morning_stores_one_ready_row_after_exactly_one_profile_read_and_one_ip_read()
    {
        var loop = Loop();

        await RunAt(loop, Ist.At(27, 9, 0));

        _world.Checks.Single().Verdict.ShouldBe(ReadinessState.Ready);
        (_world.ProfileReads, _world.IpReads).ShouldBe((1, 1));
    }

    [Fact]
    public async Task An_unchanged_verdict_is_stored_again_only_after_15_minutes_and_a_change_is_stored_at_once()
    {
        var loop = Loop();
        await RunAt(loop, Ist.At(27, 9, 0));

        await RunAt(loop, Ist.At(27, 9, 5)); // the interval is 300 s: probed, unchanged, not stored
        await RunAt(loop, Ist.At(27, 9, 10));
        _world.Checks.Count.ShouldBe(1);
        await RunAt(loop, Ist.At(27, 9, 15));
        _world.Checks.Count.ShouldBe(2);

        _world.Connection = new("Disconnected", Ist.At(27, 17, 30));
        await RunAt(loop, Ist.At(27, 9, 20));

        _world.Checks.Count.ShouldBe(3);
        _world.Checks[^1].Verdict.ShouldBe(ReadinessState.NotReady);
    }

    [Theory]
    [InlineData(7, 59)]
    [InlineData(16, 31)]
    [InlineData(23, 0)]
    public async Task Outside_08_00_to_16_30_nothing_is_read_and_no_row_is_stored(int hour, int minute)
    {
        var loop = Loop();

        await RunAt(loop, Ist.At(27, hour, minute), force: true);

        (_world.ProfileReads, _world.IpReads, _world.Checks.Count).ShouldBe((0, 0, 0));
    }

    [Fact]
    public async Task On_a_closed_day_nothing_is_read()
    {
        _world.TradingDay = false;

        await RunAt(Loop(), Ist.At(27, 9, 0), force: true);

        (_world.ProfileReads, _world.Checks.Count, _world.Days.Count).ShouldBe((0, 0, 0));
    }

    [Fact]
    public async Task An_account_that_is_not_at_assisted_is_not_read()
    {
        _world.Assisted.Clear();

        await RunAt(Loop(), Ist.At(27, 9, 0), force: true);

        _world.ProfileReads.ShouldBe(0);
    }

    [Fact]
    public async Task From_09_00_to_09_15_a_not_ready_account_is_probed_every_30_seconds_and_a_ready_one_every_300()
    {
        _world.Connection = new("Disconnected", Ist.At(27, 17, 30));
        var loop = Loop();
        await RunAt(loop, Ist.At(27, 9, 0));

        await RunAt(loop, Ist.At(27, 9, 0, 29));
        _world.ProfileReads.ShouldBe(1);
        await RunAt(loop, Ist.At(27, 9, 0, 30));
        _world.ProfileReads.ShouldBe(2);

        _world.Connection = new("Connected", Ist.At(27, 17, 30));
        await RunAt(loop, Ist.At(27, 9, 1)); // ready now
        await RunAt(loop, Ist.At(27, 9, 1, 30));
        _world.ProfileReads.ShouldBe(3);
    }

    [Fact]
    public async Task The_09_10_30_mark_stores_the_probed_state_once_and_a_later_ready_check_leaves_it_not_ready()
    {
        _world.Connection = new("Disconnected", Ist.At(27, 17, 30));
        var loop = Loop();
        await RunAt(loop, Ist.At(27, 9, 10));

        await RunAt(loop, Ist.At(27, 9, 10, 30));
        _world.Connection = new("Connected", Ist.At(27, 17, 30));
        await RunAt(loop, Ist.At(27, 9, 10, 31), force: true);
        await RunAt(loop, Ist.At(27, 9, 11), force: true);

        var mark = _world.Days.Single();
        (mark.State, mark.Late, mark.SessionDate).ShouldBe((ReadinessState.NotReady, false, new DateOnly(2026, 10, 27)));
        mark.CheckId.ShouldNotBeNull();
        _world.Checks[^1].Verdict.ShouldBe(ReadinessState.Ready);
    }

    [Fact]
    public async Task A_ready_morning_is_marked_ready_at_09_10_30()
    {
        await RunAt(Loop(), Ist.At(27, 9, 10, 30));

        _world.Days.Single().State.ShouldBe(ReadinessState.Ready);
    }

    [Theory]
    [InlineData(9, 10, 36)]
    [InlineData(9, 40, 0)]
    [InlineData(20, 0, 0)]
    public async Task If_the_service_was_not_running_at_09_10_30_the_first_run_afterwards_stores_a_late_unavailable_mark_never_ready(int hour, int minute, int second)
    {
        await RunAt(Loop(), Ist.At(27, hour, minute, second));

        var mark = _world.Days.Single();
        (mark.State, mark.Late).ShouldBe((ReadinessState.Unavailable, true));
    }

    [Fact]
    public async Task The_mark_is_written_once_per_account_and_session_and_a_new_day_gets_its_own()
    {
        var loop = Loop();
        await RunAt(loop, Ist.At(27, 9, 10, 30));
        await RunAt(loop, Ist.At(27, 9, 10, 31));
        await RunAt(loop, Ist.At(27, 9, 12));

        _world.Days.Count.ShouldBe(1);
        _world.Connection = new("Connected", Ist.At(28, 17, 30));
        _world.Profile = BrokerRead.Ok(new ProfileFacts(Ist.At(28, 17, 30)));
        await RunAt(loop, Ist.At(28, 9, 10, 30));

        _world.Days.Select(d => d.SessionDate).ShouldBe([new DateOnly(2026, 10, 27), new DateOnly(2026, 10, 28)]);
    }

    [Fact]
    public async Task Unreadable_settings_give_an_unavailable_row_and_no_broker_read()
    {
        _world.Settings = null;

        await RunAt(Loop(), Ist.At(27, 9, 0));

        _world.Checks.Single().Verdict.ShouldBe(ReadinessState.Unavailable);
        _world.ProfileReads.ShouldBe(0);
    }

    [Fact]
    public async Task A_failing_probe_is_unavailable_and_stored_not_lost_and_not_ready()
    {
        _world.BrokerThrows = true;

        await RunAt(Loop(), Ist.At(27, 9, 0));

        _world.Checks.Single().Verdict.ShouldBe(ReadinessState.Unavailable);
    }

    [Fact]
    public async Task The_closed_broker_facts_default_makes_every_verdict_unavailable()
    {
        var loop = new ReadinessLoop(_world, _world, _world, new ClosedBrokerFacts(), _halts, _world, _clock);

        await RunAt(loop, Ist.At(27, 9, 0));

        _world.Checks.Single().Verdict.ShouldBe(ReadinessState.Unavailable);
    }

    [Fact]
    public async Task A_ready_verdict_clears_the_token_and_ip_halts_of_its_account_and_touches_no_other()
    {
        var other = Guid.NewGuid();
        var token = (await _halts.AppendAsync(new(HaltScope.Account, MemReadiness.Account, HaltLevel.Entries, HaltTriggers.TokenInvalid, "system", "system:halts", "dh-901", _clock.GetUtcNow()), Ct)).Halt;
        await _halts.AppendAsync(new(HaltScope.Account, other, HaltLevel.Entries, HaltTriggers.TokenInvalid, "system", "system:halts", "other account", _clock.GetUtcNow()), Ct);
        _halts.Release((await _halts.AppendAsync(new(HaltScope.Account, MemReadiness.Account, HaltLevel.Entries, HaltTriggers.OwnerButton, "owner", "owner:x@example.test", "owner halt", _clock.GetUtcNow()), Ct)).Halt.Id);

        await RunAt(Loop(), Ist.At(27, 9, 0));

        _halts.AutoClears.Select(c => c.HaltId).ShouldBe([token.Id]);
    }

    [Fact]
    public async Task A_ready_verdict_does_not_clear_an_ip_not_allowed_halt_when_the_attestation_is_older_than_the_halt()
    {
        var halt = (await _halts.AppendAsync(new(HaltScope.Account, MemReadiness.Account, HaltLevel.Entries, HaltTriggers.IpNotAllowed, "system", "system:halts", "dh-905", _clock.GetUtcNow()), Ct)).Halt;

        await RunAt(Loop(), Ist.At(27, 9, 0)); // the only attestation was made on the 26th, before the halt

        _world.Checks.Single().Verdict.ShouldBe(ReadinessState.Ready);
        _halts.AutoClears.ShouldBeEmpty();
        (await _halts.ActiveAsync(Ct)).Select(h => h.Id).ShouldContain(halt.Id);
    }

    [Fact]
    public async Task A_ready_verdict_clears_an_ip_not_allowed_halt_when_an_allowing_attestation_is_newer_than_the_halt()
    {
        var halt = (await _halts.AppendAsync(new(HaltScope.Account, MemReadiness.Account, HaltLevel.Entries, HaltTriggers.IpNotAllowed, "system", "system:halts", "dh-905", _clock.GetUtcNow()), Ct)).Halt;
        _world.Attestations.Add(new(MemReadiness.Account, MemReadiness.Ip, true, Ist.At(27, 8, 0)));

        await RunAt(Loop(), Ist.At(27, 9, 0));

        _world.Checks.Single().Verdict.ShouldBe(ReadinessState.Ready);
        _halts.AutoClears.Select(c => c.HaltId).ShouldBe([halt.Id]);
    }

    [Fact]
    public async Task A_ready_verdict_clears_a_token_invalid_halt_even_though_the_attestation_is_older_than_it()
    {
        var halt = (await _halts.AppendAsync(new(HaltScope.Account, MemReadiness.Account, HaltLevel.Entries, HaltTriggers.TokenInvalid, "system", "system:halts", "dh-901", _clock.GetUtcNow()), Ct)).Halt;

        await RunAt(Loop(), Ist.At(27, 9, 0)); // attestation (26th 20:00) predates the halt

        _halts.AutoClears.Select(c => c.HaltId).ShouldBe([halt.Id]);
    }

    [Fact]
    public async Task A_halt_of_another_kind_makes_the_account_not_ready_and_is_not_touched()
    {
        var halt = (await _halts.AppendAsync(new(HaltScope.Account, MemReadiness.Account, HaltLevel.Entries, HaltTriggers.DailyLoss, "system", "system:halts", "loss", _clock.GetUtcNow()), Ct)).Halt;

        await RunAt(Loop(), Ist.At(27, 9, 0));

        _world.Checks.Single().Verdict.ShouldBe(ReadinessState.NotReady);
        _halts.AutoClears.ShouldBeEmpty();
        (await _halts.ActiveAsync(Ct)).Select(h => h.Id).ShouldContain(halt.Id);
    }

    [Fact]
    public async Task An_unreadable_halt_record_clears_nothing_and_is_not_ready()
    {
        await _halts.AppendAsync(new(HaltScope.Account, MemReadiness.Account, HaltLevel.Entries, HaltTriggers.TokenInvalid, "system", "system:halts", "dh-901", _clock.GetUtcNow()), Ct);
        _halts.Unreadable = true;

        await RunAt(Loop(), Ist.At(27, 9, 0));

        _world.Checks.Single().Verdict.ShouldBe(ReadinessState.Unavailable);
        _halts.AutoClears.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_dh_905_rejection_is_not_ready_at_once_and_only_a_newer_attestation_lifts_it()
    {
        var loop = Loop();
        await RunAt(loop, Ist.At(27, 9, 0));
        _clock.SetUtcNow(Ist.At(27, 9, 2));

        (await loop.NoteRejectionAsync(MemReadiness.Account, "ip_not_allowed", Ct)).ShouldBeTrue();

        _world.Checks[^1].Verdict.ShouldBe(ReadinessState.NotReady);
        _world.Checks[^1].Reasons.ShouldContain(r => r.Code == "ip_rejected");

        await RunAt(loop, Ist.At(27, 12, 0)); // hours later, everything else fine: still rejected
        _world.Checks[^1].Verdict.ShouldBe(ReadinessState.NotReady);
        _world.Checks[^1].Reasons.ShouldContain(r => r.Code == "ip_rejected");

        _world.Attestations.Add(new(MemReadiness.Account, MemReadiness.Ip, true, Ist.At(27, 12, 30)));
        await RunAt(loop, Ist.At(27, 13, 0));

        _world.Checks[^1].Verdict.ShouldBe(ReadinessState.Ready);
    }

    [Fact]
    public async Task A_dh_901_rejection_lifts_with_the_next_good_profile_read()
    {
        var loop = Loop();
        await RunAt(loop, Ist.At(27, 9, 0));
        _clock.SetUtcNow(Ist.At(27, 9, 1));
        await loop.NoteRejectionAsync(MemReadiness.Account, "token_invalid", Ct);

        _world.Checks[^1].Reasons.ShouldContain(r => r.Code == "token_rejected");
        await RunAt(loop, Ist.At(27, 9, 1, 40));

        _world.Checks[^1].Verdict.ShouldBe(ReadinessState.Ready);
    }

    [Fact]
    public async Task Any_other_broker_reason_is_ignored()
    {
        (await Loop().NoteRejectionAsync(MemReadiness.Account, "input_exception", Ct)).ShouldBeFalse();

        _world.Checks.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_owners_check_now_command_wakes_the_loop_and_a_system_actor_is_refused()
    {
        var signal = new ReadinessSignal();
        var commands = new Halts.MemoryCommands();
        commands.Add(new(Guid.NewGuid(), "readiness_now", null, "{}", "owner:hem@example.test", DateTimeOffset.UnixEpoch));
        commands.Add(new(Guid.NewGuid(), "readiness_now", null, "{}", "system:other", DateTimeOffset.UnixEpoch));

        await new ExecCommandProcessor(commands, readiness: signal).RunOnceAsync(Ct);

        commands.Answers.ShouldBe([("done", "execution.readiness_requested"), ("refused", "execution.readiness_invalid")]);
        signal.Consume().ShouldBeTrue();
        signal.Consume().ShouldBeFalse();
        (await signal.WaitAsync(TimeSpan.FromMilliseconds(1), Ct)).ShouldBeTrue();
    }

    [Fact]
    public async Task Without_the_signal_the_command_is_an_unknown_kind()
    {
        var commands = new Halts.MemoryCommands();
        commands.Add(new(Guid.NewGuid(), "readiness_now", null, "{}", "owner:hem@example.test", DateTimeOffset.UnixEpoch));

        await new ExecCommandProcessor(commands).RunOnceAsync(Ct);

        commands.Answers.ShouldBe([("refused", "execution.command_unknown")]);
    }

    [Fact]
    public async Task The_json_of_the_reasons_round_trips_and_holds_no_address()
    {
        await RunAt(Loop(), Ist.At(27, 9, 0));

        var json = ReadinessJson.Write(_world.Checks.Single().Reasons);

        json.ShouldNotContain(MemReadiness.Ip);
        ReadinessJson.Read(json).Select(r => (r.Code, r.Outcome)).ShouldBe(_world.Checks.Single().Reasons.Select(r => (r.Code, r.Outcome)));
    }
}
