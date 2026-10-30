using Microsoft.Extensions.Logging.Abstractions;
using XDrishti.Application.Auth;
using XDrishti.Application.Execution;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Tests.Execution;

/// <summary>P12-05 criterion 7 at the use-case level, on fakes: starting is cheap (no password, exit_all needs the step-up window), releasing needs the owner, a reason and every rule, and all reasons come back together.</summary>
public sealed class ExecutionHaltHandlerTests
{
    private const string Owner = "owner:hem@example.invalid";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Guid Account = Guid.NewGuid();

    private sealed class Commands : IExecutionCommands
    {
        public List<(string Kind, Guid? Account, string Payload, string Actor)> Rows { get; } = [];

        public Task<Guid> EnqueueAsync(string kind, Guid? accountId, string payloadJson, string actor, CancellationToken cancellationToken)
        {
            Rows.Add((kind, accountId, payloadJson, actor));
            return Task.FromResult(Guid.NewGuid());
        }
    }

    private sealed class Halts(ExecutionHaltView halt, bool positions = false, bool alarms = false) : IExecutionHaltReader, IExecutionHaltReleases
    {
        public List<(long Id, string Actor, string Reason)> Released { get; } = [];

        public string? DatabaseRefusal { get; set; }

        public Task<IReadOnlyList<ExecutionHaltView>> ReadAsync(bool active, Guid? accountId, int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ExecutionHaltView>>([halt]);

        public Task<HaltReleaseFacts?> FindAsync(long id, CancellationToken cancellationToken) => Task.FromResult(id == halt.Id ? new HaltReleaseFacts(Released.Count > 0 ? halt with { End = new ExecutionHaltEnd(DateTimeOffset.UnixEpoch, Owner, "owner", "r") } : halt, positions, alarms) : null);

        public Task<string?> ReleaseAsync(long haltId, string actor, string reason, CancellationToken cancellationToken)
        {
            if (DatabaseRefusal is null)
            {
                Released.Add((haltId, actor, reason));
            }

            return Task.FromResult(DatabaseRefusal);
        }
    }

    private static ExecutionHaltView Halt(string level = "cancel", string trigger = "owner_button") => new(7, DateTimeOffset.UnixEpoch, "account", Account, level, trigger, "owner", Owner, "stop", null);

    private static StartExecutionHaltCommand Start(string? level = "cancel", string scope = "account", Guid? account = null, string? reason = "gap down", bool stepUp = false, string actor = Owner) =>
        new(actor, scope, scope == "account" ? account ?? Account : account, level ?? string.Empty, reason, false, stepUp);

    [Fact]
    public async Task Starting_a_halt_leaves_one_command_row_with_the_owner_actor_and_no_password_and_a_cancel_needs_no_step_up()
    {
        var commands = new Commands();

        var result = await new StartExecutionHaltHandler(commands, NullLogger<StartExecutionHaltHandler>.Instance).HandleAsync(Start(), Ct);

        result.IsSuccess.ShouldBeTrue();
        var row = commands.Rows.Single();
        (row.Kind, row.Account, row.Actor).ShouldBe(("halt", Account, Owner));
        row.Payload.ShouldContain("\"level\":\"cancel\"");
        row.Payload.ShouldNotContain("password", Case.Insensitive);
    }

    [Fact]
    public async Task Exit_all_needs_the_step_up_window_because_it_realises_losses_and_with_it_it_goes_through()
    {
        var commands = new Commands();
        var handler = new StartExecutionHaltHandler(commands, NullLogger<StartExecutionHaltHandler>.Instance);

        var without = await handler.HandleAsync(Start("exit_all"), Ct);
        var with = await handler.HandleAsync(Start("exit_all", stepUp: true), Ct);

        without.Error.Code.ShouldBe(AuthErrors.StepUpRequiredCode);
        with.IsSuccess.ShouldBeTrue();
        commands.Rows.Count.ShouldBe(1);
    }

    [Theory]
    [InlineData("freeze", "account", "gap")]
    [InlineData("none", "account", "gap")]
    [InlineData("cancel", "account", "")]
    [InlineData("cancel", "account", "   ")]
    [InlineData("cancel", "account", "my token is here")]
    [InlineData("cancel", "galaxy", "gap")]
    public async Task A_bad_halt_is_refused_before_anything_is_written(string level, string scope, string reason)
    {
        var commands = new Commands();

        var result = await new StartExecutionHaltHandler(commands, NullLogger<StartExecutionHaltHandler>.Instance).HandleAsync(new StartExecutionHaltCommand(Owner, scope, scope == "account" ? Account : null, level, reason, false, true), Ct);

        result.IsFailure.ShouldBeTrue();
        commands.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_scope_that_disagrees_with_the_account_and_a_non_owner_actor_are_refused()
    {
        var commands = new Commands();
        var handler = new StartExecutionHaltHandler(commands, NullLogger<StartExecutionHaltHandler>.Instance);

        (await handler.HandleAsync(new StartExecutionHaltCommand(Owner, "account", null, "cancel", "r", false, false), Ct)).Error.Code.ShouldBe(ExecutionHaltErrors.Invalid);
        (await handler.HandleAsync(new StartExecutionHaltCommand(Owner, "system", Account, "cancel", "r", false, false), Ct)).Error.Code.ShouldBe(ExecutionHaltErrors.Invalid);
        (await handler.HandleAsync(Start(actor: "system:halts"), Ct)).Error.Code.ShouldBe("execution.owner_only");
        (await handler.HandleAsync(Start(actor: "owner:"), Ct)).Error.Code.ShouldBe("execution.owner_only");
        commands.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_release_writes_the_row_with_the_owner_actor_and_the_trimmed_reason()
    {
        var halts = new Halts(Halt());

        var result = await new ReleaseExecutionHaltHandler(halts, halts, NullLogger<ReleaseExecutionHaltHandler>.Instance).HandleAsync(new ReleaseExecutionHaltCommand(Owner, 7, "  resolved by hand  "), Ct);

        result.IsSuccess.ShouldBeTrue();
        halts.Released.Single().ShouldBe((7, Owner, "resolved by hand"));
        result.Value.End!.How.ShouldBe("owner");
    }

    [Fact]
    public async Task An_unknown_halt_is_404_and_a_refused_release_answers_every_reason_at_once_and_writes_nothing()
    {
        var halts = new Halts(Halt("exit_all", "protection_breach"), positions: true, alarms: true);
        var handler = new ReleaseExecutionHaltHandler(halts, halts, NullLogger<ReleaseExecutionHaltHandler>.Instance);

        (await handler.HandleAsync(new ReleaseExecutionHaltCommand(Owner, 99, "r"), Ct)).Error.Code.ShouldBe(ExecutionHaltErrors.Unknown);
        var refused = await handler.HandleAsync(new ReleaseExecutionHaltCommand("system:halts", 7, " "), Ct);

        refused.Error.Code.ShouldBe(ExecutionHaltErrors.ReleaseRefused);
        refused.Error.Kind.ShouldBe(ErrorKind.Conflict);
        foreach (var word in new[] { "owner_required", "reason_required", "exit_all_not_flat", "protection_unresolved" })
        {
            refused.Error.Message.ShouldContain(word);
        }

        halts.Released.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("freeze", "reconciliation_break", false, false, "x reviewed it", "freeze_needs_reconciliation")]
    [InlineData("entries", "drawdown", false, false, "too short", "drawdown_review_too_short")]
    [InlineData("exit_all", "owner_button", true, false, "flat soon", "exit_all_not_flat")]
    [InlineData("entries", "protection_breach", false, true, "fixed it", "protection_unresolved")]
    [InlineData("entries", "oversold", false, true, "fixed it", "protection_unresolved")]
    public void Each_release_rule_refuses_on_its_own(string level, string trigger, bool positions, bool alarms, string reason, string expected) =>
        HaltReleaseRules.Refusals(new HaltReleaseFacts(Halt(level, trigger), positions, alarms), Owner, reason).ShouldBe([expected]);

    [Fact]
    public void A_clean_halt_with_a_reason_and_the_owner_has_no_refusal_and_a_halt_that_already_ended_is_refused()
    {
        HaltReleaseRules.Refusals(new HaltReleaseFacts(Halt("entries", "daily_loss"), false, false), Owner, "reviewed").ShouldBeEmpty();
        HaltReleaseRules.Refusals(new HaltReleaseFacts(Halt("entries", "drawdown"), false, false), Owner, "I have written the review of this week").ShouldBeEmpty();
        HaltReleaseRules.Refusals(new HaltReleaseFacts(Halt() with { End = new ExecutionHaltEnd(DateTimeOffset.UnixEpoch, "system:halts", "auto", null) }, false, false), Owner, "r").ShouldBe(["already_ended"]);
    }

    [Fact]
    public async Task A_refusal_of_the_database_comes_back_as_a_conflict_with_its_words()
    {
        var halts = new Halts(Halt("entries", "daily_loss")) { DatabaseRefusal = "the halt is already cleared" };

        var result = await new ReleaseExecutionHaltHandler(halts, halts, NullLogger<ReleaseExecutionHaltHandler>.Instance).HandleAsync(new ReleaseExecutionHaltCommand(Owner, 7, "r"), Ct);

        result.Error.Code.ShouldBe(ExecutionHaltErrors.ReleaseRefused);
        result.Error.Message.ShouldContain("already cleared");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(201)]
    public async Task The_halts_query_takes_1_to_200(int take)
    {
        var halts = new Halts(Halt());

        (await new GetExecutionHaltsHandler(halts).HandleAsync(new GetExecutionHaltsQuery(false, null, take), Ct)).IsFailure.ShouldBeTrue();
        (await new GetExecutionHaltsHandler(halts).HandleAsync(new GetExecutionHaltsQuery(false, null, 200), Ct)).IsSuccess.ShouldBeTrue();
    }
}
