using XDrishti.Execution.Application;
using XDrishti.Execution.Application.Halts;
using XDrishti.Execution.Application.Ports;

namespace XDrishti.Execution.Tests.Halts;

internal sealed class MemoryCommands : IExecCommandStore
{
    private readonly List<ExecCommand> _pending = [];

    public List<(string Result, string Code)> Answers { get; } = [];

    public void Add(ExecCommand command) => _pending.Add(command);

    public Task<IReadOnlyList<ExecCommand>> PendingAsync(int max, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ExecCommand>>([.. _pending.Take(max)]);

    public Task<bool> AnswerAsync(Guid commandId, string result, string code, CancellationToken cancellationToken)
    {
        if (_pending.RemoveAll(c => c.Id == commandId) == 0)
        {
            return Task.FromResult(false);
        }

        Answers.Add((result, code));
        return Task.FromResult(true);
    }
}

/// <summary>P12-05 criterion 7: the owner's button as a command. It only records a halt, validates everything, refuses a bad one and keeps trying when the record cannot be written.</summary>
public sealed class HaltCommandTests
{
    private static readonly Guid Account = Guid.NewGuid();

    private static ExecCommand Halt(string payload, Guid? account, string actor = "owner:hem@example.test") => new(Guid.NewGuid(), "halt", account, payload, actor, DateTimeOffset.UnixEpoch);

    [Fact]
    public async Task A_valid_account_halt_and_a_valid_system_halt_are_recorded_with_the_owner_actor_and_the_kill_switch_choice()
    {
        var store = new InMemoryHaltStore();
        var commands = new MemoryCommands();
        commands.Add(Halt("""{"scope":"account","level":"exit_all","reason":"gap","brokerKillSwitch":true}""", Account));
        commands.Add(Halt("""{"scope":"system","level":"entries","reason":"news"}""", null));

        await new ExecCommandProcessor(commands, new HaltService(store, TimeProvider.System)).RunOnceAsync(CancellationToken.None);

        var halts = await store.ActiveAsync(CancellationToken.None);
        halts.Select(h => (h.Scope, h.Level, h.BrokerKillSwitch, h.Actor)).ShouldBe([(HaltScope.Account, HaltLevel.ExitAll, true, "owner:hem@example.test"), (HaltScope.System, HaltLevel.Entries, false, "owner:hem@example.test")]);
    }

    [Theory]
    [InlineData("""{"scope":"account","level":"freeze","reason":"x"}""", "00000000-0000-0000-0000-000000000001", "owner:hem@example.test")]
    [InlineData("""{"scope":"account","level":"cancel","reason":"x"}""", null, "owner:hem@example.test")]
    [InlineData("""{"scope":"system","level":"cancel","reason":"x"}""", "00000000-0000-0000-0000-000000000001", "owner:hem@example.test")]
    [InlineData("""{"scope":"system","level":"cancel","reason":"   "}""", null, "owner:hem@example.test")]
    [InlineData("""{"scope":"system","level":"cancel","reason":"x"}""", null, "system:halts")]
    [InlineData("""{"scope":"system","level":"none","reason":"x"}""", null, "owner:hem@example.test")]
    [InlineData("not json", null, "owner:hem@example.test")]
    public async Task A_bad_halt_command_is_refused_and_records_nothing(string payload, string? account, string actor)
    {
        var store = new InMemoryHaltStore();
        var commands = new MemoryCommands();
        commands.Add(Halt(payload, account is null ? null : Guid.Parse(account), actor));

        await new ExecCommandProcessor(commands, new HaltService(store, TimeProvider.System)).RunOnceAsync(CancellationToken.None);

        store.Rows.ShouldBe(0);
        commands.Answers.Single().ShouldBe(("refused", "execution.halt_invalid"));
    }

    [Fact]
    public async Task A_halt_whose_record_cannot_be_written_is_not_answered_and_is_tried_again_until_it_lands()
    {
        var store = new InMemoryHaltStore { Unreadable = true };
        var commands = new MemoryCommands();
        commands.Add(Halt("""{"scope":"system","level":"cancel","reason":"stop"}""", null));
        var processor = new ExecCommandProcessor(commands, new HaltService(store, TimeProvider.System));

        (await processor.RunOnceAsync(CancellationToken.None)).ShouldBe(0);
        commands.Answers.ShouldBeEmpty();

        store.Unreadable = false;
        (await processor.RunOnceAsync(CancellationToken.None)).ShouldBe(1);
        store.Rows.ShouldBe(1);
    }

    [Fact]
    public async Task Without_a_halt_service_the_halt_kind_is_unknown_as_before_and_ping_still_works()
    {
        var commands = new MemoryCommands();
        commands.Add(Halt("""{"scope":"system","level":"cancel","reason":"x"}""", null));
        commands.Add(new ExecCommand(Guid.NewGuid(), "ping", null, "{}", "owner:t", DateTimeOffset.UnixEpoch));

        await new ExecCommandProcessor(commands).RunOnceAsync(CancellationToken.None);

        commands.Answers.ShouldBe([("refused", "execution.command_unknown"), ("done", "execution.ok")]);
    }

    [Fact]
    public async Task A_halt_that_keeps_failing_backs_off_2_4_8_seconds_and_is_never_given_up()
    {
        var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        var store = new InMemoryHaltStore { Unreadable = true };
        var commands = new MemoryCommands();
        commands.Add(Halt("""{"scope":"system","level":"cancel","reason":"stop"}""", null));
        var processor = new ExecCommandProcessor(commands, new HaltService(store, clock), clock);

        await processor.RunOnceAsync(CancellationToken.None); // failure 1: retried at once
        await processor.RunOnceAsync(CancellationToken.None); // failure 2: next in 2 s
        store.Unreadable = false;
        (await processor.RunOnceAsync(CancellationToken.None)).ShouldBe(0, "still backing off");
        clock.Advance(TimeSpan.FromSeconds(2));

        (await processor.RunOnceAsync(CancellationToken.None)).ShouldBe(1);
        store.Rows.ShouldBe(1);
    }
}
