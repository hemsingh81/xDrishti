using XDrishti.Application.Abstractions.Platform;

namespace XDrishti.Application.Tests.Platform.Services;

internal sealed class FakeApplicationInfo : IApplicationInfo
{
    public string Name => "xd-test";

    public string Version => "0.0.0";

    public string Environment => "Testing";

    public string Instance => "test";

    /// <summary>When the (fake) process started: runs left running from before it are interrupted.</summary>
    public DateTimeOffset StartedAt { get; set; } = new(2026, 10, 12, 10, 0, 0, TimeSpan.FromMinutes(330));
}
