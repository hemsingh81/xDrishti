using Microsoft.Extensions.Time.Testing;
using XDrishti.Execution.Application;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain;
using XDrishti.Execution.Domain.Checks;

namespace XDrishti.Execution.Tests.Checks;

public sealed class ChecklistPreSendCheckTests
{
    private static readonly Guid Account = Guid.NewGuid();
    private static readonly DateOnly Day = new(2026, 10, 29);

    private static BrokerLeg Leg(LegKind kind) =>
        new(ClientOrderId.Create(Day, 361, kind, 1).Value.ToString(), kind, kind == LegKind.Entry ? ExecSide.Buy : ExecSide.Sell, "11536", 10, 1500m);

    private sealed class Loader(Func<CheckFacts?> facts) : ICheckFactsLoader
    {
        public int Calls { get; private set; }

        public Task<CheckFacts?> LoadAsync(Guid accountId, long intentId, BrokerLeg leg, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(facts());
        }
    }

    private sealed class Store : ICheckStore
    {
        public List<(long Intent, CheckRun Run)> Runs { get; } = [];

        public CheckConfirmation? Confirmation { get; set; }

        public bool Fail { get; set; }

        public Task<long> AppendRunAsync(long intentId, CheckRun run, CancellationToken cancellationToken)
        {
            if (Fail)
            {
                throw new InvalidOperationException("db down");
            }

            Runs.Add((intentId, run));
            return Task.FromResult((long)Runs.Count);
        }

        public Task<StoredRun?> LatestFullRunAsync(long intentId, CancellationToken cancellationToken)
        {
            var last = Runs.LastOrDefault(r => r.Intent == intentId && r.Run.Kind == CheckRunKind.Full);
            return Task.FromResult<StoredRun?>(last.Run is null ? null : new StoredRun(1, CheckRunKind.Full, last.Run.At, last.Run.ValidUntil, last.Run.Verdict));
        }

        public Task AppendConfirmationAsync(long runId, CheckConfirmation confirmation, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<CheckConfirmation?> LatestConfirmationAsync(long intentId, CancellationToken cancellationToken) => Task.FromResult(Confirmation);
    }

    private sealed class ExitPolicy(bool open) : IExitSendPolicy
    {
        public int Calls { get; private set; }

        public Task<GateDecision> CheckAsync(Guid accountId, long intentId, BrokerLeg leg, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(open ? GateDecision.Allowed : new GateDecision(false, "no_exit_policy"));
        }
    }

    private static (ChecklistPreSendCheck Check, Store Store, Loader Loader, ExitPolicy Exits, FakeTimeProvider Clock, Func<Facts, Facts> Set) Make(bool exitOpen = false)
    {
        var current = Facts.Good();
        var clock = new FakeTimeProvider(Facts.Now);
        var store = new Store();
        var loader = new Loader(() => RefreshStamps(current, clock.GetUtcNow()).ToLoaded());
        var exits = new ExitPolicy(exitOpen);
        return (new ChecklistPreSendCheck(loader, store, exits, clock), store, loader, exits, clock, f => current = f);
    }

    // The loader reads "now": every stamp is as fresh as the clock, so only the defect under test matters.
    private static Facts RefreshStamps(Facts f, DateTimeOffset now) => f with
    {
        M = f.M with { Quote = f.M.Quote is null ? null : new Stamped<QuoteFacts>(f.M.Quote.Value, now) },
        S = f.S with
        {
            KillSwitchOff = f.S.KillSwitchOff is null ? null : new Stamped<bool>(f.S.KillSwitchOff.Value, now),
            TokenExpiry = f.S.TokenExpiry is null ? null : new Stamped<DateTimeOffset>(now.AddHours(2), now),
            StaticIpAllowed = f.S.StaticIpAllowed is null ? null : new Stamped<bool>(f.S.StaticIpAllowed.Value, now),
            StreamConnected = f.S.StreamConnected is null ? null : new Stamped<bool>(f.S.StreamConnected.Value, now),
        },
        A = f.A with
        {
            FundsAvailable = f.A.FundsAvailable is null ? null : new Stamped<double>(f.A.FundsAvailable.Value, now),
            Margin = f.A.Margin is null ? null : new Stamped<MarginFacts>(f.A.Margin.Value, now),
            Book = f.A.Book is null ? null : new Stamped<BookFacts>(f.A.Book.Value, now),
            TodayPnl = f.A.TodayPnl is null ? null : new Stamped<double>(f.A.TodayPnl.Value, now),
        },
    };

    [Fact]
    public async Task With_no_facts_source_every_entry_is_refused_and_nothing_is_stored()
    {
        var store = new Store();
        var check = new ChecklistPreSendCheck(new ClosedCheckFactsLoader(), store, new ClosedExitSendPolicy(), TimeProvider.System);

        var decision = await check.CheckAsync(Account, 361, Leg(LegKind.Entry), CancellationToken.None);

        decision.ShouldBe(new GateDecision(false, "check_facts_unavailable"));
        store.Runs.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_full_run_clears_and_is_stored_then_a_fast_run_follows_within_validity_and_a_full_run_after_it()
    {
        var (check, store, _, _, clock, _) = Make();

        (await check.CheckAsync(Account, 361, Leg(LegKind.Entry), CancellationToken.None)).Open.ShouldBeTrue();
        store.Runs.Select(r => r.Run.Kind).ShouldBe([CheckRunKind.Full]);
        store.Runs[0].Run.Results.Count.ShouldBe(34);

        clock.Advance(TimeSpan.FromSeconds(5));
        (await check.CheckAsync(Account, 361, Leg(LegKind.Entry), CancellationToken.None)).Open.ShouldBeTrue();
        store.Runs[1].Run.Kind.ShouldBe(CheckRunKind.Fast);
        store.Runs[1].Run.Results.Count.ShouldBe(7);

        clock.Advance(TimeSpan.FromSeconds(6));
        (await check.CheckAsync(Account, 361, Leg(LegKind.Entry), CancellationToken.None)).Open.ShouldBeTrue();
        store.Runs[2].Run.Kind.ShouldBe(CheckRunKind.Full);
    }

    [Fact]
    public async Task A_halt_that_appears_after_a_clear_full_run_aborts_the_send_with_the_failing_code()
    {
        var (check, store, _, _, clock, set) = Make();
        (await check.CheckAsync(Account, 361, Leg(LegKind.Entry), CancellationToken.None)).Open.ShouldBeTrue();

        clock.Advance(TimeSpan.FromSeconds(2));
        set(Facts.Good() with { S = Facts.Good().S with { ExecutionHalt = HaltReading.Halted } });
        var decision = await check.CheckAsync(Account, 361, Leg(LegKind.Entry), CancellationToken.None);

        decision.ShouldBe(new GateDecision(false, "check_failed:no_halt"));
        store.Runs[^1].Run.Kind.ShouldBe(CheckRunKind.Fast);
        store.Runs[^1].Run.Verdict.ShouldBe(CheckVerdict.Rejected);
    }

    [Fact]
    public async Task A_full_run_that_fails_is_stored_and_refuses_and_a_soft_failure_needs_the_exact_confirmation()
    {
        var (check, store, _, _, _, set) = Make();
        var ban = Facts.Good() with { M = Facts.Good().M with { InFnoBan = true } };
        set(ban);

        (await check.CheckAsync(Account, 361, Leg(LegKind.Entry), CancellationToken.None)).Reason.ShouldBe("check_failed:fno_ban");
        store.Runs.Count.ShouldBe(1);

        store.Confirmation = new CheckConfirmation("owner:hem", "reviewed", [CheckCodes.FnoBan], Facts.Now);
        (await check.CheckAsync(Account, 361, Leg(LegKind.Entry), CancellationToken.None)).Open.ShouldBeTrue();
        store.Runs[^1].Run.Verdict.ShouldBe(CheckVerdict.ClearedByConfirmation);

        store.Confirmation = store.Confirmation with { Codes = [CheckCodes.Surveillance] };
        store.Runs.Clear();
        (await check.CheckAsync(Account, 361, Leg(LegKind.Entry), CancellationToken.None)).Open.ShouldBeFalse();
    }

    [Fact]
    public async Task A_store_that_cannot_write_or_a_loader_that_throws_is_a_refusal()
    {
        var (check, store, _, _, _, _) = Make();
        store.Fail = true;
        (await check.CheckAsync(Account, 361, Leg(LegKind.Entry), CancellationToken.None)).ShouldBe(new GateDecision(false, "check_store_unavailable"));

        var throwing = new ChecklistPreSendCheck(new Loader(() => throw new InvalidOperationException("boom")), new Store(), new ClosedExitSendPolicy(), TimeProvider.System);
        (await throwing.CheckAsync(Account, 361, Leg(LegKind.Entry), CancellationToken.None)).ShouldBe(new GateDecision(false, "check_store_unavailable"));
    }

    [Fact]
    public async Task Limits_that_cannot_be_vouched_for_are_a_refusal()
    {
        var f = Facts.Good().ToLoaded() with { Limits = null };
        var check = new ChecklistPreSendCheck(new Loader(() => f), new Store(), new ClosedExitSendPolicy(), TimeProvider.System);

        (await check.CheckAsync(Account, 361, Leg(LegKind.Entry), CancellationToken.None)).Reason.ShouldBe("check_limits_unavailable");
    }

    [Theory]
    [InlineData(LegKind.Stop)]
    [InlineData(LegKind.Target)]
    [InlineData(LegKind.Exit)]
    [InlineData(LegKind.Flatten)]
    public async Task Exits_never_reach_the_entry_checklist_and_a_halted_over_limit_out_of_window_account_can_still_exit(LegKind kind)
    {
        var (check, store, loader, exits, _, set) = Make(exitOpen: true);
        var f = Facts.Good();
        set(f with { S = f.S with { ExecutionHalt = HaltReading.Halted }, M = f.M with { TradingDay = false }, A = f.A with { EntriesToday = 99, TodayPnl = new Stamped<double>(-9999, Facts.Now) } });

        (await check.CheckAsync(Account, 361, Leg(kind), CancellationToken.None)).Open.ShouldBeTrue();

        loader.Calls.ShouldBe(0);
        store.Runs.ShouldBeEmpty();
        exits.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task The_default_exit_policy_is_closed_so_nothing_but_an_entry_can_pass_yet()
    {
        var (check, _, _, _, _, _) = Make(exitOpen: false);

        (await check.CheckAsync(Account, 361, Leg(LegKind.Stop), CancellationToken.None)).ShouldBe(new GateDecision(false, "no_exit_policy"));
    }

    private sealed class Journal : ISendJournal
    {
        public int Begins { get; private set; }

        public Task BeginAsync(Guid accountId, long intentId, BrokerLeg leg, CancellationToken cancellationToken)
        {
            Begins++;
            return Task.CompletedTask;
        }

        public Task DroppedAsync(BrokerLeg leg, string reason, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task OutcomeAsync(BrokerLeg leg, BrokerAck ack, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class Modes : IModeSource
    {
        public Task<ModeAnswer?> AnswerAsync(Guid accountId, CancellationToken cancellationToken) => Task.FromResult<ModeAnswer?>(new ModeAnswer(ExecMode.SemiAuto, true));
    }

    private sealed class Halts : IHaltSource
    {
        public Task<HaltReading> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(HaltReading.Clear);
    }

    [Fact]
    public async Task Behind_the_sender_a_halt_seen_by_the_checklist_means_no_record_and_no_request()
    {
        var (check, store, _, _, clock, set) = Make();
        var f = Facts.Good();
        set(f with { S = f.S with { LiveHalt = HaltReading.Halted } });
        var broker = new FakeBroker();
        var journal = new Journal();
        var sender = new LegSender(broker, new Modes(), new Halts(), check, journal, new ExecRateLimiter(clock, 2, 30, 200), ExecEnvironment.Sandbox, clock, TimeSpan.FromSeconds(5));

        var result = await sender.SendAsync(new SendRequest(Account, 361, Leg(LegKind.Entry), clock.GetUtcNow()), CancellationToken.None);

        (result.Status, result.Reason).ShouldBe((SendStatus.Refused, "check_failed:no_halt"));
        broker.Calls.ShouldBe(0);
        journal.Begins.ShouldBe(0);
        store.Runs.Count.ShouldBe(1);
    }
}
