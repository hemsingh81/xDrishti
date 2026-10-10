using XDrishti.Application.Auth;
using XDrishti.Domain.Auth;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Tests.Auth;

public sealed class LoginHandlerTests
{
    private readonly AuthFixture _fx = new();

    private ValueTask<Result<LoginResult>> LoginAsync(string? email, string? password) =>
        _fx.Login().HandleAsync(new LoginCommand(email, password, AuthFixture.Client), CancellationToken.None);

    [Fact]
    public async Task Correct_credentials_start_a_session_and_return_the_session_info()
    {
        var owner = _fx.SeedOwner();

        var result = await LoginAsync("  Owner@Example.Invalid ", AuthFixture.OwnerPassword);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Session.User.ShouldBe(new UserInfo(AuthFixture.OwnerEmail, "The Owner"));
        result.Value.Session.CsrfToken.ShouldBe("csrf-of-" + result.Value.SessionToken);
        result.Value.Session.IdleTimeoutSeconds.ShouldBe(1800);
        result.Value.Session.ExpiresAt.ShouldBe(AuthFixture.Start.AddHours(12));
        var session = _fx.Data.Sessions.ShouldHaveSingleItem();
        session.OwnerId.ShouldBe(owner.Id);
        session.Ip.ShouldBe("203.0.113.7");
    }

    [Fact]
    public async Task Only_a_hash_of_the_session_token_is_stored()
    {
        _fx.SeedOwner();

        var result = await LoginAsync(AuthFixture.OwnerEmail, AuthFixture.OwnerPassword);

        var session = _fx.Data.Sessions.Single();
        session.TokenHash.ShouldBe("hash-of-" + result.Value.SessionToken);
        session.TokenHash.ShouldNotBe(result.Value.SessionToken);
    }

    [Fact]
    public async Task Success_is_audited_and_saved_once()
    {
        _fx.SeedOwner();

        await LoginAsync(AuthFixture.OwnerEmail, AuthFixture.OwnerPassword);

        var entry = _fx.Data.Events.ShouldHaveSingleItem();
        entry.Event.ShouldBe(AuditEventType.LoginSucceeded);
        entry.Email.ShouldBe(AuthFixture.OwnerEmail);
        entry.Ip.ShouldBe("203.0.113.7");
        _fx.Data.SaveCount.ShouldBe(1);
    }

    [Fact]
    public async Task A_second_login_does_not_revoke_the_first_session()
    {
        _fx.SeedOwner();

        await LoginAsync(AuthFixture.OwnerEmail, AuthFixture.OwnerPassword);
        await LoginAsync(AuthFixture.OwnerEmail, AuthFixture.OwnerPassword);

        _fx.Data.Sessions.Count.ShouldBe(2);
        _fx.Data.Sessions.ShouldAllBe(s => s.RevokedAt == null);
    }

    [Fact]
    public async Task Wrong_password_and_unknown_email_give_the_same_error()
    {
        _fx.SeedOwner();

        var wrongPassword = await LoginAsync(AuthFixture.OwnerEmail, "not the password");
        var unknownEmail = await LoginAsync("nobody@example.invalid", AuthFixture.OwnerPassword);

        wrongPassword.Error.ShouldBe(AuthErrors.InvalidCredentials);
        unknownEmail.Error.ShouldBe(AuthErrors.InvalidCredentials);
        _fx.Data.Sessions.ShouldBeEmpty();
    }

    [Fact]
    public async Task Unknown_email_still_spends_the_time_of_a_password_check()
    {
        _fx.SeedOwner();

        await LoginAsync("nobody@example.invalid", "whatever");

        _fx.Hasher.SimulatedVerifications.ShouldBe(1);
    }

    [Theory]
    [InlineData(null, "pw")]
    [InlineData("", "pw")]
    [InlineData("owner@example.invalid", null)]
    [InlineData("owner@example.invalid", "")]
    public async Task Missing_input_is_an_invalid_credential_not_an_exception(string? email, string? password)
    {
        _fx.SeedOwner();

        var result = await LoginAsync(email, password);

        result.Error.ShouldBe(AuthErrors.InvalidCredentials);
    }

    [Fact]
    public async Task Oversized_password_is_refused_without_hashing_it()
    {
        _fx.SeedOwner();

        var result = await LoginAsync(AuthFixture.OwnerEmail, new string('x', 5000));

        result.Error.ShouldBe(AuthErrors.InvalidCredentials);
    }

    [Fact]
    public async Task Failures_are_audited_with_the_attempted_email_only()
    {
        _fx.SeedOwner();

        await LoginAsync("Nobody@Example.Invalid", "secret-that-must-not-leak");

        var entry = _fx.Data.Events.ShouldHaveSingleItem();
        entry.Event.ShouldBe(AuditEventType.LoginFailed);
        entry.Email.ShouldBe("nobody@example.invalid");
        entry.Detail.ShouldBeNull();
    }

    [Fact]
    public async Task Fifth_consecutive_failure_locks_and_reports_the_remaining_time()
    {
        _fx.SeedOwner();
        for (var i = 0; i < 4; i++)
        {
            (await LoginAsync(AuthFixture.OwnerEmail, "bad")).Error.ShouldBe(AuthErrors.InvalidCredentials);
        }

        var fifth = await LoginAsync(AuthFixture.OwnerEmail, "bad");

        fifth.Error.Code.ShouldBe(AuthErrors.LockedCode);
        fifth.Error.Kind.ShouldBe(ErrorKind.Locked);
        fifth.Error.RetryAfterSeconds.ShouldBe(900);
        _fx.Data.Events.Select(e => e.Event).ShouldContain(AuditEventType.AccountLocked);
    }

    [Fact]
    public async Task A_correct_password_during_the_lock_is_still_refused()
    {
        _fx.SeedOwner();
        for (var i = 0; i < 5; i++)
        {
            await LoginAsync(AuthFixture.OwnerEmail, "bad");
        }

        _fx.Clock.Advance(TimeSpan.FromMinutes(5));
        var result = await LoginAsync(AuthFixture.OwnerEmail, AuthFixture.OwnerPassword);

        result.Error.Code.ShouldBe(AuthErrors.LockedCode);
        result.Error.RetryAfterSeconds.ShouldBe(600);
        _fx.Data.Sessions.ShouldBeEmpty();
        _fx.Data.Events.Last().Event.ShouldBe(AuditEventType.LoginRefusedLocked);
    }

    [Fact]
    public async Task After_the_lock_expires_the_correct_password_works_again()
    {
        _fx.SeedOwner();
        for (var i = 0; i < 5; i++)
        {
            await LoginAsync(AuthFixture.OwnerEmail, "bad");
        }

        _fx.Clock.Advance(TimeSpan.FromMinutes(15));
        var result = await LoginAsync(AuthFixture.OwnerEmail, AuthFixture.OwnerPassword);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Success_resets_the_failure_counter()
    {
        var owner = _fx.SeedOwner();
        for (var i = 0; i < 4; i++)
        {
            await LoginAsync(AuthFixture.OwnerEmail, "bad");
        }

        await LoginAsync(AuthFixture.OwnerEmail, AuthFixture.OwnerPassword);
        for (var i = 0; i < 4; i++)
        {
            (await LoginAsync(AuthFixture.OwnerEmail, "bad")).Error.ShouldBe(AuthErrors.InvalidCredentials);
        }

        owner.FailedAttempts.ShouldBe(4);
        owner.IsLocked(_fx.Clock.GetUtcNow()).ShouldBeFalse();
    }

    [Fact]
    public async Task An_outdated_hash_is_upgraded_at_login()
    {
        var owner = _fx.SeedOwner("old:" + AuthFixture.OwnerPassword);

        var result = await LoginAsync(AuthFixture.OwnerEmail, AuthFixture.OwnerPassword);

        result.IsSuccess.ShouldBeTrue();
        owner.PasswordHash.ShouldBe("h:" + AuthFixture.OwnerPassword);
    }

    [Fact]
    public async Task A_current_hash_is_left_alone()
    {
        var owner = _fx.SeedOwner();

        await LoginAsync(AuthFixture.OwnerEmail, AuthFixture.OwnerPassword);

        owner.PasswordHash.ShouldBe("h:" + AuthFixture.OwnerPassword);
    }
}
