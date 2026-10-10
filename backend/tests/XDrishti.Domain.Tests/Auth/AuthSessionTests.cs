using XDrishti.Domain.Auth;

namespace XDrishti.Domain.Tests.Auth;

public sealed class AuthSessionTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 10, 9, 0, 0, TimeSpan.Zero);

    private static AuthSession NewSession() => AuthSession.Start(Guid.NewGuid(), "token-hash", T0, "203.0.113.7", "test-agent");

    [Fact]
    public void Start_sets_times_and_the_absolute_expiry()
    {
        var session = NewSession();

        session.CreatedAt.ShouldBe(T0);
        session.LastSeenAt.ShouldBe(T0);
        session.ExpiresAt.ShouldBe(T0.AddHours(12));
        session.RevokedAt.ShouldBeNull();
        session.Evaluate(T0).ShouldBe(SessionStatus.Active);
    }

    [Theory]
    [InlineData(29, SessionStatus.Active)]
    [InlineData(30, SessionStatus.IdleExpired)]
    [InlineData(31, SessionStatus.IdleExpired)]
    public void Idle_timeout_is_thirty_minutes_without_activity(int minutes, SessionStatus expected) =>
        NewSession().Evaluate(T0.AddMinutes(minutes)).ShouldBe(expected);

    [Fact]
    public void Activity_slides_the_idle_window()
    {
        var session = NewSession();

        session.TryTouch(T0.AddMinutes(20)).ShouldBeTrue();

        session.Evaluate(T0.AddMinutes(49)).ShouldBe(SessionStatus.Active);
        session.Evaluate(T0.AddMinutes(50)).ShouldBe(SessionStatus.IdleExpired);
    }

    [Fact]
    public void Absolute_lifetime_ends_the_session_even_when_active()
    {
        var session = NewSession();
        session.TryTouch(T0.AddHours(11).AddMinutes(50));

        session.Evaluate(T0.AddHours(11).AddMinutes(59)).ShouldBe(SessionStatus.Active);
        session.Evaluate(T0.AddHours(12)).ShouldBe(SessionStatus.AbsoluteExpired);
    }

    [Fact]
    public void Absolute_expiry_wins_over_idle_expiry()
    {
        NewSession().Evaluate(T0.AddHours(13)).ShouldBe(SessionStatus.AbsoluteExpired);
    }

    [Theory]
    [InlineData(59, false)]
    [InlineData(60, true)]
    [InlineData(61, true)]
    public void Touch_is_throttled_to_once_a_minute(int seconds, bool expected)
    {
        var session = NewSession();

        session.TryTouch(T0.AddSeconds(seconds)).ShouldBe(expected);

        session.LastSeenAt.ShouldBe(expected ? T0.AddSeconds(seconds) : T0);
    }

    [Fact]
    public void Revoke_ends_the_session_and_is_idempotent()
    {
        var session = NewSession();

        session.Revoke(T0.AddMinutes(1));
        session.Revoke(T0.AddMinutes(2));

        session.RevokedAt.ShouldBe(T0.AddMinutes(1));
        session.Evaluate(T0.AddMinutes(1)).ShouldBe(SessionStatus.Revoked);
    }
}
