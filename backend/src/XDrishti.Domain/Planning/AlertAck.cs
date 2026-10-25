namespace XDrishti.Domain.Planning;

/// <summary>One owner act on a notice, as stored (insert-only; the newest decides).</summary>
/// <param name="Id">Row id; a larger id is newer.</param>
/// <param name="Action"><c>snooze</c> or <c>dismiss</c>.</param>
/// <param name="Until">The end of a snooze.</param>
public sealed record AlertAck(long Id, string Action, DateTimeOffset? Until);
