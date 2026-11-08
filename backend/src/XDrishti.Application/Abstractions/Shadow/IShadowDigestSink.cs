namespace XDrishti.Application.Abstractions.Shadow;

/// <summary>One line of the digest log kept outside the database.</summary>
/// <param name="Week">The ISO week the line was written for, <c>yyyy-Www</c> (India time).</param>
/// <param name="LastSession">The newest session of the chain.</param>
/// <param name="Digest">Its digest.</param>
/// <param name="Sessions">Stored sessions.</param>
/// <param name="Gaps">Of those, gap sessions.</param>
/// <param name="WrittenAt">When the line was written.</param>
public sealed record ShadowDigestLine(string Week, DateOnly LastSession, string Digest, int Sessions, int Gaps, DateTimeOffset WrittenAt);

/// <summary>The log as read: whether the file exists, its lines, and a problem when the file cannot be trusted (unreadable, a malformed or torn line, a broken line chain).</summary>
/// <param name="Exists">Whether the file exists.</param>
/// <param name="Lines">The lines read, oldest first (up to a problem).</param>
/// <param name="Problem">Why the log cannot be trusted; null when it reads cleanly.</param>
public sealed record ShadowDigestLogRead(bool Exists, IReadOnlyList<ShadowDigestLine> Lines, string? Problem);

/// <summary>The copy of the chain head kept outside the database (spec CF-08 review fix 4, ADR 0232): append only, never rewritten; each line carries the SHA-256 of the line before it.</summary>
public interface IShadowDigestSink
{
    /// <summary>Reads the log and verifies its line chain.</summary>
    ValueTask<ShadowDigestLogRead> ReadAsync(CancellationToken cancellationToken);

    /// <summary>Appends the line; false (nothing written) when the newest line already holds this last session and digest. Throws when the log does not read cleanly: callers read and check first.</summary>
    ValueTask<bool> AppendAsync(ShadowDigestLine line, CancellationToken cancellationToken);

    /// <summary>Whether the folder can be written by this process (creates it, writes and removes a probe file); null when it can, else the reason.</summary>
    ValueTask<string?> CheckWritableAsync(CancellationToken cancellationToken);
}
