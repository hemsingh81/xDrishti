#pragma warning disable CA2012 // NSubstitute setup calls on ValueTask members are not awaited
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Capabilities;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.OwnerActs;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Capabilities;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Tests.OwnerActs;

/// <summary>Spec P8-19 (ADR 0130): the runner asks the gate again, calls the one handler it is given only when the capability is ON, and writes the audit row only after a success; a failed audit write never undoes the move.</summary>
public sealed class OwnerActRunnerTests
{
    private const string Owner = "owner:test@example.com";

    public sealed record Ping(string Reason) : ICommand;

    private readonly ICapabilityGate _gate = Substitute.For<ICapabilityGate>();

    private readonly IAccountEventLog _events = Substitute.For<IAccountEventLog>();

    private readonly IAccountUnitOfWork _unit = Substitute.For<IAccountUnitOfWork>();

    private readonly ICommandHandler<Ping, int> _handler = Substitute.For<ICommandHandler<Ping, int>>();

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 26, 6, 0, 0, TimeSpan.Zero));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private OwnerActRunner Runner() => new(_gate, _events, _unit, _time, NullLogger<OwnerActRunner>.Instance);

    private void GateIs(bool on) => _gate.RequireAsync(Domain.Capabilities.Capabilities.LearningMovesUi, Ct).Returns(on ? Result.Success() : Result.Failure(CapabilityErrors.Off(Domain.Capabilities.Capabilities.LearningMovesUi)));

    private ValueTask<Result<int>> RunAsync() =>
        Runner().RunAsync(Domain.Capabilities.Capabilities.LearningMovesUi, _handler, new Ping("a free-text reason that must never be copied"), Owner, AccountEventType.LessonMoved, n => $"lesson={n} to=linked", Ct);

    [Fact]
    public async Task With_the_capability_off_nothing_is_called_and_nothing_is_recorded()
    {
        GateIs(false);

        var result = await RunAsync();

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("capabilities.off");
        await _handler.DidNotReceiveWithAnyArgs().HandleAsync(default!, Ct);
        _events.DidNotReceiveWithAnyArgs().Record(default!);
        await _unit.DidNotReceiveWithAnyArgs().TrySaveChangesAsync(Ct);
    }

    [Fact]
    public async Task A_success_records_one_event_with_ids_and_state_words_only_and_the_actor()
    {
        GateIs(true);
        _handler.HandleAsync(Arg.Any<Ping>(), Ct).Returns(Result.Success(12));
        _unit.TrySaveChangesAsync(Ct).Returns(true);
        AccountEvent? recorded = null;
        _events.When(e => e.Record(Arg.Any<AccountEvent>())).Do(c => recorded = c.Arg<AccountEvent>());

        var result = await RunAsync();

        result.Value.ShouldBe(12);
        recorded.ShouldNotBeNull();
        (recorded.Event, recorded.Detail, recorded.Actor).ShouldBe((AccountEventType.LessonMoved, "lesson=12 to=linked", Owner));
        (recorded.Detail ?? string.Empty).ShouldNotContain("reason");
    }

    [Fact]
    public async Task A_refused_move_records_nothing()
    {
        GateIs(true);
        _handler.HandleAsync(Arg.Any<Ping>(), Ct).Returns(Result.Failure<int>(Error.Conflict("lessons.move_refused", "closed is final")));

        var result = await RunAsync();

        result.Error.Code.ShouldBe("lessons.move_refused");
        _events.DidNotReceiveWithAnyArgs().Record(default!);
    }

    [Fact]
    public async Task When_the_audit_write_fails_the_move_stands_and_the_answer_is_still_the_success()
    {
        GateIs(true);
        _handler.HandleAsync(Arg.Any<Ping>(), Ct).Returns(Result.Success(3));
        _unit.TrySaveChangesAsync(Ct).Returns(false);

        (await RunAsync()).Value.ShouldBe(3);

        _unit.TrySaveChangesAsync(Ct).Returns<bool>(_ => throw new InvalidOperationException("database down"));
        (await RunAsync()).Value.ShouldBe(3);
    }
}
