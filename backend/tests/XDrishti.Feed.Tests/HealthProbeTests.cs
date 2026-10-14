using Microsoft.Extensions.Configuration;

namespace XDrishti.Feed.Tests;

/// <summary>The container health check: the exit code is 0 only for a heartbeat younger than 60 seconds.</summary>
public sealed class HealthProbeTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(14)]
    [InlineData(59)]
    public void A_heartbeat_younger_than_a_minute_is_healthy_in_or_out_of_the_window(int seconds) =>
        HealthProbe.ExitCode(TimeSpan.FromSeconds(seconds)).ShouldBe(0);

    [Theory]
    [InlineData(60)]
    [InlineData(300)]
    public void A_stale_heartbeat_is_unhealthy(int seconds) =>
        HealthProbe.ExitCode(TimeSpan.FromSeconds(seconds)).ShouldBe(1);

    [Fact]
    public void No_heartbeat_at_all_is_unhealthy() =>
        HealthProbe.ExitCode(null).ShouldBe(1);

    [Fact]
    public async Task A_database_that_cannot_be_reached_is_unhealthy_and_does_not_throw()
    {
        var exit = await HealthProbe.RunAsync(builder => builder.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:ConnectionString"] = "Host=127.0.0.1;Port=1;Database=xdrishti;Username=xd_app;Timeout=2",
            ["Database:Password"] = "not-a-real-password",
        }));

        exit.ShouldBe(1);
    }

    [Fact]
    public async Task A_missing_connection_string_is_unhealthy_and_does_not_throw() =>
        (await HealthProbe.RunAsync(builder => builder.AddInMemoryCollection(new Dictionary<string, string?> { ["Database:ConnectionString"] = null }))).ShouldBe(1);
}
