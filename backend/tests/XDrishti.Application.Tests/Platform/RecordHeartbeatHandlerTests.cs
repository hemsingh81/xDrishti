using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Platform.Heartbeats;
using XDrishti.Domain.Common;
using XDrishti.Domain.Platform;

namespace XDrishti.Application.Tests.Platform;

public sealed class RecordHeartbeatHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 10, 0, 0, TimeSpan.Zero);
    private readonly IServiceHeartbeatStore _store = Substitute.For<IServiceHeartbeatStore>();
    private readonly FakeTimeProvider _clock = new(Now);

    [Fact]
    public async Task First_beat_adds_a_heartbeat()
    {
        var result = await new RecordHeartbeatHandler(_store, _clock)
            .HandleAsync(new RecordHeartbeatCommand("xd-worker", "w1", "0.1.0"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        _store.Received(1).Add(Arg.Is<ServiceHeartbeat>(h => h.ServiceName == "xd-worker" && h.LastSeenAt == Now));
        await _store.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Next_beat_updates_the_existing_heartbeat()
    {
        var existing = ServiceHeartbeat.Start("xd-worker", "w1", "0.1.0", Now.AddSeconds(-15));
        _store.FindAsync("xd-worker", Arg.Any<CancellationToken>()).Returns(existing);

        await new RecordHeartbeatHandler(_store, _clock)
            .HandleAsync(new RecordHeartbeatCommand("xd-worker", "w1", "0.1.0"), CancellationToken.None);

        existing.LastSeenAt.ShouldBe(Now);
        _store.DidNotReceive().Add(Arg.Any<ServiceHeartbeat>());
    }

    [Fact]
    public async Task Missing_service_name_is_a_validation_error()
    {
        var result = await new RecordHeartbeatHandler(_store, _clock)
            .HandleAsync(new RecordHeartbeatCommand(" ", "w1", "0.1.0"), CancellationToken.None);

        result.Error.Kind.ShouldBe(ErrorKind.Validation);
        await _store.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
