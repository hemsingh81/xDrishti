using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain.Limits;

namespace XDrishti.Execution.Tests.Limits;

/// <summary>Spec P12-09 criterion 7: attended is an owner session seen within the window by its last_seen_at and not locked; the presence port of this slice is the closed default (no grant on auth.sessions), which is never attended.</summary>
public sealed class OwnerPresenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 10, 0, 0, TimeSpan.FromMinutes(330));

    [Theory]
    [InlineData(0, 10, true)]
    [InlineData(600, 10, true)]
    [InlineData(601, 10, false)]
    [InlineData(1800, 30, true)]
    [InlineData(61, 1, false)]
    [InlineData(-1, 10, true)]
    [InlineData(-2, 10, false)]
    public void A_session_counts_only_while_seen_within_the_attended_minutes(int secondsAgo, int minutes, bool attended)
    {
        OwnerPresence.IsAttended(new OwnerSeen(Now.AddSeconds(-secondsAgo), false), Now, minutes).ShouldBe(attended);
    }

    [Fact]
    public void A_locked_owner_or_no_session_is_not_attended()
    {
        OwnerPresence.IsAttended(new OwnerSeen(Now, true), Now, 10).ShouldBeFalse();
        OwnerPresence.IsAttended(null, Now, 10).ShouldBeFalse();
        OwnerPresence.IsAttended(new OwnerSeen(Now, false), Now, 0).ShouldBeFalse();
    }

    [Fact]
    public async Task The_closed_presence_source_answers_nothing()
    {
        (await new ClosedOwnerPresence().NewestAsync(TestContext.Current.CancellationToken)).ShouldBeNull();
    }
}
