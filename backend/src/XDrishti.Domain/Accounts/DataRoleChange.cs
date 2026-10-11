namespace XDrishti.Domain.Accounts;

/// <summary>Switch the Data role on (as primary or standby) or off.</summary>
public sealed record DataRoleChange(bool Enabled, DataRoleKind? Kind);
