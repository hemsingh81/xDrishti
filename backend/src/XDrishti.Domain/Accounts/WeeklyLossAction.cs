namespace XDrishti.Domain.Accounts;

/// <summary>What happens when the weekly loss limit is hit. Stored by name — never rename a value.</summary>
public enum WeeklyLossAction
{
    HalveSize,
    StopNewEntries,
    AlertOnly,
}
