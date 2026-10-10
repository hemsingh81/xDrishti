namespace XDrishti.Application.Abstractions.Auth;

public enum PasswordVerificationResult
{
    Failed,
    Success,

    /// <summary>The password is right but the stored hash uses outdated parameters: store a fresh hash.</summary>
    SuccessRehashNeeded,
}
