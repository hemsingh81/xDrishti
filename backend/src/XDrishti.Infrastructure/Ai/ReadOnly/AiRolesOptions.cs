namespace XDrishti.Infrastructure.Ai.ReadOnly;

/// <summary>Section <c>AiRoles</c> of the one-shot <c>xd-api migrate</c> (spec P9-02 AC 13): the two passwords of the read-only logins, from secret files (<c>AiRoles__ReadPassword</c>, <c>AiRoles__SqlPassword</c>). Absent or empty: the roles stay NOLOGIN.</summary>
public sealed class AiRolesOptions
{
    public const string SectionName = "AiRoles";

    public string? ReadPassword { get; set; }

    public string? SqlPassword { get; set; }

    /// <summary>Never shows a password.</summary>
    public override string ToString() => "AiRolesOptions { passwords hidden }";
}
