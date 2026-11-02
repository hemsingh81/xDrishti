namespace XDrishti.Infrastructure.Ai.Assistant;

/// <summary>Where the eval's files are (P9-09, P15-07): the repo's <c>eval/</c> folder and the git-ignored <c>data/eval</c> folder that holds the announcement sample and the labelled set. Paths only; nothing is read or written here.</summary>
internal static class EvalPaths
{
    public const string BaselineName = "announcement-baseline.json";

    public const string ManifestName = "announcements.manifest.json";

    public const string LabelledName = "announcements.jsonl";

    /// <summary>The configured data folder, else <c>data/eval</c> beside the located <c>eval/</c> folder.</summary>
    public static string DataFolder(string evalFolder, string configured) =>
        !string.IsNullOrWhiteSpace(configured) ? configured : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(evalFolder)) ?? ".", "data", "eval");

    internal static string? Locate(string configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return File.Exists(Path.Combine(configured, "thresholds.json")) ? configured : null;
        }

        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "eval");
                if (File.Exists(Path.Combine(candidate, "thresholds.json")))
                {
                    return candidate;
                }
            }
        }

        return null;
    }
}
