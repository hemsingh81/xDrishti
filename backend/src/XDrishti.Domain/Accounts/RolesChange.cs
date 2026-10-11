namespace XDrishti.Domain.Accounts;

/// <summary>A request to switch roles on or off; null leaves that role as it is.</summary>
public sealed record RolesChange(DataRoleChange? Data, bool? Trading, bool? Portfolio);
