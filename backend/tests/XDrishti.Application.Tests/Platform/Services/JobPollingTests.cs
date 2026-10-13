using XDrishti.Application.Platform.Services;

namespace XDrishti.Application.Tests.Platform.Services;

public sealed class JobPollingTests
{
    [Fact]
    public void The_wait_starts_short_doubles_and_never_exceeds_five_seconds()
    {
        var waits = new List<TimeSpan> { JobPolling.FirstWait };
        for (var i = 0; i < 8; i++)
        {
            waits.Add(JobPolling.Next(waits[^1]));
        }

        waits.Select(w => w.TotalMilliseconds).ShouldBe([100d, 200, 400, 800, 1600, 3200, 5000, 5000, 5000]);
    }
}
