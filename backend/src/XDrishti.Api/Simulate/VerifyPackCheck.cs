using System.Security.Cryptography;
using System.Text;
using XDrishti.Domain.Common;
using XDrishti.SimExport.Manifest;
using XDrishti.SimExport.Verification;

namespace XDrishti.Api.Simulate;

/// <summary>
/// The check mode of <c>verify-pack</c> (spec P3-09, V8): reads <c>check-log.csv</c> and <c>engine-values.csv</c> of one pack folder and prints the comparison of <see cref="CheckLogReader"/>. It writes nothing and reads no database. The two paths are
/// derived from the folder of the log the owner names, which must be called <c>check-log.csv</c>; a symbolic link is refused (the log, the engine values, the manifest, the folder and every file the manifest lists), the log is capped at 5 MB and the
/// engine values at 20 MB, and the manifest is verified for every file <b>except</b> <c>check-log.csv</c>, the one file the owner edits: a card, the index, <c>pack.json</c>, the README or <c>engine-values.csv</c> that differs from it is exit 2 with the file named.
/// Exit 0 when the pack is complete, 1 when it is not, 2 for a usage problem, an input that cannot be used or a changed pack file.
/// </summary>
internal static class VerifyPackCheck
{
    /// <summary>The largest log read.</summary>
    public const long MaxLogBytes = 5L * 1024 * 1024;

    /// <summary>The largest engine-values file read.</summary>
    public const long MaxEngineBytes = 20L * 1024 * 1024;

    private const long MaxManifestBytes = 4L * 1024 * 1024;

    public static async Task<int> RunAsync(string path, SimulateConsole console, ILogger logger, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(logger);

        var located = Locate(path);
        if (located.IsFailure)
        {
            return await Refuse(console, located.Error.Message);
        }

        var (log, engine, folder) = located.Value;
        var manifest = VerifyManifest(folder);
        if (manifest is not null)
        {
            return await Refuse(console, manifest);
        }

        string logText;
        string logHash;
        string engineText;
        try
        {
            var logBytes = await File.ReadAllBytesAsync(log, cancellationToken);
            logText = new UTF8Encoding(false, true).GetString(logBytes);
            logHash = Convert.ToHexStringLower(SHA256.HashData(logBytes));
            engineText = await ReadTextAsync(engine, cancellationToken);
        }
        catch (Exception bad) when (bad is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            return await Refuse(console, "a file of the pack cannot be read as UTF-8 text: " + bad.GetType().Name);
        }

        var report = CheckLogReader.Read(logText, engineText);
        if (report.Problems.Count > 0)
        {
            foreach (var problem in report.Problems.Take(SimulateCommand.MaxProblems))
            {
                await console.Error.WriteLineAsync(problem);
            }

            return SimulateCommand.UsageError;
        }

        await console.Out.WriteLineAsync("log sha256: " + logHash + " (copy or commit the first-pass log before you edit it again: it is the evidence)");
        foreach (var line in report.Lines)
        {
            await console.Out.WriteLineAsync(line);
        }

        SimulateLog.CheckRead(logger, report.Rows, report.Complete);
        return report.Complete ? SimulateCommand.Done : SimulateCommand.Refused;
    }

    private static async Task<int> Refuse(SimulateConsole console, string message)
    {
        await console.Error.WriteLineAsync(message);
        return SimulateCommand.UsageError;
    }

    private static Result<(string Log, string Engine, string Folder)> Locate(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Any(char.IsControl))
        {
            return Fail("the path of the log is empty or has a control character");
        }

        var log = Path.GetFullPath(path);
        var folder = Path.GetDirectoryName(log);
        if (folder is null || !string.Equals(Path.GetFileName(log), PackFiles.CheckLogName, StringComparison.Ordinal))
        {
            return Fail("give the path of " + PackFiles.CheckLogName + " in the pack folder");
        }

        var engine = Path.Combine(folder, PackFiles.EngineValuesName);
        if (!Directory.Exists(folder) || ExportPaths.IsLink(folder))
        {
            return Fail("the pack folder does not exist or is a symbolic link");
        }

        foreach (var (file, cap) in new[] { (log, MaxLogBytes), (engine, MaxEngineBytes) })
        {
            var name = Path.GetFileName(file);
            if (ExportPaths.IsLink(file))
            {
                return Fail(name + " is a symbolic link; it is never read through one");
            }

            if (!File.Exists(file))
            {
                return Fail(name + " is not in the pack folder");
            }

            if (new FileInfo(file).Length > cap)
            {
                return Fail(name + " is larger than " + (cap / (1024 * 1024)).ToString(System.Globalization.CultureInfo.InvariantCulture) + " MB");
            }
        }

        return Result.Success((log, engine, folder));
    }

    private static Result<(string Log, string Engine, string Folder)> Fail(string message) =>
        Result.Failure<(string Log, string Engine, string Folder)>(Error.Validation("verify_pack.check_input", message));

    /// <summary>Null when every file the manifest lists, but the log, is there and hashes to its recorded value; otherwise the sentence for exit 2.</summary>
    private static string? VerifyManifest(string folder)
    {
        var path = Path.Combine(folder, ExportPaths.ManifestName);
        try
        {
            if (!File.Exists(path) || ExportPaths.IsLink(path) || new FileInfo(path).Length > MaxManifestBytes)
            {
                return "the pack has no usable manifest.json";
            }

            var parsed = ExportManifest.ParseBytes(File.ReadAllBytes(path));
            if (parsed.IsFailure || !string.Equals(parsed.Value.Format, PackFiles.Format, StringComparison.Ordinal))
            {
                return "manifest.json is not the manifest of a verification pack (" + PackFiles.Format + ")";
            }

            foreach (var entry in parsed.Value.Entries.Where(e => !string.Equals(e.Name, PackFiles.CheckLogName, StringComparison.Ordinal)))
            {
                var file = ExportPaths.Combine(folder, entry.Name);
                if (!File.Exists(file) || ExportPaths.IsLink(file) || new FileInfo(file).Length != entry.Bytes
                    || !string.Equals(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file))), entry.Sha256, StringComparison.Ordinal))
                {
                    return entry.Name + " differs from manifest.json: the pack was changed after it was written (only " + PackFiles.CheckLogName + " may be edited)";
                }
            }
        }
        catch (Exception unreadable) when (unreadable is IOException or UnauthorizedAccessException)
        {
            return "a file of the pack cannot be read: " + unreadable.GetType().Name;
        }

        return null;
    }

    private static async Task<string> ReadTextAsync(string file, CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(file, cancellationToken);
        return new UTF8Encoding(false, true).GetString(bytes);
    }
}
