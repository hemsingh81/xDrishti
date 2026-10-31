using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using XDrishti.Execution.Application;
using XDrishti.Execution.Application.Halts;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Application.Recon;
using XDrishti.Execution.Domain;
using XDrishti.Execution.Domain.Recon;
using XDrishti.Execution.Host;

namespace XDrishti.Execution.Tests.Halts;

/// <summary>
/// ADR 0174: the owner's drill command. It runs the P12-05 drill over the sandbox book (read only) and our record, refuses on anything that is not provably flat or on an unreadable book, never touches the broker's order calls, answers why, and
/// is composed only in the sandbox send composition.
/// </summary>
public sealed class DrillRunnerTests
{
    private static readonly Guid Account = Guid.NewGuid();
    private const string Owner = "owner:hem@example.test";

    private sealed class World
    {
        public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 26, 6, 0, 0, TimeSpan.Zero));

        public FakeBroker Broker { get; }

        public StaticReconSource Ours { get; } = new();

        public InMemoryHaltStore Halts { get; } = new();

        public InMemoryDrillStore Drills { get; } = new();

        public World() => Broker = new FakeBroker(Clock);

        public DrillRunner Runner() => new(new HaltService(Halts, Clock), Halts, Broker, Ours, Drills, Clock);

        public async Task<(string Result, string Code)> CommandAsync(ExecCommand command, bool composed = true)
        {
            var commands = new MemoryCommands();
            commands.Add(command);
            await new ExecCommandProcessor(commands, new HaltService(Halts, Clock), Clock, drills: composed ? Runner() : null).RunOnceAsync(CancellationToken.None);
            return commands.Answers.Single();
        }
    }

    private static ExecCommand Drill(Guid? account = null, string actor = Owner) => new(Guid.NewGuid(), "drill", account ?? Account, "{}", actor, DateTimeOffset.UnixEpoch);

    [Fact]
    public async Task On_a_flat_sandbox_book_the_drill_passes_clears_its_own_halt_stores_one_row_and_never_reaches_the_broker()
    {
        var world = new World();

        var answer = await world.CommandAsync(Drill());

        answer.ShouldBe(("done", "execution.drill_passed"));
        var row = world.Drills.Rows.ShouldHaveSingleItem();
        row.Passed.ShouldBeTrue(row.Detail);
        row.Detail.ShouldContain("transport_calls=0");
        (await world.Halts.ActiveAsync(CancellationToken.None)).ShouldBeEmpty();
        world.Halts.AutoClears.Single().Code.ShouldBe("drill");
        (await world.Broker.GetOrderBookAsync(CancellationToken.None)).Value.ShouldBeEmpty("nothing was placed at the broker");
    }

    [Fact]
    public async Task A_position_at_the_broker_refuses_the_drill_with_not_flat_and_changes_nothing()
    {
        var world = new World();
        world.Broker.ShiftPosition("3426", 10);

        var answer = await world.CommandAsync(Drill());

        answer.ShouldBe(("refused", "execution.drill_refused:not_flat"));
        world.Halts.Rows.ShouldBe(0);
        world.Drills.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_working_order_at_the_broker_or_an_open_intent_of_ours_refuses_the_drill()
    {
        var withOrder = new World();
        withOrder.Broker.AddManualOrder("B1", "3426", ExecSide.Buy, 10, 400m);
        var withIntent = new World();
        withIntent.Ours.Set(Account, new OurAccountView([], [], [], [new OurIntentView(7, Account, ExecState.Submitting, "3426", ExecSide.Buy, 10, 0, 390m, 400m)]));

        (await withOrder.CommandAsync(Drill())).ShouldBe(("refused", "execution.drill_refused:not_flat"));
        (await withIntent.CommandAsync(Drill())).ShouldBe(("refused", "execution.drill_refused:not_flat"));
    }

    [Theory]
    [InlineData(ExecState.Planned)]
    [InlineData(ExecState.Closed)]
    [InlineData(ExecState.Open)]
    public async Task Any_intent_not_in_the_final_set_of_the_open_intent_count_refuses_the_drill(ExecState state)
    {
        var world = new World();
        world.Ours.Set(Account, new OurAccountView([], [], [], [new OurIntentView(7, Account, state, "3426", ExecSide.Buy, 10, 0, 390m, 400m)]));

        (await world.CommandAsync(Drill())).ShouldBe(("refused", "execution.drill_refused:not_flat"));
    }

    [Theory]
    [InlineData(ExecState.Rejected)]
    [InlineData(ExecState.Cancelled)]
    [InlineData(ExecState.Failed)]
    [InlineData(ExecState.Reconciled)]
    public async Task An_intent_in_the_final_set_does_not_block_the_drill(ExecState state)
    {
        var world = new World();
        world.Ours.Set(Account, new OurAccountView([], [], [], [new OurIntentView(7, Account, state, "3426", ExecSide.Buy, 10, 0, 390m, 400m)]));

        (await world.CommandAsync(Drill())).ShouldBe(("done", "execution.drill_passed"));
    }

    [Fact]
    public async Task An_unreadable_book_or_record_is_never_flat_the_drill_refuses_with_no_snapshot()
    {
        var blindBroker = new World();
        blindBroker.Broker.TokenExpired = true;
        var blindRecord = new World();
        blindRecord.Ours.Fail = true;

        (await blindBroker.CommandAsync(Drill())).ShouldBe(("refused", "execution.drill_refused:no_snapshot"));
        (await blindRecord.CommandAsync(Drill())).ShouldBe(("refused", "execution.drill_refused:no_snapshot"));
        blindBroker.Halts.Rows.ShouldBe(0);
        blindRecord.Halts.Rows.ShouldBe(0);
    }

    [Fact]
    public async Task Another_halt_in_force_refuses_the_drill()
    {
        var world = new World();
        await world.Halts.AppendAsync(new HaltDraft(HaltScope.System, null, HaltLevel.Entries, HaltTriggers.ExchangeHalt, "system", "system:t", "x", DateTimeOffset.UnixEpoch), CancellationToken.None);

        (await world.CommandAsync(Drill())).ShouldBe(("refused", "execution.drill_refused:halt_active"));
        world.Halts.Rows.ShouldBe(1);
    }

    [Fact]
    public async Task Without_the_sandbox_composition_the_command_answers_unavailable_and_a_system_actor_or_a_missing_account_is_invalid()
    {
        var world = new World();

        (await world.CommandAsync(Drill(), composed: false)).ShouldBe(("refused", "execution.drill_unavailable"));
        (await world.CommandAsync(Drill(actor: "system:x"))).ShouldBe(("refused", "execution.drill_invalid"));
        (await world.CommandAsync(new ExecCommand(Guid.NewGuid(), "drill", null, "{}", Owner, DateTimeOffset.UnixEpoch))).ShouldBe(("refused", "execution.drill_invalid"));
        world.Halts.Rows.ShouldBe(0);
        world.Drills.Rows.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(null, null, false)]
    [InlineData("sandbox", null, false)]
    [InlineData("sandbox", "false", false)]
    [InlineData("live", "true", false)]
    [InlineData("sandbox", "true", true)]
    public void The_drill_is_registered_only_in_the_sandbox_send_composition(string? environment, string? sendEnabled, bool expected)
    {
        var values = new Dictionary<string, string?>();
        if (environment is not null)
        {
            values["Execution:Environment"] = environment;
        }

        if (sendEnabled is not null)
        {
            values["Execution:SendEnabled"] = sendEnabled;
        }

        var services = new ServiceCollection().AddLogging().AddExecutionRecord(new ConfigurationBuilder().AddInMemoryCollection(values).Build());

        services.Any(d => d.ServiceType == typeof(DrillRunner)).ShouldBe(expected);
        services.Any(d => d.ServiceType == typeof(DrillService)).ShouldBeFalse();
        services.Any(d => d.ServiceType == typeof(IDrillStore)).ShouldBe(expected);
    }
}
