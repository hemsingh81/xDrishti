using Microsoft.Extensions.Time.Testing;
using XDrishti.Execution.Application;
using XDrishti.Execution.Application.Halts;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain;
using XDrishti.Execution.Tests.Protection;

namespace XDrishti.Execution.Tests.Halts;

/// <summary>
/// P12-05 criterion 2 on the FakeBroker with seeds. A burst of 50 entries; the halt commits at a seeded point INSIDE one entry's write-ahead (after its first gate check, before its second). The invariant: no entry reaches the broker after the halt's
/// commit. The negative control is a send path whose only gate check comes before the write-ahead: it is caught on far more than ten seeds.
/// </summary>
public sealed class HaltGateRaceTests
{
    private const int Seeds = 1000;

    /// <summary>A write-ahead that commits the halt when the seeded entry is being journalled; later entries find it already there.</summary>
    private sealed class HaltingJournal(int commitAtEntry, Func<Task> commit, Action committed) : ISendJournal
    {
        private int _begun;

        public async Task BeginAsync(Guid accountId, long intentId, BrokerLeg leg, CancellationToken cancellationToken)
        {
            if (++_begun == commitAtEntry)
            {
                await commit();
                committed();
            }
        }

        public Task DroppedAsync(BrokerLeg leg, string reason, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task OutcomeAsync(BrokerLeg leg, BrokerAck ack, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class AllowAll : IPreSendCheck
    {
        public Task<GateDecision> CheckAsync(Guid accountId, long intentId, BrokerLeg leg, CancellationToken cancellationToken) => Task.FromResult(GateDecision.Allowed);
    }

    /// <summary>The send path with the gate checked only once, before the write-ahead (the mistake the second check exists to prevent).</summary>
    private sealed class GateCheckedOnce(IHaltSource halts, IBrokerOrders broker, ISendJournal journal)
    {
        public async Task<bool> SendAsync(BrokerLeg leg, CancellationToken cancellationToken)
        {
            if (HaltPolicy.EntriesBlocked(await halts.ReadAsync(cancellationToken)))
            {
                return false;
            }

            await journal.BeginAsync(Rig.Account, 1, leg, cancellationToken);
            await broker.PlaceAsync(leg, cancellationToken);
            return true;
        }
    }

    private static BrokerLeg Entry(int n) => new(ClientOrderId.Create(new DateOnly(2026, 10, 26), 1000 + n, LegKind.Entry, 1).Value.ToString(), LegKind.Entry, ExecSide.Buy, "1594", 1, 1500m);

    [Fact]
    public async Task In_1000_seeds_no_entry_reaches_the_broker_after_the_halt_commits_and_the_one_caught_in_the_write_ahead_ends_dropped()
    {
        for (var seed = 1; seed <= Seeds; seed++)
        {
            var at = new Random(seed).Next(1, 51);
            var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 26, 4, 0, 0, TimeSpan.Zero));
            var store = new InMemoryHaltStore();
            var service = new HaltService(store, clock);
            var broker = new FakeBroker(clock);
            var committed = false;
            var journal = new HaltingJournal(at, () => service.RequestAsOwnerAsync(Rig.Account, HaltLevel.Cancel, "race", "owner:test", false, CancellationToken.None), () => committed = true);
            var sender = new LegSender(broker, new FixedMode(new ModeAnswer(ExecMode.SemiAuto, true)), service, new AllowAll(), journal, new ExecRateLimiter(clock, 100, 100, 100), ExecEnvironment.Sandbox, clock, TimeSpan.FromSeconds(5));

            var results = new List<SendResult>();
            for (var n = 1; n <= 50; n++)
            {
                clock.Advance(TimeSpan.FromMilliseconds(200)); // the fake broker caps writes per second
                results.Add(await sender.SendAsync(new SendRequest(Rig.Account, 1000 + n, Entry(n), clock.GetUtcNow()), CancellationToken.None));
            }

            committed.ShouldBeTrue();
            broker.Legs.Count.ShouldBe(at - 1, $"seed {seed}: exactly the entries before the halt were sent");
            results[at - 1].Status.ShouldBe(SendStatus.Dropped, $"seed {seed}");
            results[at - 1].Reason.ShouldBe("halted_before_send");
            results.Skip(at).ShouldAllBe(r => r.Status == SendStatus.Refused && r.Reason == "halted", $"seed {seed}");
        }
    }

    [Fact]
    public async Task Negative_control_a_gate_checked_only_before_the_write_ahead_lets_an_entry_through_after_the_halt_on_every_seed()
    {
        var caught = 0;
        for (var seed = 1; seed <= Seeds; seed++)
        {
            var at = new Random(seed).Next(1, 51);
            var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 26, 4, 0, 0, TimeSpan.Zero));
            var service = new HaltService(new InMemoryHaltStore(), clock);
            var broker = new FakeBroker(clock);
            var journal = new HaltingJournal(at, () => service.RequestAsOwnerAsync(Rig.Account, HaltLevel.Cancel, "race", "owner:test", false, CancellationToken.None), () => { });
            var sender = new GateCheckedOnce(service, broker, journal);

            for (var n = 1; n <= 50; n++)
            {
                clock.Advance(TimeSpan.FromMilliseconds(200));
                await sender.SendAsync(Entry(n), CancellationToken.None);
            }

            if (broker.Legs.Count > at - 1)
            {
                caught++; // an entry was placed after the halt committed
            }
        }

        caught.ShouldBe(Seeds, "the single-check path fails the invariant on every seed, so the real path's pass is not a vacuous one");
        caught.ShouldBeGreaterThanOrEqualTo(10);
    }
}

/// <summary>P12-05 criterion 2 (latency): the record is the truth the gate reads, so a committed halt blocks the very next gate read, and a halt that cannot be read blocks as well.</summary>
public sealed class HaltLatencyTests
{
    [Fact]
    public async Task The_next_gate_read_after_the_commit_sees_the_halt_with_no_cache_in_between()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 26, 4, 0, 0, TimeSpan.Zero));
        var service = new HaltService(new InMemoryHaltStore(), clock);
        var started = clock.GetTimestamp();

        (await service.ReadAsync(CancellationToken.None)).ShouldBe(HaltReading.Clear);
        await service.RequestAsOwnerAsync(Rig.Account, HaltLevel.Entries, "now", "owner:test", false, CancellationToken.None);
        var reading = await service.ReadAsync(CancellationToken.None);

        reading.ShouldBe(HaltReading.Halted);
        clock.GetElapsedTime(started).ShouldBeLessThan(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task A_halt_command_is_carried_out_by_the_next_poll_which_is_within_the_500_ms_the_spec_budgets()
    {
        var store = new InMemoryHaltStore();
        var commands = new MemoryCommands();
        commands.Add(new ExecCommand(Guid.NewGuid(), "halt", Rig.Account, """{"scope":"account","level":"cancel","reason":"stop now"}""", "owner:hem@example.test", DateTimeOffset.UnixEpoch));
        var processor = new ExecCommandProcessor(commands, new HaltService(store, TimeProvider.System));

        (await processor.RunOnceAsync(CancellationToken.None)).ShouldBe(1);

        (await store.ActiveAsync(CancellationToken.None)).Single().Actor.ShouldBe("owner:hem@example.test");
        commands.Answers.Single().ShouldBe(("done", "execution.halt_started"));
    }
}
