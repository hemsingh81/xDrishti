using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using XDrishti.Infrastructure.Filings;

namespace XDrishti.Infrastructure.Tests.Filings;

/// <summary>A temporary inbox (0700) with helpers to drop files and sidecars, and the adapter over it. The clock is fixed at 2026-11-02 12:00 UTC.</summary>
internal sealed class InboxFixture : IDisposable
{
    public static readonly DateTimeOffset Now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    private static readonly UTF8Encoding NoBom = new(false);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "xd-filings-" + Guid.NewGuid().ToString("N"));

    public InboxFixture(int maxFileMb = 20, int maxFiles = 5_000)
    {
        Inbox = Path.Combine(_root, "inbox");
        Directory.CreateDirectory(Inbox);
        Lock();
        Source = new FolderDropFilingsSource(Options.Create(new FilingsOptions { InboxPath = Inbox, MaxFileMb = maxFileMb, MaxFiles = maxFiles }), new Microsoft.Extensions.Time.Testing.FakeTimeProvider(Now), NullLogger<FolderDropFilingsSource>.Instance);
    }

    public string Inbox { get; }

    public string Root => _root;

    public FolderDropFilingsSource Source { get; }

    public static string Sidecar(string symbol = "RELIANCE", string kind = "results", string? period = "2026-06-30", string? broadcast = "2026-07-24T16:12:00+05:30") =>
        $$"""{"schema":1,"symbol":"{{symbol}}","kind":"{{kind}}"{{(period is null ? "" : $",\"period_end\":\"{period}\"")}}{{(broadcast is null ? "" : $",\"broadcast_ts\":\"{broadcast}\"")}}}""";

    public string Drop(string name, string content, string? sidecar = null)
    {
        var path = Path.Combine(Inbox, name);
        File.WriteAllText(path, content, NoBom);
        if (sidecar is not null)
        {
            File.WriteAllText(path + ".meta.json", sidecar, NoBom);
        }

        return path;
    }

    public void Lock()
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(Inbox, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    /// <summary>Name, size, modified time, mode and content hash of everything in the inbox, to prove a scan changed nothing.</summary>
    public List<string> Snapshot() =>
        [.. new DirectoryInfo(Inbox).EnumerateFileSystemInfos().OrderBy(i => i.Name, StringComparer.Ordinal).Select(i =>
            $"{i.Name}|{(i is FileInfo f ? f.Length : -1)}|{i.LastWriteTimeUtc.Ticks}|{(OperatingSystem.IsWindows() ? 0 : (int)File.GetUnixFileMode(i.FullName))}|{(i is FileInfo g && i.LinkTarget is null ? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(g.FullName))) : "-")}")];

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            if (!OperatingSystem.IsWindows() && Directory.Exists(Inbox))
            {
                File.SetUnixFileMode(Inbox, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            Directory.Delete(_root, recursive: true);
        }
    }
}
