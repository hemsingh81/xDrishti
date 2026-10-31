using Microsoft.Extensions.Logging.Abstractions;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.LiveTrading;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Auth;
using XDrishti.Application.Execution;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Application.Tests.Execution;

/// <summary>
/// ADR 0174 at the use-case level, on fakes: the owner's change of the global switches (password on every change, a raise behind the live-writes gate and a full closed session, auto_live never, an audit event) and the owner's drill request
/// (owner, listed account, no halt in force, a command row that carries nothing). Every refusal writes nothing.
/// </summary>
public sealed class ExecutionSwitchHandlerTests
{
    private const string Owner = "owner:hem@example.invalid";

    // Monday 26 Oct 2026, 11:00 India time: a raise now takes effect on Wednesday at 09:00.
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 11, 0, 0, TimeSpan.FromMinutes(330));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Access : IExecutionSettingsAccess
    {
        public List<ExecutionSettingsRowView> Rows { get; } = [];

        public string? Refusal { get; set; }

        public Task<DateTimeOffset> NowAsync(CancellationToken cancellationToken) => Task.FromResult(Now);

        public Task<IReadOnlyList<ExecutionSettingsRowView>> RowsAsync(int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ExecutionSettingsRowView>>([.. Rows.OrderByDescending(r => r.Id).Take(take)]);

        public Task<(long? Id, string? Refused)> InsertAsync(NewExecutionSettings row, CancellationToken cancellationToken)
        {
            if (Refusal is not null)
            {
                return Task.FromResult<(long?, string?)>((null, Refusal));
            }

            var id = Rows.Count + 1L;
            Rows.Add(new ExecutionSettingsRowView(id, row.ExecutionEnabled, false, row.Actor, row.Reason, Now, row.EffectiveFrom, new string('a', 64)));
            return Task.FromResult<(long?, string?)>((id, null));
        }
    }

    private sealed class Password : ICommandHandler<StepUpCommand, StepUpResult>
    {
        public List<StepUpCommand> Calls { get; } = [];

        public ValueTask<Result<StepUpResult>> HandleAsync(StepUpCommand command, CancellationToken cancellationToken)
        {
            Calls.Add(command);
            return ValueTask.FromResult(command.Password == "right" ? Result.Success(new StepUpResult(DateTimeOffset.UnixEpoch)) : Result.Failure<StepUpResult>(AuthErrors.InvalidCredentials));
        }
    }

    private sealed class Gate(bool enabled) : ILiveWritesGate
    {
        public bool Enabled { get; } = enabled;
    }

    private sealed class Calendar : ITradingCalendar
    {
        public ValueTask<TradingCalendar> GetAsync(CancellationToken cancellationToken) => ValueTask.FromResult(new TradingCalendar());

        public void Invalidate()
        {
        }
    }

    private sealed class Events : IAccountEventLog
    {
        public List<AccountEvent> Recorded { get; } = [];

        public void Record(AccountEvent accountEvent) => Recorded.Add(accountEvent);

        public ValueTask<IReadOnlyList<AccountEvent>> ListForAccountAsync(Guid accountId, int take, CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<AccountEvent>>([]);
    }

    private sealed class Unit : IAccountUnitOfWork
    {
        public ValueTask<bool> TrySaveChangesAsync(CancellationToken cancellationToken) => ValueTask.FromResult(true);

        public ValueTask<Result> RunSerializedAsync(Func<CancellationToken, ValueTask<Result>> work, CancellationToken cancellationToken) => work(cancellationToken);
    }

    private sealed record Rig(ChangeExecutionSettingsHandler Handler, Access Access, Password Password, Events Events);

    private static Rig New(bool gate = true)
    {
        var access = new Access();
        var password = new Password();
        var events = new Events();
        return new Rig(
            new ChangeExecutionSettingsHandler(access, password, new Gate(gate), new Calendar(), events, new Unit(), TimeProvider.System, NullLogger<ChangeExecutionSettingsHandler>.Instance), access, password, events);
    }

    private static ChangeExecutionSettingsCommand Change(bool? on, bool? autoLive = false, string? password = "right", string actor = Owner, string? reason = "ready for assisted") =>
        new(actor, Guid.NewGuid(), new ClientInfo("127.0.0.1", "test"), on, autoLive, reason, password);

    [Fact]
    public async Task Switching_execution_on_needs_the_password_and_takes_effect_on_wednesday_at_09_00_after_tuesdays_full_session()
    {
        var rig = New();

        var missing = await rig.Handler.HandleAsync(Change(true, password: null), Ct);
        var wrong = await rig.Handler.HandleAsync(Change(true, password: "wrong"), Ct);
        var stored = await rig.Handler.HandleAsync(Change(true), Ct);

        missing.Error.Code.ShouldBe(LimitsErrors.PasswordRequired);
        wrong.Error.Code.ShouldBe(AuthErrors.InvalidCredentialsCode);
        rig.Password.Calls.ShouldAllBe(c => !c.Elevate);
        rig.Access.Rows.ShouldHaveSingleItem().EffectiveFrom.ShouldBe(new DateTimeOffset(2026, 10, 28, 9, 0, 0, TimeSpan.FromMinutes(330)));
        stored.Value.InForce.ShouldBeNull();
        stored.Value.Pending!.ExecutionEnabled.ShouldBeTrue();
        stored.Value.AutoLiveLocked.ShouldBeTrue();
        rig.Events.Recorded.ShouldHaveSingleItem().Event.ShouldBe(AccountEventType.ExecutionSettingsChanged);
        rig.Events.Recorded[0].Detail!.ShouldContain("kind=raise");
    }

    [Fact]
    public async Task A_raise_with_the_live_writes_gate_off_is_refused_before_the_password_is_checked()
    {
        var rig = New(gate: false);

        var result = await rig.Handler.HandleAsync(Change(true), Ct);

        result.Error.Code.ShouldBe("live.capability_off");
        rig.Password.Calls.ShouldBeEmpty();
        rig.Access.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task Switching_off_is_in_force_at_once_even_with_the_gate_off_still_needs_the_password_and_supersedes_a_waiting_raise()
    {
        var rig = New(gate: false);
        rig.Access.Rows.Add(new ExecutionSettingsRowView(1, false, false, Owner, "r", Now.AddDays(-3), Now.AddDays(-3), new string('a', 64)));
        rig.Access.Rows.Add(new ExecutionSettingsRowView(2, true, false, Owner, "r", Now.AddHours(-1), Now.AddDays(2), new string('a', 64)));

        var withoutPassword = await rig.Handler.HandleAsync(Change(false, password: null), Ct);
        var result = await rig.Handler.HandleAsync(Change(false), Ct);

        withoutPassword.Error.Code.ShouldBe(LimitsErrors.PasswordRequired);
        result.IsSuccess.ShouldBeTrue();
        rig.Access.Rows[^1].EffectiveFrom.ShouldBe(Now);
        result.Value.InForce!.Id.ShouldBe(3);
        result.Value.Pending.ShouldBeNull();
        rig.Events.Recorded.Single().Detail!.ShouldContain("kind=lowering");
    }

    [Fact]
    public async Task Auto_live_on_is_refused_before_anything_is_checked_or_written()
    {
        var rig = New();

        var result = await rig.Handler.HandleAsync(Change(true, autoLive: true), Ct);

        result.Error.Code.ShouldBe(ExecutionSettingsErrors.AutoLiveLocked);
        rig.Password.Calls.ShouldBeEmpty();
        rig.Access.Rows.ShouldBeEmpty();
        rig.Events.Recorded.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(null, "ok", "owner:hem@example.invalid", LimitsErrors.ValuesInvalid)]
    [InlineData(false, null, "owner:hem@example.invalid", LimitsErrors.ValuesInvalid)]
    [InlineData(false, "my password is x", "owner:hem@example.invalid", LimitsErrors.ValuesInvalid)]
    [InlineData(false, "ok", "system:x", LimitsErrors.OwnerOnly)]
    [InlineData(false, "ok", "owner:", LimitsErrors.OwnerOnly)]
    public async Task A_bad_change_is_refused_and_writes_nothing(bool? on, string? reason, string actor, string code)
    {
        var rig = New();

        var result = await rig.Handler.HandleAsync(Change(on, reason: reason, actor: actor), Ct);

        result.Error.Code.ShouldBe(code);
        rig.Access.Rows.ShouldBeEmpty();
        rig.Password.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_row_the_database_refuses_is_stale_and_leaves_no_audit_event()
    {
        var rig = New();
        rig.Access.Refusal = "switching execution on takes effect after one full closed session";

        var result = await rig.Handler.HandleAsync(Change(true), Ct);

        result.Error.Code.ShouldBe(LimitsErrors.Stale);
        result.Error.Message.ShouldContain("one full closed session");
        rig.Events.Recorded.ShouldBeEmpty();
    }

    private sealed class Facts(bool halted, params Guid[] accounts) : IExecutionFactsReader
    {
        public Task<ExecutionFacts> ReadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ExecutionFacts("ready", Now, "sandbox", null, 0, [.. accounts.Select(a => (a, "manual_live"))], halted));
    }

    private sealed class Commands : IExecutionCommands
    {
        public List<(string Kind, Guid? Account, string Payload, string Actor)> Rows { get; } = [];

        public Task<Guid> EnqueueAsync(string kind, Guid? accountId, string payloadJson, string actor, CancellationToken cancellationToken)
        {
            Rows.Add((kind, accountId, payloadJson, actor));
            return Task.FromResult(Guid.NewGuid());
        }
    }

    private static (RequestExecutionDrillHandler Handler, Commands Commands, Events Events) Drill(bool halted, params Guid[] accounts)
    {
        var commands = new Commands();
        var events = new Events();
        return (new RequestExecutionDrillHandler(new Facts(halted, accounts), commands, events, new Unit(), TimeProvider.System, NullLogger<RequestExecutionDrillHandler>.Instance), commands, events);
    }

    [Fact]
    public async Task A_drill_request_leaves_one_drill_command_with_an_empty_payload_and_an_audit_event_for_the_account()
    {
        var account = Guid.NewGuid();
        var (handler, commands, events) = Drill(false, account);

        var result = await handler.HandleAsync(new RequestExecutionDrillCommand(Owner, account), Ct);

        result.IsSuccess.ShouldBeTrue();
        commands.Rows.ShouldHaveSingleItem().ShouldBe(("drill", (Guid?)account, "{}", Owner));
        var recorded = events.Recorded.ShouldHaveSingleItem();
        recorded.Event.ShouldBe(AccountEventType.ExecutionDrillRequested);
        recorded.AccountId.ShouldBe(account);
    }

    [Fact]
    public async Task A_drill_is_refused_while_a_halt_is_in_force_for_an_unknown_account_without_an_account_or_for_a_non_owner()
    {
        var account = Guid.NewGuid();

        var halted = await Drill(true, account).Handler.HandleAsync(new RequestExecutionDrillCommand(Owner, account), Ct);
        var unknown = await Drill(false, account).Handler.HandleAsync(new RequestExecutionDrillCommand(Owner, Guid.NewGuid()), Ct);
        var none = await Drill(false, account).Handler.HandleAsync(new RequestExecutionDrillCommand(Owner, null), Ct);
        var system = Drill(false, account);
        var notOwner = await system.Handler.HandleAsync(new RequestExecutionDrillCommand("system:x", account), Ct);

        halted.Error.Code.ShouldBe(ExecutionDrillErrors.Refused);
        unknown.Error.Code.ShouldBe(ExecutionDrillErrors.UnknownAccount);
        none.Error.Code.ShouldBe(LimitsErrors.ValuesInvalid);
        notOwner.Error.Code.ShouldBe(LimitsErrors.OwnerOnly);
        system.Commands.Rows.ShouldBeEmpty();
    }
}
