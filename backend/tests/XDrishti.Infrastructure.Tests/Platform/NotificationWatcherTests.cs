using XDrishti.Infrastructure.Platform;

namespace XDrishti.Infrastructure.Tests.Platform;

/// <summary>The wait before the listener on the feed's channels tries the database again (ADR 0024 §6): short at first, growing, never more than half a minute.</summary>
public sealed class NotificationWatcherTests
{
    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 4)]
    [InlineData(3, 8)]
    [InlineData(4, 16)]
    [InlineData(5, 30)]
    [InlineData(40, 30)]
    public void The_wait_after_failed_attempts_doubles_up_to_thirty_seconds(int failures, int seconds)
    {
        NotificationWatcher.Backoff(failures).ShouldBe(TimeSpan.FromSeconds(seconds));
    }
}
