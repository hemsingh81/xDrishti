namespace XDrishti.Application.Accounts.Buckets;

public sealed record BucketView(Guid Id, string Name);

/// <summary>A new bucket. The name has 1 to 40 letters, digits, spaces, '_' or '-' and is unique ignoring case.</summary>
public sealed record BucketInput(string? Name);
