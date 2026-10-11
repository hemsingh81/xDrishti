namespace XDrishti.Api.Auth;

/// <summary>
/// Owner credentials supplied as secret files (<c>Auth__Owner__Email</c>, <c>Auth__Owner__Name</c>, <c>Auth__Owner__Password</c>).
/// Read only by the provisioning and reset steps, never at normal start-up.
/// </summary>
internal sealed class OwnerSecretsOptions
{
    public const string SectionName = "Auth:Owner";

    public string? Email { get; set; }

    public string? Name { get; set; }

    public string? Password { get; set; }
}
