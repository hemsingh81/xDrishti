using XDrishti.Application.Auth;
using XDrishti.Domain.Auth;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Tests.Auth;

/// <summary>
/// A save that loses against a concurrent change to the owner (row version) is retried from fresh state:
/// the failure counter is never overwritten, and a lock set by a concurrent request is never bypassed.
/// </summary>
public sealed class LoginConcurrencyTests
{
    private readonly AuthFixture _fx = new();

    private ValueTask<Result<LoginResult>> LoginAsync(string password) =>
        _fx.Login().HandleAsync(new LoginCommand(AuthFixture.OwnerEmail, password, AuthFixture.Client), CancellationToken.None);

    /// <summary>What another request leaves behind: the owner with exactly this many consecutive failures.</summary>
    private Action OtherRequestLeavesFailures(OwnerAccount owner, int failures) => () =>
    {
        owner.RegisterSuccess();
        for (var i = 0; i < failures; i++)
        {
            owner.RegisterFailure(_fx.Clock.GetUtcNow());
        }
    };

    [Fact]
    public async Task A_wrong_password_that_loses_the_race_is_counted_on_top_of_the_stored_failures()
    {
        var owner = _fx.SeedOwner();
        _fx.Data.ConcurrentWrites.Enqueue(OtherRequestLeavesFailures(owner, 2));

        var result = await LoginAsync("bad");

        result.Error.ShouldBe(AuthErrors.InvalidCredentials);
        owner.FailedAttempts.ShouldBe(3);
        _fx.Data.ConflictCount.ShouldBe(1);
        _fx.Data.Events.ShouldHaveSingleItem().Event.ShouldBe(AuditEventType.LoginFailed); // the discarded attempt left no trace
    }

    [Fact]
    public async Task The_failure_that_reaches_the_threshold_after_a_retry_locks_the_account()
    {
        var owner = _fx.SeedOwner();
        _fx.Data.ConcurrentWrites.Enqueue(OtherRequestLeavesFailures(owner, 4));

        var result = await LoginAsync("bad");

        result.Error.Code.ShouldBe(AuthErrors.LockedCode);
        owner.IsLocked(_fx.Clock.GetUtcNow()).ShouldBeTrue();
        _fx.Data.Events.Select(e => e.Event).ShouldBe([AuditEventType.LoginFailed, AuditEventType.AccountLocked]);
    }

    [Fact]
    public async Task A_wrong_password_that_finds_the_account_locked_by_the_race_is_refused_as_locked()
    {
        var owner = _fx.SeedOwner();
        _fx.Data.ConcurrentWrites.Enqueue(OtherRequestLeavesFailures(owner, 5));

        var result = await LoginAsync("bad");

        result.Error.Code.ShouldBe(AuthErrors.LockedCode);
        owner.FailedAttempts.ShouldBe(5);
        _fx.Data.Events.ShouldHaveSingleItem().Event.ShouldBe(AuditEventType.LoginRefusedLocked);
    }

    [Fact]
    public async Task A_correct_password_cannot_bypass_a_lock_set_while_it_was_being_saved()
    {
        var owner = _fx.SeedOwner();
        _fx.Data.ConcurrentWrites.Enqueue(OtherRequestLeavesFailures(owner, 5));

        var result = await LoginAsync(AuthFixture.OwnerPassword);

        result.Error.Code.ShouldBe(AuthErrors.LockedCode);
        _fx.Data.Sessions.ShouldBeEmpty();
        _fx.Data.Events.Select(e => e.Event).ShouldNotContain(AuditEventType.LoginSucceeded);
    }

    [Fact]
    public async Task A_correct_password_that_loses_the_race_without_a_lock_still_signs_in_once()
    {
        var owner = _fx.SeedOwner();
        _fx.Data.ConcurrentWrites.Enqueue(OtherRequestLeavesFailures(owner, 3));

        var result = await LoginAsync(AuthFixture.OwnerPassword);

        result.IsSuccess.ShouldBeTrue();
        _fx.Data.Sessions.ShouldHaveSingleItem();
        _fx.Data.Events.ShouldHaveSingleItem().Event.ShouldBe(AuditEventType.LoginSucceeded);
        owner.FailedAttempts.ShouldBe(0);
    }

    [Fact]
    public async Task The_password_is_checked_once_however_often_the_save_is_retried()
    {
        var owner = _fx.SeedOwner("old:" + AuthFixture.OwnerPassword);
        _fx.Data.ConcurrentWrites.Enqueue(OtherRequestLeavesFailures(owner, 1));
        _fx.Data.ConcurrentWrites.Enqueue(OtherRequestLeavesFailures(owner, 2));

        var result = await LoginAsync(AuthFixture.OwnerPassword);

        result.IsSuccess.ShouldBeTrue();
        _fx.Data.ConflictCount.ShouldBe(2);
        owner.PasswordHash.ShouldBe("h:" + AuthFixture.OwnerPassword);
    }

    [Fact]
    public async Task Persistent_conflicts_end_in_a_busy_error_that_neither_counts_nor_grants()
    {
        _fx.SeedOwner();
        for (var i = 0; i < LoginHandler.MaxConcurrencyAttempts; i++)
        {
            _fx.Data.ConcurrentWrites.Enqueue(() => { });
        }

        var result = await LoginAsync(AuthFixture.OwnerPassword);

        result.Error.ShouldBe(AuthErrors.Busy);
        _fx.Data.ConflictCount.ShouldBe(LoginHandler.MaxConcurrencyAttempts);
        _fx.Data.Sessions.ShouldBeEmpty();
        _fx.Data.Events.ShouldBeEmpty();
    }
}
