using XDrishti.Application.Auth;
using XDrishti.Domain.Auth;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Tests.Auth;

public sealed class ChangePasswordHandlerTests
{
    private const string NewPassword = "a brand new password";

    private readonly AuthFixture _fx = new();

    private async Task<AuthSession> SignedInSessionAsync()
    {
        _fx.SeedOwner();
        await _fx.SignInAsync();
        return _fx.Data.Sessions.Single();
    }

    private ValueTask<Result> ChangeAsync(AuthSession session, string? current, string? next) =>
        _fx.ChangePassword().HandleAsync(new ChangePasswordCommand(session.Id, current, next, AuthFixture.Client), CancellationToken.None);

    [Fact]
    public async Task The_correct_current_password_stores_the_new_hash_and_audits()
    {
        var session = await SignedInSessionAsync();
        _fx.Clock.Advance(TimeSpan.FromMinutes(2));

        var result = await ChangeAsync(session, AuthFixture.OwnerPassword, NewPassword);

        result.IsSuccess.ShouldBeTrue();
        var owner = _fx.Data.Owners.Single();
        owner.PasswordHash.ShouldBe("h:" + NewPassword);
        owner.PasswordChangedAt.ShouldBe(AuthFixture.Start.AddMinutes(2));
        var entry = _fx.Data.Events.Last();
        entry.Event.ShouldBe(AuditEventType.PasswordChanged);
        entry.Email.ShouldBe(AuthFixture.OwnerEmail);
        entry.Detail.ShouldNotBeNull().ShouldNotContain(NewPassword);
        entry.Detail.ShouldNotContain(AuthFixture.OwnerPassword);
    }

    [Fact]
    public async Task Every_other_session_is_revoked_and_the_current_one_stays_signed_in()
    {
        var current = await SignedInSessionAsync();
        await _fx.SignInAsync();
        await _fx.SignInAsync();
        var alreadyRevoked = _fx.Data.Sessions[2];
        alreadyRevoked.Revoke(_fx.Clock.GetUtcNow());
        _fx.Clock.Advance(TimeSpan.FromMinutes(1));

        await ChangeAsync(current, AuthFixture.OwnerPassword, NewPassword);

        current.RevokedAt.ShouldBeNull();
        current.Evaluate(_fx.Clock.GetUtcNow()).ShouldBe(SessionStatus.Active);
        _fx.Data.Sessions[1].RevokedAt.ShouldBe(_fx.Clock.GetUtcNow());
        alreadyRevoked.RevokedAt.ShouldBe(AuthFixture.Start); // untouched
    }

    [Fact]
    public async Task The_old_password_stops_working_and_the_new_one_works()
    {
        var session = await SignedInSessionAsync();
        await ChangeAsync(session, AuthFixture.OwnerPassword, NewPassword);

        var old = await _fx.Login().HandleAsync(new LoginCommand(AuthFixture.OwnerEmail, AuthFixture.OwnerPassword, AuthFixture.Client), CancellationToken.None);
        var fresh = await _fx.Login().HandleAsync(new LoginCommand(AuthFixture.OwnerEmail, NewPassword, AuthFixture.Client), CancellationToken.None);

        old.Error.ShouldBe(AuthErrors.InvalidCredentials);
        fresh.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task A_correct_password_resets_the_failure_counter()
    {
        var session = await SignedInSessionAsync();
        await ChangeAsync(session, "wrong", NewPassword);
        await ChangeAsync(session, "wrong", NewPassword);

        await ChangeAsync(session, AuthFixture.OwnerPassword, NewPassword);

        _fx.Data.Owners.Single().FailedAttempts.ShouldBe(0);
    }

    [Theory]
    [InlineData("wrong")]
    [InlineData("")]
    [InlineData(null)]
    public async Task A_wrong_or_empty_current_password_is_refused_counted_and_audited(string? current)
    {
        var session = await SignedInSessionAsync();

        var result = await ChangeAsync(session, current, NewPassword);

        result.Error.ShouldBe(AuthErrors.InvalidCredentials);
        var owner = _fx.Data.Owners.Single();
        owner.PasswordHash.ShouldBe("h:" + AuthFixture.OwnerPassword);
        owner.FailedAttempts.ShouldBe(1);
        _fx.Data.Events.Last().Event.ShouldBe(AuditEventType.StepUpFailed);
        _fx.Data.Events.Select(e => e.Event).ShouldNotContain(AuditEventType.PasswordChanged);
        _fx.Data.Sessions.ShouldAllBe(s => s.RevokedAt == null);
    }

    [Fact]
    public async Task Five_wrong_current_passwords_lock_the_account_exactly_like_sign_in()
    {
        var session = await SignedInSessionAsync();
        Result? last = null;

        for (var i = 0; i < OwnerAccount.MaxFailedAttempts; i++)
        {
            last = await ChangeAsync(session, "wrong", NewPassword);
        }

        last!.Error.Code.ShouldBe(AuthErrors.LockedCode);
        last.Error.Kind.ShouldBe(ErrorKind.Locked);
        _fx.Data.Owners.Single().IsLocked(_fx.Clock.GetUtcNow()).ShouldBeTrue();
        _fx.Data.Events.Select(e => e.Event).ShouldContain(AuditEventType.AccountLocked);
    }

    [Fact]
    public async Task Failures_from_sign_in_and_password_change_share_one_counter()
    {
        var session = await SignedInSessionAsync();
        var owner = _fx.Data.Owners.Single();
        for (var i = 0; i < OwnerAccount.MaxFailedAttempts - 1; i++)
        {
            owner.RegisterFailure(_fx.Clock.GetUtcNow());
        }

        var result = await ChangeAsync(session, "wrong", NewPassword);

        result.Error.Code.ShouldBe(AuthErrors.LockedCode);
    }

    [Fact]
    public async Task A_locked_account_is_refused_even_with_the_correct_password()
    {
        var session = await SignedInSessionAsync();
        for (var i = 0; i < OwnerAccount.MaxFailedAttempts; i++)
        {
            await ChangeAsync(session, "wrong", NewPassword);
        }

        var result = await ChangeAsync(session, AuthFixture.OwnerPassword, NewPassword);

        result.Error.Code.ShouldBe(AuthErrors.LockedCode);
        _fx.Data.Owners.Single().PasswordHash.ShouldBe("h:" + AuthFixture.OwnerPassword);
        _fx.Data.Events.Select(e => e.Event).ShouldNotContain(AuditEventType.PasswordChanged);
    }

    [Fact]
    public async Task The_lock_ends_after_fifteen_minutes_and_the_change_goes_through()
    {
        var session = await SignedInSessionAsync();
        for (var i = 0; i < OwnerAccount.MaxFailedAttempts; i++)
        {
            await ChangeAsync(session, "wrong", NewPassword);
        }

        _fx.Clock.Advance(OwnerAccount.LockoutDuration + TimeSpan.FromSeconds(1));
        var result = await ChangeAsync(session, AuthFixture.OwnerPassword, NewPassword);

        result.IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("123456789")]
    public async Task A_missing_or_too_short_new_password_is_a_field_error_that_is_not_counted(string? next)
    {
        var session = await SignedInSessionAsync();

        var result = await ChangeAsync(session, AuthFixture.OwnerPassword, next);

        result.Error.Code.ShouldBe(AuthErrors.PasswordInvalidCode);
        result.Error.Kind.ShouldBe(ErrorKind.Validation);
        result.Error.Fields.ShouldNotBeNull().Single().Field.ShouldBe("newPassword");
        _fx.Data.Owners.Single().FailedAttempts.ShouldBe(0);
        _fx.Data.Owners.Single().PasswordHash.ShouldBe("h:" + AuthFixture.OwnerPassword);
        _fx.Data.Events.Select(e => e.Event).ShouldNotContain(AuditEventType.PasswordChanged);
    }

    [Fact]
    public async Task A_new_password_of_exactly_ten_and_exactly_1024_characters_is_accepted()
    {
        var session = await SignedInSessionAsync();

        (await ChangeAsync(session, AuthFixture.OwnerPassword, new string('a', 10))).IsSuccess.ShouldBeTrue();
        (await ChangeAsync(session, new string('a', 10), new string('b', 1024))).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task A_new_password_longer_than_1024_characters_is_refused()
    {
        var session = await SignedInSessionAsync();

        var result = await ChangeAsync(session, AuthFixture.OwnerPassword, new string('a', 1025));

        result.Error.Code.ShouldBe(AuthErrors.PasswordInvalidCode);
    }

    [Fact]
    public async Task A_new_password_equal_to_the_current_one_is_refused_and_not_counted()
    {
        var session = await SignedInSessionAsync();

        var result = await ChangeAsync(session, AuthFixture.OwnerPassword, AuthFixture.OwnerPassword);

        result.Error.Code.ShouldBe(AuthErrors.PasswordUnchangedCode);
        result.Error.Fields.ShouldNotBeNull().Single().Field.ShouldBe("newPassword");
        _fx.Data.Owners.Single().FailedAttempts.ShouldBe(0);
        _fx.Data.Events.Select(e => e.Event).ShouldNotContain(AuditEventType.PasswordChanged);
    }

    [Fact]
    public async Task A_wrong_current_password_equal_to_the_new_one_is_still_a_credential_failure()
    {
        var session = await SignedInSessionAsync();

        var result = await ChangeAsync(session, NewPassword, NewPassword);

        result.Error.ShouldBe(AuthErrors.InvalidCredentials);
        _fx.Data.Owners.Single().FailedAttempts.ShouldBe(1);
    }

    [Fact]
    public async Task A_revoked_or_unknown_session_is_unauthenticated()
    {
        var session = await SignedInSessionAsync();
        session.Revoke(_fx.Clock.GetUtcNow());

        var revoked = await ChangeAsync(session, AuthFixture.OwnerPassword, NewPassword);
        var unknown = await _fx.ChangePassword().HandleAsync(
            new ChangePasswordCommand(Guid.NewGuid(), AuthFixture.OwnerPassword, NewPassword, AuthFixture.Client), CancellationToken.None);

        revoked.Error.ShouldBe(AuthErrors.Unauthenticated);
        unknown.Error.ShouldBe(AuthErrors.Unauthenticated);
        _fx.Data.Owners.Single().PasswordHash.ShouldBe("h:" + AuthFixture.OwnerPassword);
    }

    [Fact]
    public async Task A_save_that_loses_against_a_concurrent_change_is_retried_and_still_revokes_the_other_sessions()
    {
        var current = await SignedInSessionAsync();
        await _fx.SignInAsync();
        var owner = _fx.Data.Owners.Single();
        _fx.Data.ConcurrentWrites.Enqueue(() => owner.RegisterFailure(_fx.Clock.GetUtcNow()));

        var result = await ChangeAsync(current, AuthFixture.OwnerPassword, NewPassword);

        result.IsSuccess.ShouldBeTrue();
        _fx.Data.ConflictCount.ShouldBe(1);
        owner.PasswordHash.ShouldBe("h:" + NewPassword);
        owner.FailedAttempts.ShouldBe(0);
        _fx.Data.Sessions[1].RevokedAt.ShouldNotBeNull();
        current.RevokedAt.ShouldBeNull();
        _fx.Data.Events.Count(e => e.Event == AuditEventType.PasswordChanged).ShouldBe(1);
    }

    [Fact]
    public async Task A_correct_password_cannot_bypass_a_lock_set_while_it_was_being_saved()
    {
        var session = await SignedInSessionAsync();
        var owner = _fx.Data.Owners.Single();
        _fx.Data.ConcurrentWrites.Enqueue(() =>
        {
            for (var i = 0; i < OwnerAccount.MaxFailedAttempts; i++)
            {
                owner.RegisterFailure(_fx.Clock.GetUtcNow());
            }
        });

        var result = await ChangeAsync(session, AuthFixture.OwnerPassword, NewPassword);

        result.Error.Code.ShouldBe(AuthErrors.LockedCode);
        _fx.Data.Events.Select(e => e.Event).ShouldNotContain(AuditEventType.PasswordChanged); // the fake keeps in-memory edits; the real unit of work discards them
    }
}
