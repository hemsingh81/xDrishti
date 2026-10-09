using XDrishti.Domain.Platform;

namespace XDrishti.Domain.Tests.Platform;

public sealed class ServiceHeartbeatTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 8, 9, 15, 0, TimeSpan.Zero);

    [Fact]
    public void Start_sets_started_and_last_seen_to_now()
    {
        var beat = ServiceHeartbeat.Start("xd-worker", "host-1", "0.1.0", T0);

        beat.ServiceName.ShouldBe("xd-worker");
        beat.StartedAt.ShouldBe(T0);
        beat.LastSeenAt.ShouldBe(T0);
    }

    [Fact]
    public void Beat_from_same_instance_only_moves_last_seen()
    {
        var beat = ServiceHeartbeat.Start("xd-worker", "host-1", "0.1.0", T0);

        beat.Beat("host-1", "0.1.0", T0.AddSeconds(15));

        beat.StartedAt.ShouldBe(T0);
        beat.LastSeenAt.ShouldBe(T0.AddSeconds(15));
    }

    [Fact]
    public void Beat_from_new_instance_is_treated_as_restart()
    {
        var beat = ServiceHeartbeat.Start("xd-worker", "host-1", "0.1.0", T0);

        beat.Beat("host-2", "0.2.0", T0.AddMinutes(5));

        beat.Instance.ShouldBe("host-2");
        beat.Version.ShouldBe("0.2.0");
        beat.StartedAt.ShouldBe(T0.AddMinutes(5));
    }

    [Fact]
    public void Out_of_order_beat_is_ignored()
    {
        var beat = ServiceHeartbeat.Start("xd-worker", "host-1", "0.1.0", T0.AddMinutes(1));

        beat.Beat("host-1", "0.1.0", T0);

        beat.LastSeenAt.ShouldBe(T0.AddMinutes(1));
    }

    [Theory]
    [InlineData(59, true)]
    [InlineData(60, true)]
    [InlineData(61, false)]
    public void IsAlive_respects_tolerance(int secondsLater, bool expected)
    {
        var beat = ServiceHeartbeat.Start("xd-worker", "host-1", "0.1.0", T0);

        beat.IsAlive(T0.AddSeconds(secondsLater), TimeSpan.FromSeconds(60)).ShouldBe(expected);
    }

    [Theory]
    [InlineData("", "host", "1")]
    [InlineData("svc", " ", "1")]
    [InlineData("svc", "host", "")]
    public void Start_rejects_missing_values(string name, string instance, string version) =>
        Should.Throw<ArgumentException>(() => ServiceHeartbeat.Start(name, instance, version, T0));
}
