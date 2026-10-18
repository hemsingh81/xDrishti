using System.Security.Cryptography;
using System.Text;
using XDrishti.Api.Simulate;
using XDrishti.SimExport.Manifest;

namespace XDrishti.Api.IntegrationTests.Simulate;

/// <summary>A folder under the system temp path that a test owns: made on construction, removed with everything in it on disposal. The writer tests use it for the export root (plain temp-directory tests, outside the Testcontainers collection).</summary>
internal sealed class TempFolder : IDisposable
{
    public TempFolder(string? name = null)
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "xd-export-" + Guid.NewGuid().ToString("N")[..12], name ?? "case");
        Directory.CreateDirectory(Path);
    }

    /// <summary>The folder; its parent exists.</summary>
    public string Path { get; }

    /// <summary>A path inside the folder that does not exist yet, to be used as an export root.</summary>
    public string Root => System.IO.Path.Combine(Path, "exports");

    public void Dispose()
    {
        var top = System.IO.Path.GetDirectoryName(Path)!;
        if (Directory.Exists(top))
        {
            foreach (var entry in new DirectoryInfo(top).EnumerateFileSystemInfos("*", SearchOption.AllDirectories).Where(e => e.LinkTarget is not null))
            {
                entry.Delete();
            }

            Directory.Delete(top, recursive: true);
        }
    }
}

/// <summary>The four files of the slice, fabricated, and the manifest function the writer needs; the run key and the format are fixed so that two runs are comparable.</summary>
internal static class ExportFixture
{
    public const string Format = "xd-sim-export-1";

    public const string RunKey = "3fa91c2d07be0000000000000000000000000000000000000000000000000000";

    public const string RunId = "orb-20240401-20260430-3fa91c2d07be";

    public static readonly Guid Basket = new("11111111-2222-3333-4444-555555555555");

    public static IReadOnlyList<ExportFile> Files(string tag = "") =>
    [
        Text("README.txt", "readme " + tag + "\n"),
        Text("open_at_end.csv", "run_key,key\n"),
        Text("run.json", "{\"run\":\"" + tag + "\"}"),
        Text("trades.csv", "run_key,key\n" + RunKey + ",ORB|NSE:AAA|Buy|2026-03-02\n"),
    ];

    public static ExportFile Text(string name, string content) =>
        new(name, sink =>
        {
            var bytes = Encoding.UTF8.GetBytes(content);
            bytes.CopyTo(sink.GetSpan(bytes.Length));
            sink.Advance(bytes.Length);
        });

    /// <summary>The manifest function of the real exporter: the manifest of the entries the writer produced, and its key.</summary>
    public static Func<IReadOnlyList<ManifestEntry>, (byte[] Manifest, string ExportKey)> BuildManifest(Guid? basket = null, string[]? extra = null) =>
        entries => (ExportManifest.Build(Format, RunKey, basket, entries, extra ?? []), ExportKey.Of(Format, RunKey, basket, entries, extra ?? []));

    public static async Task<XDrishti.Domain.Common.Result<ExportOutcome>> WriteAsync(string root, IReadOnlyList<ExportFile>? files = null, bool force = false, Guid? basket = null, string runId = RunId, ExportFolderWriter? writer = null, CancellationTokenSource? cancel = null) =>
        await (writer ?? new ExportFolderWriter()).WriteAsync(root, runId, files ?? Files(), BuildManifest(basket), force, cancel?.Token ?? TestContext.Current.CancellationToken);

    public static string Sha256(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    /// <summary>Every file and folder under the path, relative, sorted, with a slash at the end of a folder: the whole tree, to compare before and after.</summary>
    public static IReadOnlyList<string> Tree(string path) =>
    [
        .. new DirectoryInfo(path).EnumerateFileSystemInfos("*", SearchOption.AllDirectories)
            .Select(e => System.IO.Path.GetRelativePath(path, e.FullName) + (e is DirectoryInfo ? "/" : string.Empty))
            .Order(StringComparer.Ordinal),
    ];
}
