namespace XDrishti.Application.Auth;

/// <summary>Input limits that keep the slow password hash from being abused and match the column sizes.</summary>
internal static class AuthLimits
{
    public const int MaxEmailLength = 256;

    public const int MaxDisplayNameLength = 128;

    public const int MaxPasswordLength = 1024;

    /// <summary>Shortest password the owner may choose when changing it (no composition rules).</summary>
    public const int MinNewPasswordLength = 10;
}
