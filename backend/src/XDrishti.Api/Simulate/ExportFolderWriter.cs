using System.Security.Cryptography;
using XDrishti.Domain.Common;
using XDrishti.SimExport.Manifest;

namespace XDrishti.Api.Simulate;

/// <summary>
/// The only code that touches the file system for the export (spec P3-04b1, E7): puts the files the library renders into <c>&lt;root&gt;/sim/&lt;run-id&gt;/</c> atomically, with owner-only permissions at creation, behind the path guard
/// of <see cref="ExportPaths"/> and with a safe overwrite policy. Everything is written into a staging folder <c>.staging-&lt;run-id&gt;-&lt;random&gt;</c> (<c>manifest.json</c> last, flushed to disk) and renamed to <c>&lt;run-id&gt;</c> in
/// one <see cref="Directory.Move(string, string)"/>. The only folder it ever deletes is its own staging folder; leftovers of a killed process are reported and never removed. It decides nothing beyond the overwrite policy: the same
/// export again is <see cref="ExportStatus.Unchanged"/> after hashing every file on disk; a different or modified one is refused unless forced, when the old folder is kept once as <c>&lt;run-id&gt;.previous</c>; a folder with no valid
/// manifest is never replaced. Expected refusals return a <see cref="Result"/>; an I/O failure is raised after the staging folder is removed.
/// </summary>
internal sealed class ExportFolderWriter
{
    private const UnixFileMode DirectoryPermissions = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private const UnixFileMode FilePermissions = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    private const long MaxManifestBytes = 4L * 1024 * 1024;

    private readonly Action<string, string> _moveDirectory;

    /// <param name="moveDirectory">The rename of a folder; <see cref="Directory.Move(string, string)"/> unless a test makes one fail.</param>
    public ExportFolderWriter(Action<string, string>? moveDirectory = null) => _moveDirectory = moveDirectory ?? Directory.Move;

    /// <summary>Writes the export.</summary>
    /// <param name="root">The export root (<c>data/exports</c>): absolute, with an existing parent; created when missing.</param>
    /// <param name="runId">The folder name, from <see cref="RunId.Of"/>.</param>
    /// <param name="files">The files to render; their names are checked, <c>manifest.json</c> is the writer's own.</param>
    /// <param name="buildManifest">Gets the entries the writer produced (name, length, SHA-256 of each file) and returns the bytes of <c>manifest.json</c> and the export key they state.</param>
    /// <param name="force">Replace a different or modified export, keeping the old one once as <c>&lt;run-id&gt;.previous</c>.</param>
    /// <param name="cancellationToken">Checked between files; a cancelled write removes its staging folder.</param>
    /// <param name="folderName">The folder below the root that holds the run folders: <c>sim</c> for a run (the default), <c>verify</c> for a verification pack (spec P3-09, V5); 1 to 20 letters.</param>
    /// <returns>The outcome, or a refusal (<see cref="ExportErrors"/>).</returns>
    /// <exception cref="IOException">The file system failed; the staging folder is removed first.</exception>
    public Task<Result<ExportOutcome>> WriteAsync(
        string root,
        string runId,
        IReadOnlyList<ExportFile> files,
        Func<IReadOnlyList<ManifestEntry>, (byte[] Manifest, string ExportKey)> buildManifest,
        bool force,
        CancellationToken cancellationToken,
        string folderName = ExportPaths.SimFolder)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(buildManifest);

        return Task.Run(() => Write(root, runId, files, buildManifest, force, folderName, cancellationToken), cancellationToken);
    }

    private static bool Exists(string path) => Directory.Exists(path) || File.Exists(path) || ExportPaths.IsLink(path);

    /// <summary>Creates a folder owner-only at creation (no umask window); an existing folder is left as it is.</summary>
    private static void CreateDirectory(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            throw NotOnWindows();
        }

        Directory.CreateDirectory(path, DirectoryPermissions);
    }

    /// <summary>Opens a new file owner-only at creation; it must not exist.</summary>
    private static FileStream OpenNew(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            throw NotOnWindows();
        }

        return new FileStream(path, new FileStreamOptions { Mode = System.IO.FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None, UnixCreateMode = FilePermissions, BufferSize = 0 });
    }

    private static PlatformNotSupportedException NotOnWindows() => new("The export is written with Unix permissions at creation; it runs on macOS and Linux.");

    private static Error? Prepare(string runId, IReadOnlyList<ExportFile> files) => ExportPaths.RunIdProblem(runId) ?? ExportPaths.FileNamesProblem(files);

    private Result<ExportOutcome> Write(
        string root,
        string runId,
        IReadOnlyList<ExportFile> files,
        Func<IReadOnlyList<ManifestEntry>, (byte[] Manifest, string ExportKey)> buildManifest,
        bool force,
        string folderName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var rooted = ExportPaths.Root(root);
        if (rooted.IsFailure)
        {
            return Result.Failure<ExportOutcome>(rooted.Error);
        }

        if ((ExportPaths.FolderNameProblem(folderName) ?? Prepare(runId, files)) is { } problem)
        {
            return Result.Failure<ExportOutcome>(problem);
        }

        var simFolder = Path.Combine(rooted.Value, folderName);
        if (!Directory.Exists(rooted.Value))
        {
            CreateDirectory(rooted.Value);
        }

        if (ExportPaths.IsLink(simFolder))
        {
            return Result.Failure<ExportOutcome>(ExportErrors.LinkRefused(simFolder));
        }

        if (File.Exists(simFolder))
        {
            return Result.Failure<ExportOutcome>(ExportErrors.PathInvalid("'" + simFolder + "' is a file"));
        }

        if (!Directory.Exists(simFolder))
        {
            CreateDirectory(simFolder);
        }

        var runFolder = ExportPaths.Combine(simFolder, runId);
        var previousFolder = ExportPaths.Combine(simFolder, runId + ExportPaths.PreviousSuffix);
        if (ExportPaths.IsLink(runFolder))
        {
            return Result.Failure<ExportOutcome>(ExportErrors.LinkRefused(runFolder));
        }

        var stale = StaleStaging(simFolder);
        var staging = NewStagingFolder(simFolder, runId);
        try
        {
            var entries = Render(staging, files, cancellationToken);
            var (manifest, exportKey) = buildManifest(entries);
            WriteFile(Path.Combine(staging, ExportPaths.ManifestName), manifest);
            cancellationToken.ThrowIfCancellationRequested();

            return Publish(staging, runFolder, previousFolder, entries, exportKey, force, stale);
        }
        finally
        {
            RemoveOwnStaging(simFolder, staging);
        }
    }

    private static List<string> StaleStaging(string simFolder) =>
        [.. Directory.EnumerateDirectories(simFolder, ExportPaths.StagingPrefix + "*", SearchOption.TopDirectoryOnly).Select(d => Path.GetFileName(d)).Order(StringComparer.Ordinal)];

    private static string NewStagingFolder(string simFolder, string runId)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var suffix = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4));
            var path = ExportPaths.Combine(simFolder, ExportPaths.StagingPrefix + runId + "-" + suffix);
            if (!Exists(path))
            {
                CreateDirectory(path);
                return path;
            }
        }

        throw new IOException("No free name for a staging folder under " + simFolder + ".");
    }

    private static List<ManifestEntry> Render(string staging, IReadOnlyList<ExportFile> files, CancellationToken cancellationToken)
    {
        var entries = new List<ManifestEntry>(files.Count);
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = ExportPaths.Combine(staging, file.Name);
            var folder = Path.GetDirectoryName(path)!;
            if (!string.Equals(folder, staging, StringComparison.Ordinal) && !Directory.Exists(folder))
            {
                CreateDirectory(folder);
            }

            using var stream = OpenNew(path);
            using var sink = new HashingFileSink(stream);
            file.Render(sink);
            var (bytes, sha256) = sink.Complete();
            stream.Flush(flushToDisk: true);
            entries.Add(new ManifestEntry(file.Name, bytes, sha256));
        }

        return entries;
    }

    private static void WriteFile(string path, byte[] content)
    {
        using var stream = OpenNew(path);
        stream.Write(content);
        stream.Flush(flushToDisk: true);
    }

    private Result<ExportOutcome> Publish(string staging, string runFolder, string previousFolder, List<ManifestEntry> entries, string exportKey, bool force, List<string> stale)
    {
        if (!Exists(runFolder))
        {
            try
            {
                _moveDirectory(staging, runFolder);
                return Result.Success(new ExportOutcome(ExportStatus.Written, runFolder, exportKey, entries, stale, null));
            }
            catch (IOException) when (Exists(runFolder))
            {
                // two writers racing for one name: the one that loses the rename finds the folder there and compares, like the second run it is
            }
        }

        return Existing(staging, runFolder, previousFolder, entries, exportKey, force, stale);
    }

    private Result<ExportOutcome> Existing(string staging, string runFolder, string previousFolder, List<ManifestEntry> entries, string exportKey, bool force, List<string> stale)
    {
        if (ExportPaths.IsLink(runFolder))
        {
            return Result.Failure<ExportOutcome>(ExportErrors.LinkRefused(runFolder));
        }

        if (!Directory.Exists(runFolder))
        {
            return Result.Failure<ExportOutcome>(ExportErrors.NoValidManifest(runFolder, "it is a file"));
        }

        var parsed = ReadManifest(runFolder);
        if (parsed.IsFailure)
        {
            return Result.Failure<ExportOutcome>(ExportErrors.NoValidManifest(runFolder, parsed.Error.Message));
        }

        if (IsUnchanged(runFolder, parsed.Value, exportKey))
        {
            return Result.Success(new ExportOutcome(ExportStatus.Unchanged, runFolder, exportKey, entries, stale, null));
        }

        if (!force)
        {
            return Result.Failure<ExportOutcome>(ExportErrors.ExistsDifferent(runFolder));
        }

        if (Exists(previousFolder))
        {
            return Result.Failure<ExportOutcome>(ExportErrors.PreviousExists(previousFolder));
        }

        _moveDirectory(runFolder, previousFolder);
        try
        {
            _moveDirectory(staging, runFolder);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            RestorePrevious(runFolder, previousFolder, failure);
            throw;
        }

        return Result.Success(new ExportOutcome(ExportStatus.ReplacedPrevious, runFolder, exportKey, entries, stale, previousFolder));
    }

    private void RestorePrevious(string runFolder, string previousFolder, Exception failure)
    {
        try
        {
            _moveDirectory(previousFolder, runFolder);
        }
        catch (Exception back) when (back is IOException or UnauthorizedAccessException)
        {
            throw new IOException("Replacing " + runFolder + " failed and the old export could not be put back: it is in " + previousFolder + ", and " + runFolder + " does not exist. Rename " + previousFolder + " to " + runFolder + " by hand.", failure);
        }
    }

    private static Result<ParsedManifest> ReadManifest(string runFolder)
    {
        var path = Path.Combine(runFolder, ExportPaths.ManifestName);
        try
        {
            if (!File.Exists(path) || ExportPaths.IsLink(path))
            {
                return Result.Failure<ParsedManifest>(Error.Validation(ExportManifest.MalformedCode, "there is no manifest.json"));
            }

            if (new FileInfo(path).Length > MaxManifestBytes)
            {
                return Result.Failure<ParsedManifest>(Error.Validation(ExportManifest.MalformedCode, "manifest.json is too large"));
            }

            return ExportManifest.ParseBytes(File.ReadAllBytes(path));
        }
        catch (Exception unreadable) when (unreadable is IOException or UnauthorizedAccessException)
        {
            return Result.Failure<ParsedManifest>(Error.Validation(ExportManifest.MalformedCode, "manifest.json cannot be read"));
        }
    }

    /// <summary>The folder is the identical export: the manifest's export key is the new one, the folder holds exactly <c>manifest.json</c> and the listed files (no extra, no missing, no link) and every listed file hashes to its recorded value.</summary>
    private static bool IsUnchanged(string runFolder, ParsedManifest manifest, string exportKey)
    {
        if (!string.Equals(manifest.ExportKey, exportKey, StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            var expected = new HashSet<string>(manifest.Entries.Select(e => e.Name), StringComparer.Ordinal) { ExportPaths.ManifestName };
            var expectedFolders = new HashSet<string>(manifest.Entries.Where(e => e.Name.Contains('/', StringComparison.Ordinal)).Select(e => e.Name[..e.Name.IndexOf('/', StringComparison.Ordinal)]), StringComparer.Ordinal);
            var found = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in new DirectoryInfo(runFolder).EnumerateFileSystemInfos("*", SearchOption.AllDirectories))
            {
                if (entry.LinkTarget is not null)
                {
                    return false;
                }

                var relative = Path.GetRelativePath(runFolder, entry.FullName).Replace(Path.DirectorySeparatorChar, '/');
                if (entry is DirectoryInfo)
                {
                    if (!expectedFolders.Contains(relative))
                    {
                        return false;
                    }
                }
                else
                {
                    found.Add(relative);
                }
            }

            if (!found.SetEquals(expected))
            {
                return false;
            }

            return manifest.Entries.All(e => HashesTo(Path.Combine(runFolder, e.Name), e));
        }
        catch (Exception unreadable) when (unreadable is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool HashesTo(string path, ManifestEntry entry)
    {
        using var stream = new FileStream(path, System.IO.FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1, FileOptions.SequentialScan);
        return stream.Length == entry.Bytes && string.Equals(Convert.ToHexStringLower(SHA256.HashData(stream)), entry.Sha256, StringComparison.Ordinal);
    }

    /// <summary>Removes the staging folder this writer made, if it is still there (after a successful rename or a refusal it is the only copy of the new export, and it is deleted with its own files; it is never anything the owner made).</summary>
    private static void RemoveOwnStaging(string simFolder, string staging)
    {
        if (Directory.Exists(staging)
            && Path.GetDirectoryName(staging) == simFolder
            && Path.GetFileName(staging).StartsWith(ExportPaths.StagingPrefix, StringComparison.Ordinal)
            && !ExportPaths.IsLink(staging))
        {
            try
            {
                Directory.Delete(staging, recursive: true);
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                // the cleanup must not hide the failure that brought us here; the folder is reported as stale by the next run
            }
        }
    }
}
