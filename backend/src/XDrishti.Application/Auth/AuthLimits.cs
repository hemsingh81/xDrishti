namespace XDrishti.Application.Auth;

/// <summary>Input limits that keep the slow password hash from being abused and match the column sizes.</summary>
internal static class AuthLimits
{
    public const int MaxEmailLength = 256;

    public const int MaxDisplayNameLength = 128;

    public const int MaxPasswordLength = 1024;
}
