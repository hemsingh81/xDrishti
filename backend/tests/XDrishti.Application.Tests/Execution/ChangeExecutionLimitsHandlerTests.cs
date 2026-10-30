using Microsoft.Extensions.Logging.Abstractions;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Auth;
using XDrishti.Application.Execution;
using XDrishti.Domain.Common;
using XDrishti.Domain.Execution.Limits;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Application.Tests.Execution;

/// <summary>
/// Spec P12-09 criterion 9 at the use-case level, on fakes: a raise needs the password (checked without opening the elevation window) and waits for one full closed session; a pure lowering needs only the session and is in force at once; the
/// answer shows the row in force and the pending row; a pending raise never displaces the lower sheet in force; every refusal writes nothing.
/// </summary>
public sealed class ChangeExecutionLimitsHandlerTests
{
    private const string Owner = "owner:hem@example.invalid";

    private static readonly Guid Account = Guid.NewGuid();

    // Monday 26 Oct 2026, 11:00 India time: a raise now takes effect on Wednesday at 09:00.
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 11, 0, 0, TimeSpan.FromMinutes(330));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Access : IExecutionLimitsAccess
    {
        public List<StoredLimits> Rows { get; } = [];

        public DateTimeOffset Clock { get; set; } = Now;

        public bool? TradingOn { get; set; } = true;

        public string? Refusal { get; set; }

        public Task<DateTimeOffset> NowAsync(CancellationToken cancellationToken) => Task.FromResult(Clock);

        public Task<bool?> TradingOnAsync(Guid accountId, CancellationToken cancellationToken) => Task.FromResult(TradingOn);

        public Task<IReadOnlyList<StoredLimits>> RowsAsync(Guid accountId, int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<StoredLimits>>([.. Rows.OrderByDescending(r => r.Id).Take(take)]);

        public Task<(long? Id, string? Refused)> InsertAsync(NewLimits row, CancellationToken cancellationToken)
        {
            if (Refusal is not null)
            {
                return Task.FromResult<(long?, string?)>((null, Refusal));
            }

            var id = Rows.Count + 1L;
            Rows.Add(new StoredLimits(id, row.AccountId, row.Limits, row.Actor, row.Reason, Clock, row.EffectiveFrom, row.SnapshotHash));
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

    private sealed class Calendar : ITradingCalendar
    {
        public ValueTask<TradingCalendar> GetAsync(CancellationToken cancellationToken) => ValueTask.FromResult(new TradingCalendar());

        public void Invalidate()
        {
        }
    }

    private static ChangeExecutionLimitsCommand Command(int? orders = 6, decimal? perOrder = 60_000m, decimal? perDay = 150_000m, decimal? loss = 1_500m, int? positions = 3, string[]? allow = null, string? start = "09:20", string? end = "14:45", string[]? products = null, string? password = null, string actor = Owner, string? reason = "first sheet") =>
        new(actor, Guid.NewGuid(), new ClientInfo("127.0.0.1", "test"), Account, orders, perOrder, perDay, loss, positions, allow ?? ["TATAPOWER", "INFY", "SBIN"], start, end, products ?? ["EQ_MIS"], false, reason, password);

    private static (ChangeExecutionLimitsHandler Handler, Access Access, Password Password) New()
    {
        var access = new Access();
        var password = new Password();
        return (new ChangeExecutionLimitsHandler(access, password, new Calendar(), NullLogger<ChangeExecutionLimitsHandler>.Instance), access, password);
    }

    [Fact]
    public async Task A_first_sheet_is_a_raise_it_needs_the_password_and_takes_effect_on_wednesday_at_09_00_after_tuesdays_full_session()
    {
        var (handler, access, password) = New();

        var missing = await handler.HandleAsync(Command(), Ct);
        var wrong = await handler.HandleAsync(Command(password: "wrong"), Ct);
        var stored = await handler.HandleAsync(Command(password: "right"), Ct);

        missing.Error.Code.ShouldBe(LimitsErrors.PasswordRequired);
        wrong.Error.Code.ShouldBe(AuthErrors.InvalidCredentialsCode);
        stored.IsSuccess.ShouldBeTrue();
        password.Calls.ShouldAllBe(c => !c.Elevate);
        access.Rows.ShouldHaveSingleItem().EffectiveFrom.ShouldBe(new DateTimeOffset(2026, 10, 28, 9, 0, 0, TimeSpan.FromMinutes(330)));
        stored.Value.InForce.ShouldBeNull();
        stored.Value.Pending!.MaxOrdersPerDay.ShouldBe(6);
        stored.Value.Complete.ShouldBeFalse();
        stored.Value.Missing.Count.ShouldBe(8);
    }

    [Fact]
    public async Task A_pure_lowering_needs_only_the_session_and_is_in_force_at_once()
    {
        var (handler, access, password) = New();
        access.Rows.Add(new StoredLimits(1, Account, Sheet(), Owner, "r", Now.AddDays(-5), Now.AddDays(-3), Sheet().Hash()));

        var result = await handler.HandleAsync(Command(orders: 3, allow: ["INFY"], start: "10:00", end: "14:00", reason: "calmer"), Ct);

        result.IsSuccess.ShouldBeTrue();
        password.Calls.ShouldBeEmpty();
        access.Rows[^1].EffectiveFrom.ShouldBe(Now);
        result.Value.InForce!.MaxOrdersPerDay.ShouldBe(3);
        result.Value.Pending.ShouldBeNull();
        result.Value.Complete.ShouldBeTrue();
        result.Value.History.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_mixed_change_counts_as_a_raise()
    {
        var (handler, access, _) = New();
        access.Rows.Add(new StoredLimits(1, Account, Sheet(), Owner, "r", Now.AddDays(-5), Now.AddDays(-3), Sheet().Hash()));

        var result = await handler.HandleAsync(Command(orders: 2, allow: ["TATAPOWER", "ITC"]), Ct);

        result.Error.Code.ShouldBe(LimitsErrors.PasswordRequired);
        result.Error.Message.ShouldContain("allowList");
        access.Rows.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_raise_pending_while_a_lower_sheet_is_in_force_leaves_the_lower_one_in_force()
    {
        var (handler, access, _) = New();
        access.Rows.Add(new StoredLimits(1, Account, Sheet() with { MaxOrdersPerDay = 3 }, Owner, "r", Now.AddDays(-5), Now.AddDays(-3), Sheet().Hash()));

        var result = await handler.HandleAsync(Command(orders: 10, password: "right"), Ct);

        result.Value.InForce!.MaxOrdersPerDay.ShouldBe(3);
        result.Value.Pending!.MaxOrdersPerDay.ShouldBe(10);
        access.Clock = new DateTimeOffset(2026, 10, 28, 9, 0, 0, TimeSpan.FromMinutes(330));
        var later = await new GetExecutionLimitsHandler(access).HandleAsync(new GetExecutionLimitsQuery(Account), Ct);
        later.Value.InForce!.MaxOrdersPerDay.ShouldBe(10);
        later.Value.Pending.ShouldBeNull();
    }

    [Theory]
    [InlineData("orders", ExecutionLimits.FieldOrders)]
    [InlineData("positions", ExecutionLimits.FieldPositions)]
    [InlineData("day below order", ExecutionLimits.FieldNotionalDay)]
    [InlineData("paise", ExecutionLimits.FieldNotionalOrder)]
    [InlineData("symbol", ExecutionLimits.FieldAllowList)]
    [InlineData("window", ExecutionLimits.FieldWindow)]
    [InlineData("half window", ExecutionLimits.FieldWindow)]
    [InlineData("product", ExecutionLimits.FieldProducts)]
    public async Task An_illegal_value_is_400_values_invalid_naming_the_field_and_nothing_is_written(string what, string field)
    {
        var (handler, access, password) = New();
        var command = what switch
        {
            "orders" => Command(orders: 21, password: "right"),
            "positions" => Command(positions: 11, password: "right"),
            "day below order" => Command(perDay: 50_000m, password: "right"),
            "paise" => Command(perOrder: 1.005m, password: "right"),
            "symbol" => Command(allow: ["tatapower"], password: "right"),
            "window" => Command(start: "09:15", password: "right"),
            "half window" => Command(end: null, password: "right"),
            _ => Command(products: ["EQ_CNC"], password: "right"),
        };

        var result = await handler.HandleAsync(command, Ct);

        result.Error.Code.ShouldBe(LimitsErrors.ValuesInvalid);
        result.Error.Message.ShouldStartWith(field);
        access.Rows.ShouldBeEmpty();
        password.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Only_the_owner_on_a_known_account_with_a_reason_and_a_raise_only_while_the_trading_role_is_on()
    {
        var (handler, access, _) = New();

        (await handler.HandleAsync(Command(actor: "system:x", password: "right"), Ct)).Error.Code.ShouldBe(LimitsErrors.OwnerOnly);
        (await handler.HandleAsync(Command(reason: " ", password: "right"), Ct)).Error.Code.ShouldBe(LimitsErrors.ValuesInvalid);
        access.TradingOn = false;
        (await handler.HandleAsync(Command(password: "right"), Ct)).Error.Code.ShouldBe(LimitsErrors.Disabled);
        (await handler.HandleAsync(Command(orders: null, perOrder: null, perDay: null, loss: null, positions: null, allow: [], start: null, end: null, products: []), Ct)).IsSuccess.ShouldBeTrue();
        access.TradingOn = null;
        (await handler.HandleAsync(Command(password: "right"), Ct)).Error.Code.ShouldBe(LimitsErrors.UnknownAccount);
        (await new GetExecutionLimitsHandler(access).HandleAsync(new GetExecutionLimitsQuery(Account), Ct)).Error.Code.ShouldBe(LimitsErrors.UnknownAccount);
        (await new GetExecutionLimitsHandler(access).HandleAsync(new GetExecutionLimitsQuery(null), Ct)).Error.Code.ShouldBe(LimitsErrors.ValuesInvalid);
    }

    [Fact]
    public async Task A_database_refusal_is_409_stale()
    {
        var (handler, access, _) = New();
        access.Refusal = "a raise takes effect after one full closed session";

        var result = await handler.HandleAsync(Command(password: "right"), Ct);

        result.Error.Code.ShouldBe(LimitsErrors.Stale);
        result.Error.Kind.ShouldBe(ErrorKind.Conflict);
    }

    private static ExecutionLimits Sheet() => new(6, 60_000m, 150_000m, 1_500m, 3, ["TATAPOWER", "INFY", "SBIN"], new TimeOnly(9, 20), new TimeOnly(14, 45), ["EQ_MIS"], false);
}
