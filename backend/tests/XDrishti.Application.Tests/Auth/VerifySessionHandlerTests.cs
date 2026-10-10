using XDrishti.Application.Auth;
using XDrishti.Domain.Auth;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Tests.Auth;

public sealed class VerifySessionHandlerTests
{
    private readonly AuthFixture _fx = new();

    private ValueTask<Result<VerifiedSession>> VerifyAsync(string token) =>
        _fx.Verify().HandleAsync(new VerifySessionQuery(token, AuthFixture.Client), CancellationToken.None);

    private async Task<string> SignInAsync()
    {
        _fx.SeedOwner();
        return (await _fx.SignInAsync()).SessionToken;
    }

    [Fact]
    public async Task A_fresh_session_is_valid_and_returns_the_csrf_token()
    {
        var token = await SignInAsync();

        var result = await VerifyAsync(token);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Info.CsrfToken.ShouldBe("csrf-of-" + token);
        result.Value.Info.User.Email.ShouldBe(AuthFixture.OwnerEmail);
        result.Value.SessionId.ShouldBe(_fx.Data.Sessions.Single().Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-such-token")]
    public async Task Unknown_or_empty_tokens_are_unauthenticated(string token)
    {
        await SignInAsync();

        var result = await VerifyAsync(token);

        result.Error.ShouldBe(AuthErrors.Unauthenticated);
    }

    [Fact]
    public async Task Idle_for_thirty_minutes_ends_the_session_and_is_audited()
    {
        var token = await SignInAsync();

        _fx.Clock.Advance(TimeSpan.FromMinutes(30));
        var result = await VerifyAsync(token);

        result.Error.ShouldBe(AuthErrors.SessionExpired);
        _fx.Data.Sessions.Single().RevokedAt.ShouldNotBeNull();
        _fx.Data.Events.Last().Event.ShouldBe(AuditEventType.SessionExpiredIdle);
        (await VerifyAsync(token)).Error.ShouldBe(AuthErrors.Unauthenticated); // a revoked session is simply gone
    }

    [Fact]
    public async Task Activity_keeps_the_session_alive_beyond_thirty_minutes()
    {
        var token = await SignInAsync();

        for (var i = 0; i < 4; i++)
        {
            _fx.Clock.Advance(TimeSpan.FromMinutes(20));
            (await VerifyAsync(token)).IsSuccess.ShouldBeTrue();
        }
    }

    [Fact]
    public async Task The_absolute_lifetime_ends_an_active_session_and_is_audited()
    {
        var token = await SignInAsync();
        for (var i = 0; i < 35; i++) // active every 20 minutes until 11 h 40
        {
            _fx.Clock.Advance(TimeSpan.FromMinutes(20));
            (await VerifyAsync(token)).IsSuccess.ShouldBeTrue();
        }

        _fx.Clock.Advance(TimeSpan.FromMinutes(20)); // 12 h after sign-in
        var result = await VerifyAsync(token);

        result.Error.ShouldBe(AuthErrors.SessionExpired);
        _fx.Data.Events.Last().Event.ShouldBe(AuditEventType.SessionExpiredAbsolute);
    }

    [Fact]
    public async Task Last_seen_is_written_at_most_once_a_minute()
    {
        var token = await SignInAsync();
        var savesAfterLogin = _fx.Data.SaveCount;

        _fx.Clock.Advance(TimeSpan.FromSeconds(30));
        await VerifyAsync(token);
        _fx.Data.SaveCount.ShouldBe(savesAfterLogin);

        _fx.Clock.Advance(TimeSpan.FromSeconds(30));
        await VerifyAsync(token);
        _fx.Data.SaveCount.ShouldBe(savesAfterLogin + 1);
        _fx.Data.Sessions.Single().LastSeenAt.ShouldBe(AuthFixture.Start.AddMinutes(1));
    }

    [Fact]
    public async Task A_revoked_session_is_unauthenticated()
    {
        var token = await SignInAsync();
        _fx.Data.Sessions.Single().Revoke(_fx.Clock.GetUtcNow());

        var result = await VerifyAsync(token);

        result.Error.ShouldBe(AuthErrors.Unauthenticated);
    }
}
