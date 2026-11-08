using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using XDrishti.Application.Abstractions.Shadow;
using XDrishti.Application.Shadow;

namespace XDrishti.Infrastructure.Paper;

/// <summary>
/// The digest log outside the database (<see cref="IShadowDigestSink"/>, spec CF-08 review fix 4, ADR 0232): one text line per export in <c>shadow-digest.log</c> in <c>Shadow:DigestFolder</c>. Every line ends with <c>prev=</c> and the SHA-256 of the whole previous line
/// (64 zeros for the first), checked on every read, so an edited or removed line in the middle is found. Append only: one <see cref="FileStream"/> in append mode, the line written from one buffer and flushed to disk. A line without its newline (a torn write) reads
/// as a problem and nothing is appended after it. The file is evidence to compare with <c>xd paper shadow verify</c>, not authentication.
/// </summary>
internal sealed class ShadowDigestFile(IOptions<ShadowOptions> options) : IShadowDigestSink
{
    public const string FileName = "shadow-digest.log";

    private static readonly string Zero = new('0', 64);

    // shortcut: cutting lines off the END of the log leaves a valid chain, so truncation of the tail is not detected here; only a copy of the newest line kept elsewhere (the owner's offsite copy) can show it (ADR 0232).
    private string Path => System.IO.Path.Combine(System.IO.Path.GetFullPath(options.Value.DigestFolder), FileName);

    public async ValueTask<ShadowDigestLogRead> ReadAsync(CancellationToken cancellationToken)
    {
        var path = Path;
        if (!File.Exists(path))
        {
            return new ShadowDigestLogRead(false, [], null);
        }

        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return new ShadowDigestLogRead(true, [], "The digest log cannot be read: " + failure.GetType().Name + ".");
        }

        return Parse(Encoding.UTF8.GetString(bytes));
    }

    public async ValueTask<bool> AppendAsync(ShadowDigestLine line, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(line);
        var read = await ReadAsync(cancellationToken);
        if (read.Problem is { } problem)
        {
            throw new InvalidOperationException(problem);
        }

        if (read.Lines.Count > 0 && read.Lines[^1].LastSession == line.LastSession && read.Lines[^1].Digest == line.Digest)
        {
            return false;
        }

        var prev = HashOfLast(read.Lines);
        var bytes = Encoding.UTF8.GetBytes(Format(line, prev) + "\n");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        await using var stream = new FileStream(Path, FileMode.Append, FileAccess.Write, FileShare.Read, 1, FileOptions.None);
        await stream.WriteAsync(bytes, cancellationToken);
        stream.Flush(true);
        return true;
    }

    public ValueTask<string?> CheckWritableAsync(CancellationToken cancellationToken)
    {
        try
        {
            var folder = System.IO.Path.GetDirectoryName(Path)!;
            Directory.CreateDirectory(folder);
            var probe = System.IO.Path.Combine(folder, ".probe-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(probe, "probe");
            File.Delete(probe);
            if (File.Exists(Path))
            {
                using var append = new FileStream(Path, FileMode.Append, FileAccess.Write, FileShare.Read); // the log itself must be appendable (a file the folder lets us create in may still be read-only)
            }

            return ValueTask.FromResult<string?>(null);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return ValueTask.FromResult<string?>("The digest folder (Shadow:DigestFolder) cannot be written by this process: " + failure.GetType().Name + ".");
        }
    }

    private static string Format(ShadowDigestLine l, string prev) =>
        string.Create(CultureInfo.InvariantCulture, $"week={l.Week} last_session={l.LastSession:yyyy-MM-dd} digest={l.Digest} sessions={l.Sessions} gaps={l.Gaps} written_at={l.WrittenAt.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ} prev={prev}");

    private static string HashOf(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static string HashOfLast(IReadOnlyList<ShadowDigestLine> lines)
    {
        var prev = Zero;
        foreach (var l in lines)
        {
            prev = HashOf(Format(l, prev));
        }

        return prev;
    }

    private static ShadowDigestLogRead Parse(string text)
    {
        var lines = new List<ShadowDigestLine>();
        if (text.Length == 0)
        {
            return new ShadowDigestLogRead(true, lines, null);
        }

        if (!text.EndsWith('\n'))
        {
            return new ShadowDigestLogRead(true, lines, "The last line of the digest log has no newline (a torn write); nothing was appended.");
        }

        var prev = Zero;
        var number = 0;
        foreach (var raw in text[..^1].Split('\n'))
        {
            number++;
            var parsed = TryParse(raw);
            if (parsed is null)
            {
                return new ShadowDigestLogRead(true, lines, $"Line {number} of the digest log is malformed.");
            }

            if (parsed.Value.Prev != prev || Format(parsed.Value.Line, parsed.Value.Prev) != raw)
            {
                return new ShadowDigestLogRead(true, lines, $"Line {number} of the digest log does not follow the line before it: the log was edited.");
            }

            lines.Add(parsed.Value.Line);
            prev = HashOf(raw);
        }

        return new ShadowDigestLogRead(true, lines, null);
    }

    private static (ShadowDigestLine Line, string Prev)? TryParse(string raw)
    {
        var parts = raw.Split(' ');
        if (parts.Length != 7)
        {
            return null;
        }

        string? Value(int i, string key) => parts[i].StartsWith(key + "=", StringComparison.Ordinal) ? parts[i][(key.Length + 1)..] : null;
        var week = Value(0, "week");
        var digest = Value(2, "digest");
        var prev = Value(6, "prev");
        if (week is null || digest is null || prev is null
            || !DateOnly.TryParseExact(Value(1, "last_session"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var session)
            || !int.TryParse(Value(3, "sessions"), NumberStyles.None, CultureInfo.InvariantCulture, out var sessions)
            || !int.TryParse(Value(4, "gaps"), NumberStyles.None, CultureInfo.InvariantCulture, out var gaps)
            || !DateTimeOffset.TryParseExact(Value(5, "written_at"), "yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at))
        {
            return null;
        }

        return (new ShadowDigestLine(week, session, digest, sessions, gaps, at), prev);
    }
}
