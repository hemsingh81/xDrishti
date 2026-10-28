using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using XDrishti.Application.Abstractions.LiveTrading;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.LiveTrading;
using XDrishti.Application.Tests.Accounts;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.LiveTrading;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Application.Tests.LiveTrading;

/// <summary>Spec P11-04 (04A) at the use-case level, on fakes: the owner sets S1 or lowers; a raise is refused until the gates exist; the gate, the owner and the audit without the reason; the switches append one row and read OFF by default.</summary>
public sealed class RampHandlerTests
{
    private const string Owner = "owner:hem@example.invalid";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Ramp : IRampStore
    {
        public List<StoredRampEvent> Rows { get; } = [];

        public bool Refuse { get; set; }

        public ValueTask<RampState?> CurrentAsync(Guid accountId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Rows.LastOrDefault(r => r.AccountId == accountId) is { } r ? new RampState(r.Id, r.ToStep, r.Hold) : null);

        public async ValueTask<IReadOnlyDictionary<Guid, RampState>> CurrentAllAsync(CancellationToken cancellationToken)
        {
            var all = new Dictionary<Guid, RampState>();
            foreach (var id in Rows.Select(r => r.AccountId).Distinct())
            {
                all[id] = (await CurrentAsync(id, cancellationToken))!;
            }

            return all;
        }

        public ValueTask<IReadOnlyList<StoredRampEvent>> HistoryAsync(Guid accountId, int limit, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<StoredRampEvent>>([.. Rows.Where(r => r.AccountId == accountId).OrderByDescending(r => r.Id).Take(limit)]);

        public ValueTask<StoredRampEvent?> AppendAsync(RampEvent ramp, CancellationToken cancellationToken)
        {
            if (Refuse)
            {
                return ValueTask.FromResult<StoredRampEvent?>(null);
            }

            var row = new StoredRampEvent(Rows.Count + 1, ramp.AccountId, ramp.Kind, ramp.FromStep, ramp.ToStep, ramp.Hold, ramp.Actor, ramp.Reason, null, DateTimeOffset.UnixEpoch);
            Rows.Add(row);
            return ValueTask.FromResult<StoredRampEvent?>(row);
        }
    }

    private sealed class Gate(bool on) : ILiveWritesGate
    {
        public bool Enabled { get; } = on;
    }

    private sealed class Factors : ILiveRampSettings
    {
        public IReadOnlyList<double> StepFactors => RampPolicy.DefaultFactors;

        public RampThresholds Thresholds => RampThresholds.Defaults;
    }

    private sealed class Switches : ILiveSettingsStore
    {
        public List<StoredLiveSettings> Rows { get; } = [];

        public ValueTask<LiveSettings> CurrentAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(Rows.LastOrDefault() is { } r ? new LiveSettings(r.Id, r.Hash, r.AutoApproveTickets, r.AutoRampUp) : LiveSettings.Defaults);

        public ValueTask<IReadOnlyList<StoredLiveSettings>> HistoryAsync(int limit, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<StoredLiveSettings>>([.. Rows.OrderByDescending(r => r.Id).Take(limit)]);

        public ValueTask<StoredLiveSettings?> AppendAsync(string actor, bool autoApproveTickets, bool autoRampUp, string reason, long? expectedCurrentId, CancellationToken cancellationToken)
        {
            if (expectedCurrentId != Rows.LastOrDefault()?.Id)
            {
                return ValueTask.FromResult<StoredLiveSettings?>(null);
            }

            var row = new StoredLiveSettings(Rows.Count + 1, new string('c', 64), autoApproveTickets, autoRampUp, actor, reason, DateTimeOffset.UnixEpoch);
            Rows.Add(row);
            return ValueTask.FromResult<StoredLiveSettings?>(row);
        }
    }

    private readonly AccountsFixture _fx = new();
    private readonly Ramp _ramp = new();

    private SetRampStepHandler Setter(bool gate = true)
    {
        var calendar = Substitute.For<ITradingCalendar>();
        calendar.GetAsync(Arg.Any<CancellationToken>()).Returns(new TradingCalendar());
        var store = new InMemoryLiveStore();
        var assessor = new RampAssessor(_ramp, store, new InMemoryExecutionStore(), new InMemoryHaltStore(), new LiveTicketSource(Substitute.For<IPlanStore>()), calendar, _fx.Data, new Factors(), _fx.Clock);
        var loss = new LiveLossReader(store, new FakeLiveMarket(), _fx.Clock);
        var view = new GetRampHandler(_fx.Data, _fx.Data, _ramp, new Factors(), new Gate(gate), assessor, loss, new InMemoryHaltStore(), new Switches(), _fx.Clock);
        return new(_fx.Data, _ramp, view, assessor, new Gate(gate), _fx.Data, _fx.Data, _fx.Clock, NullLogger<SetRampStepHandler>.Instance);
    }

    private async Task<BrokerAccount> TradingAccountAsync() =>
        await _fx.CreatedAsync(Inputs.Account(trading: Inputs.Trading(_fx.Standard.Id), roles: Inputs.Roles(true, false)));

    [Fact]
    public async Task The_owner_sets_s1_and_the_view_shows_the_scale_and_the_live_risk()
    {
        var account = await TradingAccountAsync();

        var result = await Setter().HandleAsync(new SetRampStepCommand(Owner, account.Id, 1, "first step"), Ct);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code : string.Empty);
        result.Value.Step.ShouldBe(1);
        result.Value.ScaleEffective.ShouldBe(0.25);
        result.Value.IntendedRiskPct.ShouldNotBeNull();
        result.Value.LiveRiskPct.ShouldBe(result.Value.IntendedRiskPct * 0.25);
        result.Value.GatesBuilt.ShouldBeTrue();
        result.Value.Eligible.ShouldBeFalse();
        result.Value.NextStep.ShouldBe(2);
        _ramp.Rows.Single().Actor.ShouldBe(Owner);
        var audit = _fx.Data.Events.Single(e => e.Event == AccountEventType.RampStepSet);
        audit.Detail.ShouldBe("event=1 kind=set_step from=none to=1");
        audit.Detail!.ShouldNotContain("first step");
    }

    [Fact]
    public async Task A_raise_is_refused_with_gates_not_met_and_writes_nothing()
    {
        var account = await TradingAccountAsync();
        await Setter().HandleAsync(new SetRampStepCommand(Owner, account.Id, 1, "first"), Ct);

        var raise = await Setter().HandleAsync(new SetRampStepCommand(Owner, account.Id, 2, "up"), Ct);

        raise.Error.Code.ShouldBe(RampErrors.GatesNotMetCode);
        _ramp.Rows.Count.ShouldBe(1);
    }

    [Fact]
    public async Task The_gate_the_owner_the_account_and_the_role_are_each_checked()
    {
        var account = await TradingAccountAsync();
        var plain = await _fx.CreatedAsync(Inputs.Account(name: "Plain", clientId: "1100000002", roles: Inputs.Roles(false, true)));

        (await Setter(gate: false).HandleAsync(new SetRampStepCommand(Owner, account.Id, 1, "x"), Ct)).Error.Code.ShouldBe(LiveErrors.CapabilityOff);
        (await Setter().HandleAsync(new SetRampStepCommand("rule:ramp", account.Id, 1, "x"), Ct)).Error.Code.ShouldBe(LiveErrors.OwnerOnly);
        (await Setter().HandleAsync(new SetRampStepCommand(Owner, Guid.NewGuid(), 1, "x"), Ct)).Error.Code.ShouldBe(LiveErrors.AccountNotFound);
        (await Setter().HandleAsync(new SetRampStepCommand(Owner, plain.Id, 1, "x"), Ct)).Error.Code.ShouldBe(ComplianceRecord.InvalidCode);
        _ramp.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_move_the_table_refused_is_a_stale_conflict()
    {
        var account = await TradingAccountAsync();
        _ramp.Refuse = true;

        (await Setter().HandleAsync(new SetRampStepCommand(Owner, account.Id, 1, "x"), Ct)).Error.Code.ShouldBe(LiveErrors.Stale);
    }

    [Fact]
    public async Task The_switches_default_off_and_a_change_appends_one_audited_row_and_a_stale_form_is_refused()
    {
        var store = new Switches();
        var handler = new ChangeLiveSettingsHandler(store, new Gate(true), _fx.Data, _fx.Data, _fx.Clock, NullLogger<ChangeLiveSettingsHandler>.Instance);

        (await new GetLiveSettingsHandler(store, new Gate(true)).HandleAsync(new GetLiveSettingsQuery(), Ct)).ShouldSatisfyAllConditions(v => v.Id.ShouldBeNull(), v => v.AutoApproveTickets.ShouldBeFalse(), v => v.AutoRampUp.ShouldBeFalse());
        var on = await handler.HandleAsync(new ChangeLiveSettingsCommand(Owner, "try it", true, false, 0), Ct);

        on.IsSuccess.ShouldBeTrue();
        on.Value.AutoApproveTickets.ShouldBeTrue();
        on.Value.History.Count.ShouldBe(1);
        (await handler.HandleAsync(new ChangeLiveSettingsCommand(Owner, "again", false, false, 0), Ct)).Error.Code.ShouldBe(LiveErrors.Stale);
        (await handler.HandleAsync(new ChangeLiveSettingsCommand(Owner, " ", false, false, 1), Ct)).Error.Code.ShouldBe(ComplianceRecord.InvalidCode);
        (await handler.HandleAsync(new ChangeLiveSettingsCommand("rule:autonomy", "x", false, false, 1), Ct)).Error.Code.ShouldBe(LiveErrors.OwnerOnly);
        (await new ChangeLiveSettingsHandler(store, new Gate(false), _fx.Data, _fx.Data, _fx.Clock, NullLogger<ChangeLiveSettingsHandler>.Instance).HandleAsync(new ChangeLiveSettingsCommand(Owner, "x", false, false, 1), Ct)).Error.Code.ShouldBe(LiveErrors.CapabilityOff);
        store.Rows.Count.ShouldBe(1);
    }
}
