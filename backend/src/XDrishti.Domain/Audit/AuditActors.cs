namespace XDrishti.Domain.Audit;

/// <summary>Who did it, as a kind (spec P8-22 decision 7): an automatic approval or a service is never shown as the owner, and an address typed at a failed sign-in that is not the owner's is not shown at all. Pure.</summary>
public static class AuditActors
{
    public const string Owner = "owner";
    public const string System = "system";
    public const string Rule = "rule";
    public const string Cli = "cli";
    public const string Other = "other";
    public const string OtherAddress = "other_address";

    public static IReadOnlyList<string> Filterable { get; } = [Owner, System, Rule, Cli, Other];

    /// <summary>The kind of actor and the actor to show (null when it must not be shown). <paramref name="stored"/> is the actor column, or for the sign-in store the address typed.</summary>
    public static (string Kind, string? Shown) Classify(AuditSource source, string? stored, string? ownerEmail)
    {
        var text = stored?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return (source == AuditSource.Auth ? System : Other, null);
        }

        if (source == AuditSource.Auth)
        {
            return IsOwner(text, ownerEmail) ? (Owner, text) : (OtherAddress, null);
        }

        if (text.StartsWith("owner:", StringComparison.Ordinal) || IsOwner(text, ownerEmail))
        {
            return (Owner, text);
        }

        if (text.StartsWith("system:", StringComparison.Ordinal))
        {
            return (System, text);
        }

        if (text.StartsWith("rule:", StringComparison.Ordinal))
        {
            return (Rule, text);
        }

        return text.StartsWith("cli:", StringComparison.Ordinal) ? (Cli, text) : (Other, text);
    }

    private static bool IsOwner(string text, string? ownerEmail) => !string.IsNullOrWhiteSpace(ownerEmail) && string.Equals(text, ownerEmail.Trim(), StringComparison.OrdinalIgnoreCase);
}
