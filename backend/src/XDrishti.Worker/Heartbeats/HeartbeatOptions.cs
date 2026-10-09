using System.ComponentModel.DataAnnotations;

namespace XDrishti.Worker.Heartbeats;

public sealed class HeartbeatOptions
{
    public const string SectionName = "Heartbeat";

    [Range(typeof(TimeSpan), "00:00:01", "00:10:00")]
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(15);
}
