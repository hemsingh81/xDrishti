using Microsoft.Extensions.Time.Testing;
using XDrishti.Execution.Application;
using XDrishti.Execution.Application.Halts;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Application.Recon;
using XDrishti.Execution.Domain;
using XDrishti.Execution.Domain.Protection;
using XDrishti.Execution.Domain.Recon;
using XDrishti.Execution.Tests.Protection;

namespace XDrishti.Execution.Tests.Recon;

/// <summary>P12-06 criterion 6: crash recovery resolves unknown outcomes by lookup, never by sending again, reads before it decides, and stays recovering when it cannot read.</summary>
public sealed class RecoveryTests
{
    private static readonly DateOnly Session = new(2026, 10, 30);

    /// <summary>The broker as recovery sees it: every write is logged (recovery must make none), lookups can fail, reads can be down.</summary>
    private sealed class WatchedBroker(FakeBroker inner) : IBrokerOrders, IBrokerReads
    {
        public List<string> Log { get; } = [];

        public bool LookupsFail { get; set; }

        public Task<BrokerAck> PlaceAsync(BrokerLeg leg, CancellationToken cancellationToken)
        {
            Log.Add("write");
            return inner.PlaceAsync(leg, cancellationToken);
        }

        public Task<BrokerAck> ModifyAsync(string clientOrderId, decimal price, CancellationToken cancellationToken)
        {
            Log.Add("write");
            return inner.ModifyAsync(clientOrderId, price, cancellationToken);
        }

        public Task<BrokerAck> CancelAsync(string clientOrderId, CancellationToken cancellationToken)
        {
            Log.Add("write");
            return inner.CancelAsync(clientOrderId, cancellationToken);
        }

        public Task<BrokerLeg?> LookupAsync(string clientOrderId, CancellationToken cancellationToken)
        {
            Log.Add("lookup");
            return LookupsFail ? throw new BrokerLookupFailedException("down") : inner.LookupAsync(clientOrderId, cancellationToken);
        }

        public Task<BrokerRead<IReadOnlyList<BrokerOrderView>>> GetOrderBookAsync(CancellationToken cancellationToken)
        {
            Log.Add("read");
            return inner.GetOrderBookAsync(cancellationToken);
        }

        public Task<BrokerRead<IReadOnlyList<BrokerTradeView>>> GetTradeBookAsync(CancellationToken cancellationToken) => inner.GetTradeBookAsync(cancellationToken);

        public Task<BrokerRead<IReadOnlyList<BrokerPositionView>>> GetPositionsAsync(CancellationToken cancellationToken) => inner.GetPositionsAsync(cancellationToken);

        public Task<BrokerRead<BrokerFundsView>> GetFundsAsync(CancellationToken cancellationToken) => inner.GetFundsAsync(cancellationToken);
    }

    private sealed class Rig
    {
        public Rig(IEnumerable<RecoverLeg> legs, FakeBroker? broker = null, FakeTimeProvider? clock = null)
        {
            Clock = clock ?? new FakeTimeProvider(ReconFixtures.Now);
            Fake = broker ?? new FakeBroker(Clock);
            Broker = new WatchedBroker(Fake);
            Record = new InMemoryRecoveryRecord(legs);
            Halts = new RecordingHaltRequests();
            Alarms = new InMemoryAlarmStore();
            Ours = new StaticReconSource();
            var breaks = new BreakService(Broker, Ours, new InMemoryReconStore { Session = Session }, Halts, Alarms, new MemoryEvents(), new ReadBudget(Clock, 1000), Clock, () => ReconSettings.Default);
            Service = new RecoveryService(Record, new LegLookup(Broker, Clock), Broker, breaks, new HaltService(new InMemoryHaltStore(), Clock), Ours, Alarms, new ReadBudget(Clock, 1000), Clock, () => ReconSettings.Default);
        }

        public FakeTimeProvider Clock { get; }

        public FakeBroker Fake { get; }

        public WatchedBroker Broker { get; }

        public InMemoryRecoveryRecord Record { get; }

        public RecordingHaltRequests Halts { get; }

        public InMemoryAlarmStore Alarms { get; }

        public StaticReconSource Ours { get; }

        public RecoveryService Service { get; }

        public async Task<RecoveryReport> RunAsync()
        {
            var task = Service.RunAsync(Session, CancellationToken.None);
            while (!task.IsCompleted)
            {
                Clock.Advance(TimeSpan.FromSeconds(1));
                await Task.Delay(1, TestContext.Current.CancellationToken);
            }

            return await task;
        }
    }

    private static RecoverLeg Leg(string cid, LegStatus status, LegKind kind = LegKind.Entry, int filled = 0, string? brokerId = null) =>
        new(ReconFixtures.Account, 361, cid, kind, status, 10, filled, brokerId);

    private static string Cid(LegKind kind, int attempt = 1) => new ClientOrderId(Session, 361, kind, attempt).ToString();

    private static Task<BrokerAck> PlaceAtBrokerAsync(FakeBroker broker, string cid, LegKind kind) =>
        broker.PlaceAsync(new BrokerLeg(cid, kind, kind == LegKind.Entry ? ExecSide.Buy : ExecSide.Sell, ReconFixtures.Security, 10, 100m, kind == LegKind.Stop ? 99m : null), CancellationToken.None);

    [Fact]
    public async Task A_leg_the_broker_has_is_adopted_and_a_leg_it_never_had_is_failed_and_nothing_is_sent()
    {
        var had = Cid(LegKind.Entry);
        var never = Cid(LegKind.Exit);
        var rig = new Rig([Leg(had, LegStatus.Sent), Leg(never, LegStatus.Sent, LegKind.Exit)]);
        await PlaceAtBrokerAsync(rig.Fake, had, LegKind.Entry);

        var report = await rig.RunAsync();

        rig.Record.Adopted.ShouldContain(had);
        rig.Record.Failed.ShouldBe([never]);
        rig.Broker.Log.ShouldNotContain("write");
        rig.Fake.Legs.Count.ShouldBe(1);
        (report.Adopted, report.Failed, report.UnknownLegs).ShouldBe((1, 1, 0));
    }

    [Fact]
    public async Task A_lookup_that_fails_leaves_the_leg_unknown_the_account_recovering_and_sends_nothing()
    {
        var cid = Cid(LegKind.Entry);
        var rig = new Rig([Leg(cid, LegStatus.Sent)]);
        rig.Broker.LookupsFail = true;
        rig.Ours.Set(ReconFixtures.Account, new OurAccountView([ReconFixtures.Leg(cid, LegStatus.Sent, filled: 0, brokerId: null, changed: ReconFixtures.Now)], [], [], []));

        var report = await rig.RunAsync();

        report.UnknownLegs.ShouldBe(1);
        report.Complete.ShouldBeFalse();
        rig.Record.Legs.Single().Status.ShouldBe(LegStatus.Sent);
        rig.Broker.Log.ShouldNotContain("write");
    }

    [Fact]
    public async Task An_unreadable_broker_keeps_the_host_recovering_changes_no_leg_and_after_120_seconds_raises_recovery_slow()
    {
        var cid = Cid(LegKind.Entry);
        var rig = new Rig([Leg(cid, LegStatus.Pending, brokerId: "B1")]);
        await PlaceAtBrokerAsync(rig.Fake, cid, LegKind.Entry);
        rig.Fake.TokenExpired = true;

        var first = await rig.RunAsync();
        first.BrokerRead.ShouldBeFalse();
        first.Complete.ShouldBeFalse();
        first.Reason.ShouldBe("broker_unreadable");
        rig.Alarms.Raised.ShouldBeEmpty();

        rig.Clock.Advance(TimeSpan.FromSeconds(121));
        (await rig.RunAsync()).Complete.ShouldBeFalse();

        rig.Alarms.Raised.ShouldHaveSingleItem().Code.ShouldBe(AlarmCodes.RecoverySlow);
        rig.Record.Adopted.ShouldBeEmpty();
        rig.Broker.Log.ShouldNotContain("write");
    }

    [Fact]
    public async Task Unreadable_halts_keep_the_host_recovering_before_anything_is_read()
    {
        var rig = new Rig([]);
        var service = new RecoveryService(rig.Record, new LegLookup(rig.Broker, rig.Clock), rig.Broker, null!, new HaltService(new ThrowingHaltStore(), rig.Clock), rig.Ours, rig.Alarms, new ReadBudget(rig.Clock), rig.Clock, () => ReconSettings.Default);

        var report = await service.RunAsync(Session, CancellationToken.None);

        (report.Reason, report.Complete).ShouldBe(("halts_unreadable", false));
        rig.Broker.Log.ShouldBeEmpty();
    }

    private sealed class ThrowingHaltStore : IHaltStore
    {
        public Task<(ActiveHalt Halt, bool Created)> AppendAsync(HaltDraft draft, CancellationToken cancellationToken) => throw new InvalidOperationException("down");

        public Task<IReadOnlyList<ActiveHalt>> ActiveAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("down");

        public Task<bool> AutoClearAsync(long haltId, string triggerCode, string detail, DateTimeOffset at, CancellationToken cancellationToken) => throw new InvalidOperationException("down");
    }

    [Fact]
    public async Task Agreeing_books_make_the_account_ready_and_a_break_freezes_it_and_asks_for_the_freeze()
    {
        var cid = Cid(LegKind.Entry);
        var rig = new Rig([Leg(cid, LegStatus.Pending, brokerId: "B1")]);
        await PlaceAtBrokerAsync(rig.Fake, cid, LegKind.Entry);
        rig.Fake.SetPrice(ReconFixtures.Security, 99m);
        rig.Ours.Set(ReconFixtures.Account, new OurAccountView([ReconFixtures.Leg(cid, brokerId: "B1")], [ReconFixtures.Fill(cid: cid, price: 99m)], [new OurPositionView(ReconFixtures.Security, 10)], []));
        var ready = await rig.RunAsync();
        ready.Accounts[ReconFixtures.Account].ShouldBe(AccountRecovery.Ready);
        ready.Complete.ShouldBeTrue();
        ready.BrokerReadAt.ShouldNotBeNull();

        rig.Fake.ShiftPosition(ReconFixtures.Security, 2);
        var frozen = await rig.RunAsync();

        frozen.Accounts[ReconFixtures.Account].ShouldBe(AccountRecovery.Frozen);
        frozen.Complete.ShouldBeTrue("a frozen account does not keep the host recovering");
        rig.Halts.Requests.ShouldContain((ReconFixtures.Account, HaltLevel.Freeze, "reconciliation_break"));
    }

    [Fact]
    public async Task Step_four_adopts_the_brokers_newer_status_and_fill_with_source_recovery()
    {
        var cid = Cid(LegKind.Entry);
        var rig = new Rig([Leg(cid, LegStatus.Pending, brokerId: "B1")]);
        await PlaceAtBrokerAsync(rig.Fake, cid, LegKind.Entry);
        rig.Fake.SetPrice(ReconFixtures.Security, 99m);

        await rig.RunAsync();

        var leg = rig.Record.Legs.Single();
        (leg.Status, leg.FilledQty).ShouldBe((LegStatus.Traded, 10));
    }

    // ---- 500 seeds of random crash points
    private static async Task<(Rig Rig, string[] Cids)> CrashAsync(Random rng)
    {
        var entry = Cid(LegKind.Entry);
        var stop = Cid(LegKind.Stop, 1);
        var exit = Cid(LegKind.Exit);
        var legs = new List<RecoverLeg>();
        var cids = new List<string> { entry };
        var shape = rng.Next(4); // open, filled, protected, closing
        var crash = rng.Next(4); // the four points of the send protocol, for the leg that was in flight
        var clock = new FakeTimeProvider(ReconFixtures.Now);
        var fake = new FakeBroker(clock);
        switch (shape)
        {
            case 0: // the entry was in flight at the crash
                if (crash >= 1)
                {
                    await PlaceAtBrokerAsync(fake, entry, LegKind.Entry);
                }

                legs.Add(Leg(entry, crash == 3 ? LegStatus.Pending : LegStatus.Sent, brokerId: crash == 3 ? "B1" : null));
                break;
            case 1: // filled, the stop was in flight
                await PlaceAtBrokerAsync(fake, entry, LegKind.Entry);
                fake.SetPrice(ReconFixtures.Security, 99m);
                legs.Add(Leg(entry, LegStatus.Traded, filled: 10, brokerId: "B1"));
                if (crash >= 1)
                {
                    await PlaceAtBrokerAsync(fake, stop, LegKind.Stop);
                }

                legs.Add(Leg(stop, crash == 3 ? LegStatus.Pending : LegStatus.Sent, LegKind.Stop, brokerId: crash == 3 ? "B2" : null));
                cids.Add(stop);
                break;
            case 2: // protected: the stop rests, nothing was in flight
                await PlaceAtBrokerAsync(fake, entry, LegKind.Entry);
                fake.SetPrice(ReconFixtures.Security, 99m);
                await PlaceAtBrokerAsync(fake, stop, LegKind.Stop);
                legs.Add(Leg(entry, LegStatus.Traded, filled: 10, brokerId: "B1"));
                legs.Add(Leg(stop, LegStatus.Pending, LegKind.Stop, brokerId: "B2"));
                cids.Add(stop);
                break;
            default: // closing: the exit was in flight
                await PlaceAtBrokerAsync(fake, entry, LegKind.Entry);
                fake.SetPrice(ReconFixtures.Security, 99m);
                legs.Add(Leg(entry, LegStatus.Traded, filled: 10, brokerId: "B1"));
                if (crash >= 1)
                {
                    await PlaceAtBrokerAsync(fake, exit, LegKind.Exit);
                }

                legs.Add(Leg(exit, crash == 3 ? LegStatus.Pending : LegStatus.Sent, LegKind.Exit, brokerId: crash == 3 ? "B2" : null));
                cids.Add(exit);
                break;
        }

        return (new Rig(legs, fake, clock), [.. cids]);
    }

    [Fact]
    public async Task Over_500_seeds_of_random_crash_points_no_duplicate_order_no_write_every_leg_adopted_or_failed()
    {
        for (var seed = 0; seed < 500; seed++)
        {
            var (rig, cids) = await CrashAsync(new Random(seed));
            var ordersBefore = rig.Fake.Legs.Count;
            var callsBefore = rig.Fake.Calls;

            var report = await rig.RunAsync();

            report.BrokerRead.ShouldBeTrue($"seed {seed}");
            rig.Broker.Log.ShouldNotContain("write", $"seed {seed}: recovery sends nothing");
            rig.Fake.Calls.ShouldBe(callsBefore, $"seed {seed}: no write reached the broker (protected positions get no new instruction)");
            rig.Fake.Legs.Count.ShouldBe(ordersBefore, $"seed {seed}: no duplicate order");
            rig.Fake.Legs.Select(l => l.ClientOrderId).Distinct().Count().ShouldBe(rig.Fake.Legs.Count, $"seed {seed}");
            rig.Record.Legs.ShouldAllBe(l => l.Status != LegStatus.New && l.Status != LegStatus.Sent, $"seed {seed}: every leg ends adopted or failed");
            rig.Broker.Log.IndexOf("read").ShouldBeGreaterThanOrEqualTo(0, $"seed {seed}");
            cids.ShouldNotBeEmpty();
            report.Complete.ShouldBeTrue($"seed {seed}");
        }
    }

    [Fact]
    public async Task Over_500_seeds_an_unreadable_broker_leaves_the_host_recovering_and_every_leg_unchanged()
    {
        for (var seed = 0; seed < 500; seed++)
        {
            var (rig, _) = await CrashAsync(new Random(seed));
            var before = rig.Record.Legs;
            rig.Fake.TokenExpired = true;

            var report = await rig.RunAsync();

            report.Complete.ShouldBeFalse($"seed {seed}");
            rig.Record.Legs.ShouldBe(before, $"seed {seed}");
            rig.Broker.Log.ShouldNotContain("write", $"seed {seed}");
        }
    }

    [Fact]
    public async Task Negative_control_a_recovery_that_sends_before_reading_is_caught_on_at_least_10_seeds()
    {
        var caught = 0;
        for (var seed = 0; seed < 500; seed++)
        {
            var (rig, _) = await CrashAsync(new Random(seed));
            var callsBefore = rig.Fake.Calls;

            // The control: resend every leg that is still in new or sent, without looking it up and before reading the book.
            foreach (var leg in rig.Record.Legs.Where(l => l.Status is LegStatus.New or LegStatus.Sent))
            {
                await rig.Broker.PlaceAsync(new BrokerLeg(leg.ClientOrderId, leg.Kind, ExecSide.Buy, ReconFixtures.Security, leg.Qty, 100m), CancellationToken.None);
            }

            if (rig.Broker.Log.Contains("write") && rig.Fake.Calls > callsBefore)
            {
                caught++;
            }
        }

        caught.ShouldBeGreaterThanOrEqualTo(10);
    }

    [Fact]
    public async Task A_protected_position_gets_no_instruction_at_all_during_recovery()
    {
        var entry = Cid(LegKind.Entry);
        var stop = Cid(LegKind.Stop);
        var rig = new Rig([Leg(entry, LegStatus.Traded, filled: 10, brokerId: "B1"), Leg(stop, LegStatus.Pending, LegKind.Stop, brokerId: "B2")]);
        await PlaceAtBrokerAsync(rig.Fake, entry, LegKind.Entry);
        rig.Fake.SetPrice(ReconFixtures.Security, 99m);
        await PlaceAtBrokerAsync(rig.Fake, stop, LegKind.Stop);
        var calls = rig.Fake.Calls;

        await rig.RunAsync();

        rig.Fake.Calls.ShouldBe(calls);
        rig.Fake.StatusOf(stop).ShouldBe("PENDING");
    }
}
