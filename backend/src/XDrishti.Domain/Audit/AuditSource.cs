namespace XDrishti.Domain.Audit;

/// <summary>The six insert-only stores the audit screen reads (spec P8-22, ADR 0117). The ordinal is part of the order and of the cursor: never reorder.</summary>
public enum AuditSource
{
    Auth = 0,
    Account = 1,
    Approval = 2,
    Plan = 3,
    Autonomy = 4,
    Learning = 5,
}

/// <summary>The wire names of the sources.</summary>
public static class AuditSources
{
    public static IReadOnlyList<AuditSource> All { get; } = [AuditSource.Auth, AuditSource.Account, AuditSource.Approval, AuditSource.Plan, AuditSource.Autonomy, AuditSource.Learning];

    public static string Code(AuditSource source) => source switch
    {
        AuditSource.Auth => "auth",
        AuditSource.Account => "account",
        AuditSource.Approval => "approval",
        AuditSource.Plan => "plan",
        AuditSource.Autonomy => "autonomy",
        AuditSource.Learning => "learning",
        _ => throw new ArgumentOutOfRangeException(nameof(source)),
    };

    public static bool TryParse(string? code, out AuditSource source)
    {
        foreach (var candidate in All)
        {
            if (string.Equals(Code(candidate), code, StringComparison.Ordinal))
            {
                source = candidate;
                return true;
            }
        }

        source = default;
        return false;
    }

    /// <summary>True when the source's id is a uuid (auth, account); the others are numbers.</summary>
    public static bool IsUuid(AuditSource source) => source is AuditSource.Auth or AuditSource.Account;

    /// <summary>True when the text is a valid id for the source (a uuid, or a positive number).</summary>
    public static bool IsValidId(AuditSource source, string? id) =>
        IsUuid(source) ? Guid.TryParseExact(id, "D", out _) : long.TryParse(id, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var number) && number > 0;
}
