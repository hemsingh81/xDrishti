namespace XDrishti.Api.Simulate.Assist;

/// <summary>
/// The pack folder on disk for the assisted check. It writes only the four files of the assisted check and the two of the gate (P3-09d), each through a temporary file and a move, and never <c>check-log.csv</c> or any file of the manifest; it reads nothing through a symbolic link and nothing above
/// <see cref="AssistBarsCsv.MaxBytes"/>.
/// </summary>
internal sealed class DiskPackFolder(string folder) : IPackFolder
{
    public const string LogName = "assist-log.csv";

    public const string ReportName = "assist-report.html";

    public const string ManifestName = "assist-manifest.json";

    private static readonly string[] Writable = [AssistBarsCsv.CacheName, LogName, ReportName, ManifestName, Gate.GateRunner.JsonName, Gate.GateRunner.MarkdownName];

    public byte[]? Read(string name)
    {
        var path = Path.Combine(folder, name);
        if (!File.Exists(path))
        {
            return null;
        }

        if (ExportPaths.IsLink(path) || new FileInfo(path).Length > AssistBarsCsv.MaxBytes)
        {
            throw new IOException(name + " is a symbolic link or is too large");
        }

        return File.ReadAllBytes(path);
    }

    public void Write(string name, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (!Writable.Contains(name, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("The assisted check never writes " + name);
        }

        var path = Path.Combine(folder, name);
        if (File.Exists(path) && ExportPaths.IsLink(path))
        {
            throw new IOException(name + " is a symbolic link");
        }

        var temporary = path + ".tmp";
        if (File.Exists(temporary) || ExportPaths.IsLink(temporary))
        {
            File.Delete(temporary); // a stale file or a link left there is removed, never written through
        }

        using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
        {
            stream.Write(bytes);
        }

        File.Move(temporary, path, true);
    }
}
