using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Platform.SystemStatus;
using XDrishti.Domain.Platform;

namespace XDrishti.Application.Tests.Platform;

public sealed class GetSystemStatusHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 10, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(Now);
    private readonly IApplicationInfo _app = Substitute.For<IApplicationInfo>();
    private readonly IDatabaseInfoProvider _database = Substitute.For<IDatabaseInfoProvider>();
    private readonly IServiceHeartbeatStore _heartbeats = Substitute.For<IServiceHeartbeatStore>();

    public GetSystemStatusHandlerTests()
    {
        _app.Name.Returns("xd-api");
        _app.Version.Returns("0.1.0");
        _app.Environment.Returns("Test");
        _app.Instance.Returns("test-host");
        _app.StartedAt.Returns(Now.AddMinutes(-10));
    }

    private GetSystemStatusHandler CreateHandler() => new(_app, _database, _heartbeats, _clock);

    private static DatabaseInfo Connected(int pending = 0) => new(true, "18.6", "2.30.2", "20261008_Initial", pending, 1.2, null);

    [Fact]
    public async Task Healthy_when_database_connected_and_all_services_alive()
    {
        _database.GetAsync(Arg.Any<CancellationToken>()).Returns(Connected());
        _heartbeats.ListAsync(Arg.Any<CancellationToken>())
            .Returns([ServiceHeartbeat.Start("xd-worker", "w1", "0.1.0", Now.AddSeconds(-10))]);

        var status = await CreateHandler().HandleAsync(new GetSystemStatusQuery(), CancellationToken.None);

        status.IsHealthy.ShouldBeTrue();
        status.Application.UptimeSeconds.ShouldBe(600);
        status.Database.TimescaleVersion.ShouldBe("2.30.2");
        status.Services.ShouldHaveSingleItem().IsAlive.ShouldBeTrue();
    }

    [Fact]
    public async Task Unhealthy_when_a_service_stopped_reporting()
    {
        _database.GetAsync(Arg.Any<CancellationToken>()).Returns(Connected());
        _heartbeats.ListAsync(Arg.Any<CancellationToken>())
            .Returns([ServiceHeartbeat.Start("xd-worker", "w1", "0.1.0", Now.AddMinutes(-5))]);

        var status = await CreateHandler().HandleAsync(new GetSystemStatusQuery(), CancellationToken.None);

        status.IsHealthy.ShouldBeFalse();
        status.Services.ShouldHaveSingleItem().IsAlive.ShouldBeFalse();
    }

    private static ServiceHeartbeat Feed(string state, DateTimeOffset seen)
    {
        var beat = ServiceHeartbeat.Start("xd-feed", "f1", "0.1.0", seen.AddHours(-3));
        beat.Beat("f1", "0.1.0", seen, state, "{}");
        return beat;
    }

    [Fact]
    public async Task The_feed_is_listed_like_the_worker_and_is_up_while_it_beats_even_when_it_only_says_closed()
    {
        _database.GetAsync(Arg.Any<CancellationToken>()).Returns(Connected());
        _heartbeats.ListAsync(Arg.Any<CancellationToken>())
            .Returns([ServiceHeartbeat.Start("xd-worker", "w1", "0.1.0", Now.AddSeconds(-10)), Feed("closed", Now.AddSeconds(-12))]);

        var status = await CreateHandler().HandleAsync(new GetSystemStatusQuery(), CancellationToken.None);

        status.IsHealthy.ShouldBeTrue();
        status.Services.Select(s => (s.Name, s.IsAlive, s.State)).ShouldBe([("xd-worker", true, null), ("xd-feed", true, "closed")]);
    }

    [Fact]
    public async Task A_feed_that_stopped_reporting_without_saying_so_makes_the_system_unhealthy()
    {
        _database.GetAsync(Arg.Any<CancellationToken>()).Returns(Connected());
        _heartbeats.ListAsync(Arg.Any<CancellationToken>()).Returns([Feed("connected", Now.AddMinutes(-3))]);

        var status = await CreateHandler().HandleAsync(new GetSystemStatusQuery(), CancellationToken.None);

        status.IsHealthy.ShouldBeFalse();
        status.Services.ShouldHaveSingleItem().IsAlive.ShouldBeFalse();
    }

    [Theory]
    [InlineData(5)]
    [InlineData(600)] // ten minutes: the last moment of the grace
    public async Task A_feed_that_said_it_was_stopping_is_down_but_does_not_make_the_system_unhealthy_for_ten_minutes(int secondsAgo)
    {
        _database.GetAsync(Arg.Any<CancellationToken>()).Returns(Connected());
        _heartbeats.ListAsync(Arg.Any<CancellationToken>()).Returns([Feed("stopped", Now.AddSeconds(-secondsAgo))]);

        var status = await CreateHandler().HandleAsync(new GetSystemStatusQuery(), CancellationToken.None);

        status.IsHealthy.ShouldBeTrue();
        var feed = status.Services.ShouldHaveSingleItem();
        feed.IsAlive.ShouldBeFalse();
        feed.State.ShouldBe("stopped");
    }

    [Fact]
    public async Task A_stopped_feed_that_has_stayed_down_for_more_than_ten_minutes_is_down_like_any_stale_row()
    {
        _database.GetAsync(Arg.Any<CancellationToken>()).Returns(Connected());
        _heartbeats.ListAsync(Arg.Any<CancellationToken>()).Returns([Feed("stopped", Now.AddSeconds(-601))]);

        var status = await CreateHandler().HandleAsync(new GetSystemStatusQuery(), CancellationToken.None);

        status.IsHealthy.ShouldBeFalse();
        status.Services.ShouldHaveSingleItem().State.ShouldBe("stopped");
    }

    [Fact]
    public async Task Unhealthy_when_migrations_are_pending()
    {
        _database.GetAsync(Arg.Any<CancellationToken>()).Returns(Connected(pending: 1));
        _heartbeats.ListAsync(Arg.Any<CancellationToken>()).Returns([]);

        var status = await CreateHandler().HandleAsync(new GetSystemStatusQuery(), CancellationToken.None);

        status.IsHealthy.ShouldBeFalse();
    }

    [Fact]
    public async Task Does_not_query_services_when_database_is_down()
    {
        _database.GetAsync(Arg.Any<CancellationToken>()).Returns(new DatabaseInfo(false, null, null, null, 0, 3000, "NpgsqlException"));

        var status = await CreateHandler().HandleAsync(new GetSystemStatusQuery(), CancellationToken.None);

        status.IsHealthy.ShouldBeFalse();
        status.Services.ShouldBeEmpty();
        await _heartbeats.DidNotReceive().ListAsync(Arg.Any<CancellationToken>());
    }
}
