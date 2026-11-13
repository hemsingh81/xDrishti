using System.Globalization;
using System.Text;
using Microsoft.Extensions.Options;
using XDrishti.Application.Abstractions.Market.Context;
using XDrishti.Infrastructure.Market.Bars;

namespace XDrishti.Infrastructure.Market.Context;

/// <summary>
/// The owner's drop folder of NSE daily CSVs (P3-01d fallback D, ADR 0290): lists the regular <c>.csv</c> files at the top of the folder (no symlink, no sub-folder, bounded size), reads them as strict UTF-8, and moves a handled file to <c>done/</c> or <c>failed/</c> with a
/// time prefix so nothing is overwritten or deleted. Local files only; nothing reaches a network.
/// </summary>
internal sealed class FolderContextInbox(IOptions<DataOptions> options, TimeProvider clock) : IContextInbox
{
    private const long MaxBytes = 8L * 1024 * 1024;

    private static readonly UTF8Encoding Strict = new(false, true);

    public bool Enabled => options.Value.Context.InboxEnabled;

    private string Folder => Path.GetFullPath(options.Value.Context.InboxPath);

    public ValueTask<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var folder = new DirectoryInfo(Folder);
        if (!folder.Exists)
        {
            return ValueTask.FromResult<IReadOnlyList<string>>([]);
        }

        IReadOnlyList<string> names =
        [
            .. folder.EnumerateFiles("*.csv", SearchOption.TopDirectoryOnly)
                .Where(f => f.LinkTarget is null)
                .Select(f => f.Name)
                .Order(StringComparer.Ordinal),
        ];
        return ValueTask.FromResult(names);
    }

    public async ValueTask<string?> ReadAsync(string name, CancellationToken cancellationToken)
    {
        var path = PathOf(name);
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.LinkTarget is not null || info.Length > MaxBytes)
            {
                return null;
            }

            var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
            return Strict.GetString(bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            return null;
        }
    }

    public ValueTask MoveAsync(string name, bool processed, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var target = Directory.CreateDirectory(Path.Combine(Folder, processed ? "done" : "failed"));
        var stamp = clock.GetUtcNow().ToString("yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture);
        var destination = Path.Combine(target.FullName, stamp + "_" + name);
        try
        {
            File.Move(PathOf(name), destination, overwrite: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The file stays in the inbox and is read again next run: the load is idempotent.
        }

        return ValueTask.CompletedTask;
    }

    private string PathOf(string name) => Path.Combine(Folder, Path.GetFileName(name));
}
