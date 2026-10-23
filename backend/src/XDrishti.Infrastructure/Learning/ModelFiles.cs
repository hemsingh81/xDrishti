using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Learning;
using XDrishti.Domain.Common;

namespace XDrishti.Infrastructure.Learning;

/// <summary>
/// The model files on disk (spec P6-07, ADR 0070): one file per hash, named by the hash (lower-case hex, nothing else is accepted, so a name cannot leave the folder). A write goes to a temporary file in the same folder and is renamed into place, so a reader sees
/// the whole file or none. A read hashes the bytes and refuses them if they are not the ones the name promises. The hash is integrity, not authenticity.
/// </summary>
internal sealed class ModelFiles(IOptions<ModelStoreOptions> options) : IModelFiles
{
    /// <summary>The largest file read back (the loader's own cap, ADR 0068).</summary>
    public const long MaxBytes = 64L * 1024 * 1024;

    public async ValueTask<Result> WriteAsync(string sha256, byte[] bytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (!IsHash(sha256) || bytes.Length is 0 || bytes.Length > MaxBytes || !Hashes(bytes, sha256))
        {
            return Result.Failure(ModelErrors.FileCorrupt(sha256 ?? string.Empty));
        }

        var folder = Path.GetFullPath(options.Value.Folder);
        var path = Path.Combine(folder, sha256);
        if (File.Exists(path) && await IntactAsync(path, sha256, cancellationToken))
        {
            return Result.Success();
        }

        // Shortcut: no fsync before the rename. A crash can leave a short file under the final name; the next read or write sees that it does not hash to its name and refuses it (write) or rewrites it.
        CreateFolder(folder);
        RemoveStaleTemps(folder, sha256);
        var temp = Path.Combine(folder, sha256 + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await using (var stream = new FileStream(temp, WriteOptions()))
            {
                await stream.WriteAsync(bytes, cancellationToken);
            }

            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }

        return Result.Success();
    }

    public async ValueTask<Result<byte[]>> ReadAsync(string sha256, CancellationToken cancellationToken)
    {
        if (!IsHash(sha256))
        {
            return Result.Failure<byte[]>(ModelErrors.FileCorrupt(sha256 ?? string.Empty));
        }

        var path = Path.Combine(Path.GetFullPath(options.Value.Folder), sha256);
        var bytes = await ReadCappedAsync(path, cancellationToken);
        if (bytes is null)
        {
            return Result.Failure<byte[]>(ModelErrors.FileMissing(sha256));
        }

        return bytes.Length is > 0 and <= (int)MaxBytes && Hashes(bytes, sha256) ? Result.Success(bytes) : Result.Failure<byte[]>(ModelErrors.FileCorrupt(sha256));
    }

    private static FileStreamOptions WriteOptions()
    {
        var o = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None, Options = FileOptions.Asynchronous };
        if (!OperatingSystem.IsWindows())
        {
            o.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        return o;
    }

    private static void CreateFolder(string folder)
    {
        if (Directory.Exists(folder))
        {
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(folder);
        }
        else
        {
            Directory.CreateDirectory(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    // A temporary file of this hash left by a crashed write; others in the folder are not touched.
    private static void RemoveStaleTemps(string folder, string sha256)
    {
        foreach (var stale in Directory.EnumerateFiles(folder, sha256 + ".*.tmp"))
        {
            try
            {
                File.Delete(stale);
            }
            catch (IOException)
            {
                // Another writer of the same model may be using it; it cleans up its own.
            }
        }
    }

    /// <summary>The bytes of the file through one handle (so the length checked is the length read), never more than its length plus one; empty when it is over <see cref="MaxBytes"/>; null when there is no file.</summary>
    private static async Task<byte[]?> ReadCappedAsync(string path, CancellationToken cancellationToken)
    {
        FileStream stream;
        try
        {
            stream = new FileStream(path, new FileStreamOptions { Mode = FileMode.Open, Access = FileAccess.Read, Share = FileShare.Read, Options = FileOptions.Asynchronous });
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }

        await using (stream)
        {
            if (stream.Length > MaxBytes)
            {
                return [];
            }

            var buffer = new byte[(int)stream.Length + 1];
            var read = 0;
            int n;
            while (read < buffer.Length && (n = await stream.ReadAsync(buffer.AsMemory(read), cancellationToken)) > 0)
            {
                read += n;
            }

            return read == buffer.Length ? buffer : buffer[..read];
        }
    }

    private static async Task<bool> IntactAsync(string path, string sha256, CancellationToken cancellationToken) =>
        await ReadCappedAsync(path, cancellationToken) is { Length: > 0 and <= (int)MaxBytes } bytes && Hashes(bytes, sha256);

    private static bool Hashes(byte[] bytes, string sha256) => string.Equals(Convert.ToHexStringLower(SHA256.HashData(bytes)), sha256, StringComparison.Ordinal);

    private static bool IsHash(string? value) => value is { Length: 64 } && value.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f'));
}
