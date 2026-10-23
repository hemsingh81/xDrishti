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

        Directory.CreateDirectory(folder);
        var temp = Path.Combine(folder, sha256 + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await File.WriteAllBytesAsync(temp, bytes, cancellationToken);
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
        if (!File.Exists(path))
        {
            return Result.Failure<byte[]>(ModelErrors.FileMissing(sha256));
        }

        if (new FileInfo(path).Length > MaxBytes)
        {
            return Result.Failure<byte[]>(ModelErrors.FileCorrupt(sha256));
        }

        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        return Hashes(bytes, sha256) ? Result.Success(bytes) : Result.Failure<byte[]>(ModelErrors.FileCorrupt(sha256));
    }

    private static async Task<bool> IntactAsync(string path, string sha256, CancellationToken cancellationToken) =>
        new FileInfo(path).Length <= MaxBytes && Hashes(await File.ReadAllBytesAsync(path, cancellationToken), sha256);

    private static bool Hashes(byte[] bytes, string sha256) => string.Equals(Convert.ToHexStringLower(SHA256.HashData(bytes)), sha256, StringComparison.Ordinal);

    private static bool IsHash(string? value) => value is { Length: 64 } && value.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f'));
}
