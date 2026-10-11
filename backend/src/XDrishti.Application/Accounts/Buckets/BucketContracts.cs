namespace XDrishti.Application.Accounts.Buckets;

/// <summary>
/// One bucket to pick from. IsAccountDefault is true for the automatic default bucket of an account that has the Portfolio role
/// (AccountId and AccountName name that account); custom buckets have neither.
/// </summary>
public sealed record BucketView(Guid Id, string Name, bool IsAccountDefault, Guid? AccountId, string? AccountName);

/// <summary>A new custom bucket. The name has 1 to 40 letters, digits, spaces, '_' or '-' and is unique ignoring case.</summary>
public sealed record BucketInput(string? Name);
