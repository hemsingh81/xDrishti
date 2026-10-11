using XDrishti.Application.Auth;
using XDrishti.Domain.Auth;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Tests.Auth;

public sealed class StepUpHandlerTests
{
    private readonly AuthFixture _fx = new();

    private async Task<AuthSession> SignedInSessionAsync()
    {
        _fx.SeedOwner();
        await _fx.SignInAsync();
        return _fx.Data.Sessions.Single();
    }

    private ValueTask<Result<StepUpResult>> StepUpAsync(AuthSession session, string? password) =>
        _fx.StepUp().HandleAsync(new StepUpCommand(session.Id, password, AuthFixture.Client), CancellationToken.None);

    [Fact]
    public async Task The_correct_password_elevates_the_session_for_five_minutes_and_is_audited()
    {
        var session = await SignedInSessionAsync();

        var result = await StepUpAsync(session, AuthFixture.OwnerPassword);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ElevatedUntil.ShouldBe(AuthFixture.Start.AddMinutes(5));
        session.IsElevated(AuthFixture.Start.AddMinutes(4)).ShouldBeTrue();
        _fx.Data.Events.Last().Event.ShouldBe(AuditEventType.StepUpSucceeded);
    }

    [Fact]
    public async Task The_window_ends_after_five_minutes()
    {
        var session = await SignedInSessionAsync();
        await StepUpAsync(session, AuthFixture.OwnerPassword);

        _fx.Clock.Advance(TimeSpan.FromMinutes(5));

        session.IsElevated(_fx.Clock.GetUtcNow()).ShouldBeFalse();
    }

    [Theory]
    [InlineData("wrong")]
    [InlineData("")]
    [InlineData(null)]
    public async Task A_wrong_or_empty_password_is_refused_counted_and_audited(string? password)
    {
        var session = await SignedInSessionAsync();

        var result = await StepUpAsync(session, password);

        result.Error.ShouldBe(AuthErrors.InvalidCredentials);
        session.IsElevated(_fx.Clock.GetUtcNow()).ShouldBeFalse();
        _fx.Data.Owners.Single().FailedAttempts.ShouldBe(1);
        _fx.Data.Events.Last().Event.ShouldBe(AuditEventType.StepUpFailed);
    }

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account_exactly_like_sign_in()
    {
        var session = await SignedInSessionAsync();
        Result<StepUpResult>? last = null;

        for (var i = 0; i < OwnerAccount.MaxFailedAttempts; i++)
        {
            last = await StepUpAsync(session, "wrong");
        }

        last!.Error.Code.ShouldBe(AuthErrors.LockedCode);
        last.Error.Kind.ShouldBe(ErrorKind.Locked);
        _fx.Data.Owners.Single().IsLocked(_fx.Clock.GetUtcNow()).ShouldBeTrue();
        _fx.Data.Events.Select(e => e.Event).ShouldContain(AuditEventType.AccountLocked);
    }

    [Fact]
    public async Task Failures_from_sign_in_and_step_up_share_one_counter()
    {
        var session = await SignedInSessionAsync();
        var owner = _fx.Data.Owners.Single();
        for (var i = 0; i < OwnerAccount.MaxFailedAttempts - 1; i++)
        {
            owner.RegisterFailure(_fx.Clock.GetUtcNow()); // four failed sign-ins
        }

        var result = await StepUpAsync(session, "wrong");

        result.Error.Code.ShouldBe(AuthErrors.LockedCode);
    }

    [Fact]
    public async Task A_locked_account_is_refused_even_with_the_correct_password()
    {
        var session = await SignedInSessionAsync();
        for (var i = 0; i < OwnerAccount.MaxFailedAttempts; i++)
        {
            await StepUpAsync(session, "wrong");
        }

        var result = await StepUpAsync(session, AuthFixture.OwnerPassword);

        result.Error.Code.ShouldBe(AuthErrors.LockedCode);
        session.IsElevated(_fx.Clock.GetUtcNow()).ShouldBeFalse();
        _fx.Data.Events.Count(e => e.Event == AuditEventType.StepUpSucceeded).ShouldBe(0);
    }

    [Fact]
    public async Task The_lock_ends_after_fifteen_minutes_and_a_correct_password_elevates()
    {
        var session = await SignedInSessionAsync();
        for (var i = 0; i < OwnerAccount.MaxFailedAttempts; i++)
        {
            await StepUpAsync(session, "wrong");
        }

        _fx.Clock.Advance(OwnerAccount.LockoutDuration + TimeSpan.FromSeconds(1));
        var result = await StepUpAsync(session, AuthFixture.OwnerPassword);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task A_correct_password_resets_the_failure_counter()
    {
        var session = await SignedInSessionAsync();
        await StepUpAsync(session, "wrong");
        await StepUpAsync(session, "wrong");

        await StepUpAsync(session, AuthFixture.OwnerPassword);

        _fx.Data.Owners.Single().FailedAttempts.ShouldBe(0);
    }

    [Fact]
    public async Task A_revoked_session_cannot_be_elevated()
    {
        var session = await SignedInSessionAsync();
        session.Revoke(_fx.Clock.GetUtcNow());

        var result = await StepUpAsync(session, AuthFixture.OwnerPassword);

        result.Error.ShouldBe(AuthErrors.Unauthenticated);
    }

    [Fact]
    public async Task An_unknown_session_is_unauthenticated()
    {
        _fx.SeedOwner();

        var result = await _fx.StepUp().HandleAsync(new StepUpCommand(Guid.NewGuid(), AuthFixture.OwnerPassword, AuthFixture.Client), CancellationToken.None);

        result.Error.ShouldBe(AuthErrors.Unauthenticated);
    }

    [Fact]
    public async Task A_save_that_loses_against_a_concurrent_change_is_retried_from_fresh_state()
    {
        var session = await SignedInSessionAsync();
        var owner = _fx.Data.Owners.Single();
        _fx.Data.ConcurrentWrites.Enqueue(() =>
        {
            owner.RegisterSuccess(); // what the other request leaves behind: exactly three consecutive failures
            for (var i = 0; i < 3; i++)
            {
                owner.RegisterFailure(_fx.Clock.GetUtcNow());
            }
        });

        var result = await StepUpAsync(session, "wrong");

        result.Error.ShouldBe(AuthErrors.InvalidCredentials);
        owner.FailedAttempts.ShouldBe(4);
        _fx.Data.ConflictCount.ShouldBe(1);
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

        var result = await StepUpAsync(session, AuthFixture.OwnerPassword);

        result.Error.Code.ShouldBe(AuthErrors.LockedCode);
        _fx.Data.Events.Select(e => e.Event).ShouldNotContain(AuditEventType.StepUpSucceeded);
    }
}
