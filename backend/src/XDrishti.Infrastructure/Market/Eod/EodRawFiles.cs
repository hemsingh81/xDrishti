using Microsoft.Extensions.Options;
using XDrishti.Application.Abstractions.Market.Eod;

namespace XDrishti.Infrastructure.Market.Eod;

/// <summary>Writes the probe's raw files under <c>Data:History:ProbeFolder</c> (default <c>data/history-probe</c>, git-ignored; the deployment mounts a 0700 folder), mode 0600 in a 0700 folder. Names are fixed by the caller (letters, digits, dash, dot); a path separator in a name is refused.</summary>
internal sealed class EodRawFiles(IOptions<HistoryOptions> options) : IEodRawFiles
{

    public async ValueTask<string> SaveAsync(string name, byte[] content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(content);
        if (name.Length == 0 || !name.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '.'))
        {
            throw new ArgumentException("The file name has characters that are not allowed.", nameof(name));
        }

        var configured = options.Value.ProbeFolder;
        var folder = Path.GetFullPath(configured);
        var path = Path.Combine(folder, name);
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(folder);
        }
        else
        {
            Directory.CreateDirectory(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        await File.WriteAllBytesAsync(path, content, cancellationToken);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        return Path.Combine(configured, name);
    }
}
