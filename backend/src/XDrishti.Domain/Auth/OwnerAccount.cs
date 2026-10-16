using XDrishti.Domain.Common;

namespace XDrishti.Domain.Auth;

/// <summary>
/// The one and only owner of the system. Holds the password hash and the brute-force lockout state.
/// Lockout rule: <see cref="MaxFailedAttempts"/> consecutive failures lock the account for <see cref="LockoutDuration"/>.
/// </summary>
public sealed class OwnerAccount : Entity<Guid>
{
    public const int MaxFailedAttempts = 5;

    public const string ProfileFieldEmail = "email";

    public const string ProfileFieldDisplayName = "displayName";

    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    // Parameter names match property names so EF Core can bind this constructor when materialising.
    private OwnerAccount(Guid id, string email, string displayName, string passwordHash, DateTimeOffset createdAt)
        : base(id)
    {
        Email = email;
        DisplayName = displayName;
        PasswordHash = passwordHash;
        CreatedAt = createdAt;
        PasswordChangedAt = createdAt;
    }

    public string Email { get; private set; }

    public string DisplayName { get; private set; }

    public string PasswordHash { get; private set; }

    public int FailedAttempts { get; private set; }

    public DateTimeOffset? LockedUntil { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset PasswordChangedAt { get; private set; }

    /// <summary>Canonical form of an e-mail address used for lookups and storage.</summary>
    public static string NormalizeEmail(string? email) => (email ?? string.Empty).Trim().ToLowerInvariant();

    public static OwnerAccount Create(string email, string displayName, string passwordHash, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        return new OwnerAccount(Guid.CreateVersion7(now), NormalizeEmail(email), displayName.Trim(), passwordHash, now);
    }

    public bool IsLocked(DateTimeOffset now) => LockedUntil is { } until && until > now;

    /// <summary>Time left until the lock ends; zero when the account is not locked.</summary>
    public TimeSpan RemainingLock(DateTimeOffset now) => IsLocked(now) ? LockedUntil!.Value - now : TimeSpan.Zero;

    /// <summary>Records a wrong password. Returns true when this failure locked the account.</summary>
    public bool RegisterFailure(DateTimeOffset now)
    {
        if (IsLocked(now))
        {
            return false; // refused attempts while locked change nothing
        }

        if (LockedUntil is not null)
        {
            ClearLockout(); // the previous lock has run out: start counting afresh
        }

        FailedAttempts++;
        if (FailedAttempts < MaxFailedAttempts)
        {
            return false;
        }

        LockedUntil = now + LockoutDuration;
        return true;
    }

    /// <summary>A correct password resets the failure counter.</summary>
    public void RegisterSuccess() => ClearLockout();

    /// <summary>Owner changes the password (reset): new hash, lockout cleared.</summary>
    public void ReplaceCredentials(string email, string displayName, string passwordHash, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        Email = NormalizeEmail(email);
        DisplayName = displayName.Trim();
        PasswordHash = passwordHash;
        PasswordChangedAt = now;
        ClearLockout();
    }

    /// <summary>The owner chose a new password (the current one was verified): new hash, change time recorded, failure counter and lock cleared.</summary>
    public void ChangePassword(string passwordHash, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        PasswordHash = passwordHash;
        PasswordChangedAt = now;
        ClearLockout();
    }

    /// <summary>
    /// Replaces the display name and sign-in e-mail (the password is untouched). Returns the names of the fields whose stored value
    /// actually changed (<see cref="ProfileFieldEmail"/>, <see cref="ProfileFieldDisplayName"/>), empty when nothing changed.
    /// </summary>
    public IReadOnlyList<string> UpdateProfile(string email, string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        var changed = new List<string>(2);
        var newEmail = NormalizeEmail(email);
        if (!string.Equals(Email, newEmail, StringComparison.Ordinal))
        {
            Email = newEmail;
            changed.Add(ProfileFieldEmail);
        }

        var newName = displayName.Trim();
        if (!string.Equals(DisplayName, newName, StringComparison.Ordinal))
        {
            DisplayName = newName;
            changed.Add(ProfileFieldDisplayName);
        }

        return changed;
    }

    /// <summary>Stores the same password with stronger parameters (transparent upgrade at login).</summary>
    public void UpgradePasswordHash(string passwordHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        PasswordHash = passwordHash;
    }

    private void ClearLockout()
    {
        FailedAttempts = 0;
        LockedUntil = null;
    }
}
