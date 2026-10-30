using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Auth;
using XDrishti.Application.Execution;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Infrastructure.Execution;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Execution.Tests.Database;

/// <summary>
/// Spec P12-09 criterion 9 and the grants on a real database: the owner's sheet is appended by the runtime role through the API handler; a raise waits for the next session after a full closed one and the trigger refuses an earlier one; a
/// lowering is stamped in force by the database clock; the ceilings are constraints; only the runtime role writes a sheet and only the owner's actor.
/// </summary>
[Collection(LimitsDatabaseSuite.Name)]
public sealed class LimitsDatabaseTests(ExecDatabase db)
{
    private readonly LimitsDb _db = new(db);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Password : ICommandHandler<StepUpCommand, StepUpResult>
    {
        public ValueTask<Result<StepUpResult>> HandleAsync(StepUpCommand command, CancellationToken cancellationToken) =>
            ValueTask.FromResult(command.Password == "right" ? Result.Success(new StepUpResult(DateTimeOffset.UnixEpoch)) : Result.Failure<StepUpResult>(AuthErrors.InvalidCredentials));
    }

    private sealed class Calendar : ITradingCalendar
    {
        public ValueTask<TradingCalendar> GetAsync(CancellationToken cancellationToken) => ValueTask.FromResult(new TradingCalendar());

        public void Invalidate()
        {
        }
    }

    private XDrishtiDbContext AppContext() => new(new DbContextOptionsBuilder<XDrishtiDbContext>().UseNpgsql(db.App).UseSnakeCaseNamingConvention().Options);

    private static ChangeExecutionLimitsCommand Command(Guid account, int orders, string[] allow, string? password) =>
        new(LimitsDb.Owner, Guid.NewGuid(), new ClientInfo("127.0.0.1", "test"), account, orders, 60_000m, 150_000m, 1_500m, 3, allow, "09:20", "14:45", ["EQ_MIS"], false, "test sheet", password);

    private static string Raw(Guid account, int orders, string effective, string actor = LimitsDb.Owner, string window = "'09:20', '14:45'", string products = "ARRAY['EQ_MIS']", string allow = "'[\"INFY\"]'") =>
        $"INSERT INTO execution.limits (account_id, max_orders_per_day, max_notional_per_order, max_notional_per_day, max_loss_per_day, max_open_positions, allow_list, window_start, window_end, products, actor, reason, effective_from, snapshot_hash) " +
        $"VALUES ('{account}', {orders}, 60000, 150000, 1500, 3, {allow}::jsonb, {window}, {products}, '{actor}', 'r', {effective}, repeat('a', 64))";

    [Fact]
    public async Task Through_the_api_handler_a_raise_waits_for_the_next_session_and_a_lowering_is_in_force_at_once()
    {
        var account = await _db.AccountAsync(mode: null);
        await using var context = AppContext();
        var access = new ExecutionLimitsAccess(context);
        var handler = new ChangeExecutionLimitsHandler(access, new Password(), new Calendar(), NullLogger<ChangeExecutionLimitsHandler>.Instance);

        var first = await handler.HandleAsync(Command(account, 6, ["TATAPOWER", "INFY", "SBIN"], null), Ct);
        var raise = await handler.HandleAsync(Command(account, 6, ["TATAPOWER", "INFY", "SBIN"], "right"), Ct);
        await _db.SheetAsync(account);
        var more = await handler.HandleAsync(Command(account, 10, ["TATAPOWER", "INFY", "SBIN"], "right"), Ct);
        var lower = await handler.HandleAsync(Command(account, 4, ["INFY"], null), Ct);
        var view = await new GetExecutionLimitsHandler(access).HandleAsync(new GetExecutionLimitsQuery(account), Ct);

        first.Error.Code.ShouldBe(LimitsErrors.PasswordRequired, "a first sheet sets values that were unset: a raise");
        raise.IsSuccess.ShouldBeTrue(raise.IsFailure ? raise.Error.Message : string.Empty);
        raise.Value.InForce.ShouldBeNull();
        raise.Value.Pending!.EffectiveFrom.ShouldBeGreaterThan(raise.Value.ServerNow.AddHours(12));
        more.Value.InForce!.MaxOrdersPerDay.ShouldBe(6, "a pending raise never displaces the sheet in force");
        more.Value.Pending!.MaxOrdersPerDay.ShouldBe(10);
        lower.IsSuccess.ShouldBeTrue(lower.IsFailure ? lower.Error.Message : string.Empty);
        view.Value.InForce!.MaxOrdersPerDay.ShouldBe(4);
        view.Value.InForce.AllowList.ShouldBe(["INFY"]);
        view.Value.Pending.ShouldBeNull("the newer lowering supersedes the earlier raises");
        view.Value.Complete.ShouldBeTrue();
        view.Value.History.Count.ShouldBe(4);
        (await ExecDatabase.ScalarAsync(db.Owner, $"SELECT abs(extract(epoch FROM effective_from - changed_at)) < 1 FROM execution.limits WHERE account_id = '{account}' AND max_orders_per_day = 4")).ShouldBe(true);
    }

    [Fact]
    public async Task The_trigger_refuses_a_raise_that_would_take_effect_before_09_00_of_the_next_day_and_stamps_a_lowering_now()
    {
        var account = await _db.AccountAsync(mode: null);
        var next = "(((clock_timestamp() AT TIME ZONE 'Asia/Kolkata')::date + 1) + time '09:00') AT TIME ZONE 'Asia/Kolkata'";

        (await LimitsDb.RefusalAsync(db.App, Raw(account, 6, "now()"))).ShouldContain("execution.limits_refused");
        (await LimitsDb.RefusalAsync(db.App, Raw(account, 6, next + " - interval '1 second'"))).ShouldContain("execution.limits_refused");
        (await LimitsDb.RefusalAsync(db.App, Raw(account, 6, next))).ShouldBeEmpty();

        await _db.SheetAsync(account);
        (await LimitsDb.RefusalAsync(db.App, Raw(account, 5, "now() + interval '30 days'"))).ShouldBeEmpty();
        (await ExecDatabase.ScalarAsync(db.Owner, $"SELECT abs(extract(epoch FROM effective_from - changed_at)) < 1 FROM execution.limits WHERE account_id = '{account}' ORDER BY id DESC LIMIT 1")).ShouldBe(true);
        (await LimitsDb.RefusalAsync(db.App, Raw(account, 7, "now()"))).ShouldContain("execution.limits_refused", customMessage: "7 orders is above the 6 in force");
        (await LimitsDb.RefusalAsync(db.App, Raw(account, 5, "now()", allow: "'[\"INFY\",\"ITC\"]'"))).ShouldContain("execution.limits_refused", customMessage: "a symbol added is a raise");
    }

    [Theory]
    [InlineData("orders")]
    [InlineData("window")]
    [InlineData("product")]
    [InlineData("symbol")]
    [InlineData("actor")]
    public async Task The_ceilings_and_the_shape_are_constraints(string what)
    {
        var account = await _db.AccountAsync(mode: null);
        var effective = "now() + interval '10 days'";
        var sql = what switch
        {
            "orders" => Raw(account, 21, effective),
            "window" => Raw(account, 6, effective, window: "'09:15', '14:45'"),
            "product" => Raw(account, 6, effective, products: "ARRAY['EQ_CNC']"),
            "symbol" => Raw(account, 6, effective, allow: "'[\"infy\"]'"),
            _ => Raw(account, 6, effective, actor: "system:x"),
        };

        (await LimitsDb.RefusalAsync(db.App, sql)).ShouldStartWith("23514");
    }

    [Fact]
    public async Task Only_the_runtime_role_writes_a_sheet_and_the_execution_role_only_reads_it()
    {
        var account = await _db.AccountAsync(mode: null);

        (await LimitsDb.RefusalAsync(db.Exec, Raw(account, 6, "now() + interval '10 days'"))).ShouldStartWith("42501");
        (await ExecDatabase.SqlStateAsync(db.Exec, "SELECT count(*) FROM execution.limits")).ShouldBeNull();
        (await ExecDatabase.SqlStateAsync(db.App, "UPDATE execution.limits SET reason = 'x'")).ShouldBe("42501");
        (await ExecDatabase.SqlStateAsync(db.App, "DELETE FROM execution.limits")).ShouldBe("42501");
    }
}
