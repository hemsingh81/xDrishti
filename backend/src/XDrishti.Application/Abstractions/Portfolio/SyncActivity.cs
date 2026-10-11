namespace XDrishti.Application.Abstractions.Portfolio;

/// <summary>
/// When an account was last synced automatically. <see cref="LastAutoAttemptAt"/>: the start of the latest automatic run including failed and skipped ones
/// (the first automatic sync of an account without holdings is recorded as an import, and counts too).
/// </summary>
public sealed record SyncActivity(DateTimeOffset? LastAutoAttemptAt);
