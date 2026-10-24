namespace XDrishti.Domain.Planning;

/// <summary>What a stored fill is to the journal (P7-14). Stored by code; never rename.</summary>
public enum JournalRole
{
    /// <summary>It opened (or added to) the position of a ticket.</summary>
    Entry,

    /// <summary>It closed (part of) the position of a ticket.</summary>
    Exit,

    /// <summary>No ticket: the owner's own trade, reported separately.</summary>
    Discretionary,
}

/// <summary>The spelling of a <see cref="JournalRole"/> in the table.</summary>
public static class JournalRoleText
{
    /// <summary>The lower-case code.</summary>
    public static string Code(this JournalRole role) => role.ToString().ToLowerInvariant();

    /// <summary>Reads a code back; null when unknown.</summary>
    public static JournalRole? TryParse(string? code) =>
        Enum.GetValues<JournalRole>().Cast<JournalRole?>().FirstOrDefault(r => string.Equals(r!.Value.Code(), code, StringComparison.OrdinalIgnoreCase));
}
