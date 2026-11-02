using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using XDrishti.Application.Abstractions.Filings;
using XDrishti.Domain.Common;
using XDrishti.Infrastructure.Filings;

namespace XDrishti.Infrastructure.FilingArchive;

/// <summary>
/// The store folder <c>&lt;FilesPath&gt;/&lt;sha[0..2]&gt;/&lt;sha&gt;</c> (P15-02, ADR 0180): the only copy of a filing. Folders are created 0700 and files 0600. A copy is written under a temporary name in the same folder while it is hashed, re-read and hashed again, and only then
/// moved into place without replacing anything. A file that is already there with the same hash is left alone; a different file at that name is a conflict and is never replaced. The temporary file is the only file this class ever deletes (its own, on failure).
/// </summary>
internal sealed class FilingFiles(IOptions<FilingsOptions> options) : IFilingFiles
{
    private const UnixFileMode OwnerFolder = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private const UnixFileMode OwnerFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    private string Root => Path.GetFullPath(options.Value.FilesPath);

    public ValueTask<long> UsedBytesAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(Root))
        {
            return ValueTask.FromResult(0L);
        }

        long total = 0;
        foreach (var file in new DirectoryInfo(Root).EnumerateFiles("*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            total += file.Length;
        }

        return ValueTask.FromResult(total);
    }

    public long? FreeBytes()
    {
        try
        {
            var probe = Root;
            while (!Directory.Exists(probe) && Path.GetDirectoryName(probe) is { } parent)
            {
                probe = parent;
            }

            return new DriveInfo(probe).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    public ValueTask<Result<bool>> CopyAsync(Stream source, string sha256, long expectedBytes, CancellationToken cancellationToken) => CopyCoreAsync(source, sha256, expectedBytes, true, cancellationToken);

    public ValueTask<Result<bool>> CopyRebuildableAsync(Stream source, string sha256, long expectedBytes, CancellationToken cancellationToken) => CopyCoreAsync(source, sha256, expectedBytes, false, cancellationToken);

    private async ValueTask<Result<bool>> CopyCoreAsync(Stream source, string sha256, long expectedBytes, bool durable, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (sha256.Length != 64 || !sha256.All(char.IsAsciiHexDigitLower))
        {
            return Failure("unreadable");
        }

        var folder = Path.Combine(Root, sha256[..2]);
        var target = Path.Combine(folder, sha256);
        EnsureFolder(Root);
        EnsureFolder(folder);
        var replace = false;
        if (File.Exists(target))
        {
            if (await HashAsync(target, cancellationToken) == sha256)
            {
                return Result.Success(false);
            }

            if (durable)
            {
                return Failure("store_conflict");
            }

            replace = true; // a rebuildable copy that does not hash to its own name was lost before it reached the disk: the right bytes take its place
        }

        var temp = Path.Combine(folder, ".tmp-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                await using (var output = new FileStream(temp, CreateOptions()))
                {
                    var buffer = new byte[81920];
                    long total = 0;
                    int read;
                    while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                    {
                        total += read;
                        if (total > expectedBytes)
                        {
                            return Failure("content_changed");
                        }

                        hasher.AppendData(buffer, 0, read);
                        await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    }

                    await output.FlushAsync(cancellationToken);
                    if (durable)
                    {
                        output.Flush(true);
                    }
                    if (total != expectedBytes)
                    {
                        return Failure("content_changed");
                    }
                }

                if (!string.Equals(Convert.ToHexStringLower(hasher.GetHashAndReset()), sha256, StringComparison.Ordinal))
                {
                    return Failure("content_changed");
                }
            }

            if (durable && await HashAsync(temp, cancellationToken) != sha256)
            {
                return Failure("unreadable");
            }

            try
            {
                File.Move(temp, target, overwrite: replace);
            }
            catch (IOException) when (File.Exists(target))
            {
                // Another host put the same name there first: fine when it holds the same bytes.
                return await HashAsync(target, cancellationToken) == sha256 ? Result.Success(false) : Failure("store_conflict");
            }

            return Result.Success(true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    public async ValueTask<Result<Stream>> OpenAsync(string sha256, CancellationToken cancellationToken)
    {
        if (sha256.Length != 64 || !sha256.All(char.IsAsciiHexDigitLower))
        {
            return Result.Failure<Stream>(Error.Validation("unreadable", "The stored file could not be read."));
        }

        var path = Path.Combine(Root, sha256[..2], sha256);
        try
        {
            if (!File.Exists(path))
            {
                return Result.Failure<Stream>(Error.Validation("unreadable", "The stored file could not be read."));
            }

            // Opened once: the hash is taken from this very stream, then it is rewound and handed out.
            var stream = new FileStream(path, System.IO.FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
            if (Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken)) != sha256)
            {
                await stream.DisposeAsync();
                return Result.Failure<Stream>(Error.Validation("content_changed", "The stored file does not match its hash."));
            }

            stream.Position = 0;
            return Result.Success<Stream>(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result.Failure<Stream>(Error.Validation("unreadable", "The stored file could not be read."));
        }
    }

    private static FileStreamOptions CreateOptions()
    {
        var create = new FileStreamOptions { Mode = System.IO.FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None, Options = FileOptions.Asynchronous };
        if (!OperatingSystem.IsWindows())
        {
            create.UnixCreateMode = OwnerFile;
        }

        return create;
    }

    private static Result<bool> Failure(string code) => Result.Failure<bool>(Error.Validation(code, "The file could not be stored: " + code + "."));

    private static void EnsureFolder(string path)
    {
        if (Directory.Exists(path))
        {
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(path);
        }
        else
        {
            Directory.CreateDirectory(path, OwnerFolder); // also the parents, never with the umask's permissions
        }
    }

    private static async ValueTask<string> HashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, System.IO.FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken));
    }
}
