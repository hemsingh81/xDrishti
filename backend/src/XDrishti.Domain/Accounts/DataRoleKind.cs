namespace XDrishti.Domain.Accounts;

/// <summary>What a Data account is: the one that serves market data, or the one that takes over when it fails.</summary>
public enum DataRoleKind
{
    Primary,
    Standby,
}
