using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using XDrishti.Application.Abstractions.Filings;
using XDrishti.Domain.Common;
using XDrishti.Domain.Filings;

namespace XDrishti.Infrastructure.Filings;

/// <summary>
/// The filings source that reads the owner's inbox folder (P15-01, ADR 0179). It never writes, moves or deletes there, follows no symlink, reads no sub-folder, trusts neither a file name nor a modified time, and reaches no network. A file is accepted only with a valid
/// sidecar; every other file is listed as rejected with a closed reason. File bytes are hashed by streaming and never parsed. It holds no state between calls.
/// </summary>
internal sealed class FolderDropFilingsSource(IOptions<FilingsOptions> options, TimeProvider time, ILogger<FolderDropFilingsSource> logger) : IFilingsSource
{
    private const int BufferBytes = 81_920;

    private const UnixFileMode LooseBits = UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;

    /// <summary>A test seam called with the file's path after its size was measured and before it is hashed; null in production.</summary>
    internal Action<string>? BeforeHash { get; set; }

    private FilingsOptions Settings => options.Value;

    public async ValueTask<FilingListing> ListAsync(FilingQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var listing = await ScanAsync(cancellationToken).ConfigureAwait(false);
        var filtered = new FilingListing([.. listing.Accepted.Where(e => Matches(e, query))], listing.Rejected, listing.Problems, listing.Truncated);
        var problems = string.Join(',', filtered.Problems);
        var reasons = Reasons(filtered.Rejected);
        FilingsLog.Scan(logger, filtered.Accepted.Count, filtered.Rejected.Count, filtered.Truncated, problems, reasons);
        return filtered;
    }

    public ValueTask<Result<Stream>> OpenAsync(FilingRef filing, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filing);
        cancellationToken.ThrowIfCancellationRequested();
        if (!FilingNames.IsValidName(filing.FileName) || FilingNames.AcceptedExtension(filing.FileName) is null || !IsHex64(filing.Sha256))
        {
            return Fail(FilingRejectReasons.BadName);
        }

        if (InboxProblem(new DirectoryInfo(Path.GetFullPath(Settings.InboxPath))) is not null)
        {
            return Fail(FilingRejectReasons.Unreadable);
        }

        var path = Path.Combine(Path.GetFullPath(Settings.InboxPath), filing.FileName);
        var info = new FileInfo(path);
        if (!IsRegularFile(info) || info.Length == 0 || info.Length > MaxBytes)
        {
            return Fail(FilingRejectReasons.Unreadable);
        }

        try
        {
            var stream = new FileStream(path, new FileStreamOptions { Mode = FileMode.Open, Access = FileAccess.Read, Share = FileShare.Read, BufferSize = BufferBytes, Options = FileOptions.SequentialScan | FileOptions.Asynchronous });
            if (stream.Length != info.Length || !IsRegularFile(new FileInfo(path)))
            {
                stream.Dispose();
                return Fail(FilingRejectReasons.Unreadable);
            }

            return ValueTask.FromResult(Result.Success<Stream>(new VerifyingReadStream(stream, filing.Sha256, info.Length)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Fail(FilingRejectReasons.Unreadable);
        }
    }

    private long MaxBytes => Settings.MaxFileMb * 1024L * 1024L;

    private static ValueTask<Result<Stream>> Fail(string code) => ValueTask.FromResult(Result.Failure<Stream>(Error.NotFound(code, code)));

    private static bool IsHex64(string value) => value.Length == 64 && value.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f'));

    private static bool IsRegularFile(FileSystemInfo info) =>
        info is FileInfo { Exists: true } && info.LinkTarget is null && !info.Attributes.HasFlag(FileAttributes.ReparsePoint) && !info.Attributes.HasFlag(FileAttributes.Directory);

    private static bool Matches(FilingEntry entry, FilingQuery query) =>
        (query.Symbol is null || string.Equals(entry.Symbol, query.Symbol, StringComparison.Ordinal))
        && (query.Kind is null || entry.Kind == query.Kind)
        && ((query.BroadcastFrom is null && query.BroadcastTo is null)
            || (entry.BroadcastTs is { } ts && (query.BroadcastFrom is null || ts >= query.BroadcastFrom) && (query.BroadcastTo is null || ts <= query.BroadcastTo)));

    private static string Reasons(IReadOnlyList<RejectedFiling> rejected) =>
        string.Join(',', rejected.GroupBy(r => r.Reason, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => g.Key + ":" + g.Count().ToString(System.Globalization.CultureInfo.InvariantCulture)));

    /// <summary>The problem of the inbox folder itself, or null when it may be read.</summary>
    private static string? InboxProblem(DirectoryInfo inbox)
    {
        if (!inbox.Exists)
        {
            return FilingRejectReasons.InboxMissing;
        }

        if (inbox.LinkTarget is not null || inbox.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            return FilingRejectReasons.InboxOutside;
        }

        if (!OperatingSystem.IsWindows() && (File.GetUnixFileMode(inbox.FullName) & LooseBits) != 0)
        {
            return FilingRejectReasons.InboxPermissions;
        }

        return null;
    }

    private async ValueTask<FilingListing> ScanAsync(CancellationToken cancellationToken)
    {
        var inbox = new DirectoryInfo(Path.GetFullPath(Settings.InboxPath));
        if (InboxProblem(inbox) is { } problem)
        {
            return FilingListing.Empty(problem);
        }

        List<FileSystemInfo> entries;
        try
        {
            entries = [.. inbox.EnumerateFileSystemInfos().OrderBy(i => i.Name, StringComparer.Ordinal)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return FilingListing.Empty(FilingRejectReasons.Unreadable);
        }

        var sidecars = entries.Where(e => FilingNames.IsSidecarName(e.Name)).ToDictionary(e => e.Name, StringComparer.Ordinal);
        var files = entries.Where(e => !FilingNames.IsSidecarName(e.Name)).ToList();
        var fileNames = files.Select(f => f.Name).ToHashSet(StringComparer.Ordinal);
        var accepted = new List<FilingEntry>();
        var rejected = new List<RejectedFiling>();
        var truncated = false;
        var now = time.GetUtcNow();

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (accepted.Count + rejected.Count >= Settings.MaxFiles)
            {
                truncated = true;
                break;
            }

            var (entry, rejection) = await ExamineAsync(inbox.FullName, file, sidecars, now, cancellationToken).ConfigureAwait(false);
            if (entry is not null)
            {
                accepted.Add(entry);
            }
            else
            {
                rejected.Add(rejection!);
            }
        }

        foreach (var sidecar in sidecars.Values)
        {
            var baseName = sidecar.Name[..^FilingNames.SidecarSuffix.Length];
            if (fileNames.Contains(baseName))
            {
                continue;
            }

            if (accepted.Count + rejected.Count >= Settings.MaxFiles)
            {
                truncated = true;
                break;
            }

            rejected.Add(new RejectedFiling(sidecar.Name, FilingRejectReasons.OrphanSidecar, null));
        }

        accepted.Sort(CompareEntries);
        rejected.Sort((a, b) => string.CompareOrdinal(a.FileName, b.FileName));
        return new FilingListing(accepted, rejected, [], truncated);
    }

    private static int CompareEntries(FilingEntry a, FilingEntry b)
    {
        var order = string.CompareOrdinal(a.Symbol, b.Symbol);
        if (order != 0)
        {
            return order;
        }

        order = string.CompareOrdinal(FilingKinds.Code(a.Kind), FilingKinds.Code(b.Kind));
        if (order != 0)
        {
            return order;
        }

        order = string.CompareOrdinal(a.PeriodEnd?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), b.PeriodEnd?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        return order != 0 ? order : string.CompareOrdinal(a.Sha256, b.Sha256);
    }

    private async ValueTask<(FilingEntry? Entry, RejectedFiling? Rejection)> ExamineAsync(string inboxPath, FileSystemInfo file, Dictionary<string, FileSystemInfo> sidecars, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!IsRegularFile(file))
        {
            return Reject(file, FilingRejectReasons.NotARegularFile);
        }

        if (!FilingNames.IsValidName(file.Name))
        {
            return Reject(file, FilingRejectReasons.BadName);
        }

        var extension = FilingNames.AcceptedExtension(file.Name);
        if (extension is null)
        {
            return Reject(file, FilingRejectReasons.BadExtension);
        }

        var size = ((FileInfo)file).Length;
        if (size == 0)
        {
            return Reject(file, FilingRejectReasons.Empty);
        }

        if (size > MaxBytes)
        {
            return Reject(file, FilingRejectReasons.TooLarge);
        }

        if (!sidecars.TryGetValue(FilingNames.SidecarName(file.Name), out var sidecarInfo))
        {
            return Reject(file, FilingRejectReasons.NoSidecar);
        }

        var parsed = await ReadSidecarAsync(sidecarInfo, extension, now, cancellationToken).ConfigureAwait(false);
        if (parsed.IsFailure)
        {
            return Reject(file, FilingRejectReasons.InvalidSidecar, parsed.Error.Code);
        }

        BeforeHash?.Invoke(Path.Combine(inboxPath, file.Name));
        var sha = await HashAsync(Path.Combine(inboxPath, file.Name), size, cancellationToken).ConfigureAwait(false);
        if (sha is null)
        {
            return Reject(file, FilingRejectReasons.Unreadable);
        }

        var sidecar = parsed.Value;
        return (new FilingEntry(sidecar.Symbol, sidecar.Kind, sidecar.PeriodEnd, sidecar.Broadcast.Instant, sidecar.Broadcast.State, sidecar.Broadcast.Reason, file.Name, size, sha), null);
    }

    private static (FilingEntry?, RejectedFiling?) Reject(FileSystemInfo file, string reason, string? detail = null) => (null, new RejectedFiling(file.Name, reason, detail));

    private static async ValueTask<Result<FilingSidecar>> ReadSidecarAsync(FileSystemInfo sidecar, string extension, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!IsRegularFile(sidecar))
        {
            return Result.Failure<FilingSidecar>(Error.Validation("sidecar_not_regular", "sidecar_not_regular"));
        }

        if (((FileInfo)sidecar).Length == 0)
        {
            return Result.Failure<FilingSidecar>(Error.Validation("sidecar_empty", "sidecar_empty")); // never opened: a pipe reports length 0 and opening it would block
        }

        if (((FileInfo)sidecar).Length > FilingNames.MaxSidecarBytes)
        {
            return Result.Failure<FilingSidecar>(Error.Validation("sidecar_too_large", "sidecar_too_large"));
        }

        try
        {
            await using var stream = new FileStream(sidecar.FullName, new FileStreamOptions { Mode = FileMode.Open, Access = FileAccess.Read, Share = FileShare.Read, BufferSize = 1 });
            var buffer = new byte[FilingNames.MaxSidecarBytes + 1];
            var total = 0;
            int read;
            while (total < buffer.Length && (read = await stream.ReadAsync(buffer.AsMemory(total), cancellationToken).ConfigureAwait(false)) > 0)
            {
                total += read;
            }

            return FilingSidecar.Parse(buffer.AsSpan(0, total), extension, now);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result.Failure<FilingSidecar>(Error.Validation("sidecar_unreadable", "sidecar_unreadable"));
        }
    }

    /// <summary>The lowercase hex SHA-256 of the file read by streaming, or null when it cannot be read or is not the size it had when it was measured.</summary>
    private static async ValueTask<string?> HashAsync(string path, long expectedSize, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(path, new FileStreamOptions { Mode = FileMode.Open, Access = FileAccess.Read, Share = FileShare.Read, BufferSize = 1, Options = FileOptions.SequentialScan | FileOptions.Asynchronous });
            if (stream.Length != expectedSize || !IsRegularFile(new FileInfo(path)))
            {
                return null;
            }

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[BufferBytes];
            long total = 0;
            int read;
            while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                total += read;
                if (total > expectedSize)
                {
                    return null;
                }

                hash.AppendData(buffer, 0, read);
            }

            return total == expectedSize ? Convert.ToHexStringLower(hash.GetHashAndReset()) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
