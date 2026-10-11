using XDrishti.Domain.Auth;

namespace XDrishti.Domain.Tests.Auth;

public sealed class OwnerAccountTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 10, 9, 0, 0, TimeSpan.Zero);

    private static OwnerAccount NewOwner() => OwnerAccount.Create("Owner@Example.Invalid ", " The Owner ", "hash-1", T0);

    [Fact]
    public void Create_normalises_email_and_trims_name()
    {
        var owner = NewOwner();

        owner.Email.ShouldBe("owner@example.invalid");
        owner.DisplayName.ShouldBe("The Owner");
        owner.FailedAttempts.ShouldBe(0);
        owner.IsLocked(T0).ShouldBeFalse();
        owner.PasswordChangedAt.ShouldBe(T0);
    }

    [Theory]
    [InlineData("", "name", "hash")]
    [InlineData("a@b.c", " ", "hash")]
    [InlineData("a@b.c", "name", "")]
    public void Create_rejects_missing_values(string email, string name, string hash) =>
        Should.Throw<ArgumentException>(() => OwnerAccount.Create(email, name, hash, T0));

    [Fact]
    public void Failures_below_the_limit_do_not_lock()
    {
        var owner = NewOwner();

        for (var i = 0; i < OwnerAccount.MaxFailedAttempts - 1; i++)
        {
            owner.RegisterFailure(T0).ShouldBeFalse();
        }

        owner.FailedAttempts.ShouldBe(4);
        owner.IsLocked(T0).ShouldBeFalse();
    }

    [Fact]
    public void Fifth_consecutive_failure_locks_for_fifteen_minutes()
    {
        var owner = NewOwner();
        for (var i = 0; i < 4; i++)
        {
            owner.RegisterFailure(T0);
        }

        var lockedNow = owner.RegisterFailure(T0);

        lockedNow.ShouldBeTrue();
        owner.IsLocked(T0).ShouldBeTrue();
        owner.LockedUntil.ShouldBe(T0.AddMinutes(15));
        owner.RemainingLock(T0.AddMinutes(5)).ShouldBe(TimeSpan.FromMinutes(10));
    }

    [Fact]
    public void Lock_ends_exactly_when_the_window_is_over()
    {
        var owner = NewOwner();
        for (var i = 0; i < 5; i++)
        {
            owner.RegisterFailure(T0);
        }

        owner.IsLocked(T0.AddMinutes(15).AddTicks(-1)).ShouldBeTrue();
        owner.IsLocked(T0.AddMinutes(15)).ShouldBeFalse();
        owner.RemainingLock(T0.AddMinutes(15)).ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void Failures_while_locked_change_nothing()
    {
        var owner = NewOwner();
        for (var i = 0; i < 5; i++)
        {
            owner.RegisterFailure(T0);
        }

        owner.RegisterFailure(T0.AddMinutes(1)).ShouldBeFalse();

        owner.FailedAttempts.ShouldBe(5);
        owner.LockedUntil.ShouldBe(T0.AddMinutes(15));
    }

    [Fact]
    public void After_the_lock_expired_counting_starts_afresh()
    {
        var owner = NewOwner();
        for (var i = 0; i < 5; i++)
        {
            owner.RegisterFailure(T0);
        }

        owner.RegisterFailure(T0.AddMinutes(16)).ShouldBeFalse();

        owner.FailedAttempts.ShouldBe(1);
        owner.LockedUntil.ShouldBeNull();
    }

    [Fact]
    public void Success_resets_the_failure_counter()
    {
        var owner = NewOwner();
        owner.RegisterFailure(T0);
        owner.RegisterFailure(T0);

        owner.RegisterSuccess();

        owner.FailedAttempts.ShouldBe(0);
        owner.LockedUntil.ShouldBeNull();
    }

    [Fact]
    public void ReplaceCredentials_sets_new_values_and_clears_the_lock()
    {
        var owner = NewOwner();
        for (var i = 0; i < 5; i++)
        {
            owner.RegisterFailure(T0);
        }

        owner.ReplaceCredentials("New@Example.Invalid", "New Name", "hash-2", T0.AddHours(1));

        owner.Email.ShouldBe("new@example.invalid");
        owner.DisplayName.ShouldBe("New Name");
        owner.PasswordHash.ShouldBe("hash-2");
        owner.PasswordChangedAt.ShouldBe(T0.AddHours(1));
        owner.IsLocked(T0.AddHours(1)).ShouldBeFalse();
        owner.FailedAttempts.ShouldBe(0);
    }

    [Fact]
    public void UpgradePasswordHash_keeps_the_password_changed_time()
    {
        var owner = NewOwner();

        owner.UpgradePasswordHash("hash-upgraded");

        owner.PasswordHash.ShouldBe("hash-upgraded");
        owner.PasswordChangedAt.ShouldBe(T0);
    }
}
