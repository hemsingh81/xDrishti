using System.Text.RegularExpressions;
using XDrishti.Domain.Common;
using XDrishti.SimExport.Manifest;

namespace XDrishti.Api.Simulate;

/// <summary>
/// The path guard of the folder writer (spec P3-04b1, E7): every path of an export is built from fixed names plus the sanitised run id and entry names, resolved with <see cref="Path.GetFullPath(string)"/> and checked to lie under
/// <c>&lt;root&gt;/sim/</c>. A name cannot contain <c>..</c>, a backslash, a colon, a control or format character or a Windows device name; the root must be absolute and its parent must exist.
/// </summary>
internal static partial class ExportPaths
{
    public const string ManifestName = "manifest.json";

    public const string SimFolder = "sim";

    public const string StagingPrefix = ".staging-";

    public const string PreviousSuffix = ".previous";

    private static readonly HashSet<string> DeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,79}$", RegexOptions.CultureInvariant)]
    private static partial Regex RunIdPattern();

    /// <summary>The root as a full path without a trailing separator, or <see cref="ExportErrors.RootInvalid"/> (a configuration error): it must be absolute, not the root of the file system, not a file, and its parent must exist.</summary>
    public static Result<string> Root(string? root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            return Result.Failure<string>(ExportErrors.RootInvalid("it is not set"));
        }

        if (root.Any(char.IsControl))
        {
            return Result.Failure<string>(ExportErrors.RootInvalid("it has a control character"));
        }

        if (!Path.IsPathFullyQualified(root))
        {
            return Result.Failure<string>(ExportErrors.RootInvalid("it is not absolute"));
        }

        var full = Path.GetFullPath(root);
        if (full.Length > 1)
        {
            full = full.TrimEnd(Path.DirectorySeparatorChar);
        }

        var parent = Path.GetDirectoryName(full);
        if (parent is null)
        {
            return Result.Failure<string>(ExportErrors.RootInvalid("it is the root of the file system"));
        }

        if (!Directory.Exists(parent))
        {
            return Result.Failure<string>(ExportErrors.RootInvalid("its parent folder does not exist"));
        }

        return File.Exists(full)
            ? Result.Failure<string>(ExportErrors.RootInvalid("it is a file"))
            : Result.Success(full);
    }

    /// <summary>A refusal when the folder below the root is not 1 to 20 lower-case letters (the writer's own names, <c>sim</c> and <c>verify</c>, never a caller's free text); otherwise null.</summary>
    public static Error? FolderNameProblem(string? folderName) =>
        folderName is { Length: >= 1 and <= 20 } && folderName.All(c => c is >= 'a' and <= 'z') ? null : ExportErrors.PathInvalid("the export folder name must be 1 to 20 lower-case letters");

    /// <summary>A refusal when the run id is not 1 to 80 characters of <c>a-z 0-9 -</c> starting with a letter or digit (what <see cref="RunId"/> gives); otherwise null.</summary>
    public static Error? RunIdProblem(string? runId) =>
        runId is not null && RunIdPattern().IsMatch(runId) ? null : ExportErrors.PathInvalid("the run id must be 1 to 80 characters of a-z, 0-9 and -");

    /// <summary>A refusal when a file name is not allowed: not valid for <see cref="ExportNames"/>, a device name, <c>manifest.json</c> (the writer's own), two names equal ignoring case, or a name that is also the folder of another; otherwise null.</summary>
    public static Error? FileNamesProblem(IReadOnlyList<ExportFile> files)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            var name = file.Name;
            if (!ExportNames.IsValid(name))
            {
                return ExportErrors.PathInvalid("the file name '" + Printable(name) + "' is not valid");
            }

            if (name.Split('/').Any(IsDeviceName))
            {
                return ExportErrors.PathInvalid("the file name '" + name + "' is a device name");
            }

            if (string.Equals(name, ManifestName, StringComparison.OrdinalIgnoreCase))
            {
                return ExportErrors.PathInvalid("the file name '" + name + "' is the manifest's own");
            }

            if (!seen.Add(name))
            {
                return ExportErrors.PathInvalid("the file name '" + name + "' is there twice (names are compared ignoring case)");
            }

            var slash = name.IndexOf('/', StringComparison.Ordinal);
            if (slash >= 0)
            {
                folders.Add(name[..slash]);
            }
        }

        var clash = seen.FirstOrDefault(folders.Contains);
        return clash is null ? null : ExportErrors.PathInvalid("'" + clash + "' is both a file and a folder");
    }

    /// <summary>The full path of <paramref name="relative"/> under <paramref name="folder"/>, which must lie inside it after <see cref="Path.GetFullPath(string)"/>.</summary>
    /// <exception cref="InvalidOperationException">The path would leave the folder (the names are validated first, so this is the guard that is never meant to fire).</exception>
    public static string Combine(string folder, string relative)
    {
        var full = Path.GetFullPath(Path.Combine(folder, relative));
        return full.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            ? full
            : throw new InvalidOperationException("The path '" + relative + "' leaves the export folder.");
    }

    /// <summary>Whether the path is a symbolic link (a file or a folder, working or broken).</summary>
    public static bool IsLink(string path) => new FileInfo(path).LinkTarget is not null;

    private static bool IsDeviceName(string segment)
    {
        var dot = segment.IndexOf('.', StringComparison.Ordinal);
        return DeviceNames.Contains(dot < 0 ? segment : segment[..dot]);
    }

    private static string Printable(string? name) =>
        name is null ? "(null)" : new string([.. name.Take(40).Select(c => char.IsControl(c) || char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Format ? '?' : c)]);
}
