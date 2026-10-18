namespace XDrishti.Tests.Golden;

/// <summary>
/// The one mechanism of golden files for the test projects (spec P3-04b1, E9): <c>Golden/&lt;name&gt;.golden.txt</c> next to the tests, a first line holding a header the caller gives (a version of the thing the file pins), then the
/// rendering. A changed rule needs a new version and a regenerated file, on purpose: <c>XD_UPDATE_GOLDEN=1</c> rewrites every golden file a test checks. The project copies the <c>Golden</c> folder to its output and says which project
/// file marks its source folder, so that the rewrite lands in the checkout and not in <c>bin</c>. A project that wants the header fixed supplies a thin wrapper (the simulation tests do: <c>engine &lt;version&gt;</c>).
/// </summary>
internal static class GoldenFile
{
    public const string UpdateSwitch = "XD_UPDATE_GOLDEN";

    /// <summary>Compares the rendering with the checked-in file (or rewrites the file when the switch is set).</summary>
    /// <param name="name">The file is <c>Golden/&lt;name&gt;.golden.txt</c>.</param>
    /// <param name="rendered">The text to pin (the file holds the header, a newline and this text).</param>
    /// <param name="header">The first line of the file: what the file was made with.</param>
    /// <param name="projectFile">The name of the test project's <c>.csproj</c> file, which marks the folder where the file is rewritten.</param>
    public static void Check(string name, string rendered, string header, string projectFile)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(rendered);
        ArgumentException.ThrowIfNullOrEmpty(header);
        ArgumentException.ThrowIfNullOrEmpty(projectFile);

        var expected = header + "\n" + rendered;
        if (Environment.GetEnvironmentVariable(UpdateSwitch) == "1")
        {
            var path = SourcePath(name, projectFile);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, expected);
            return;
        }

        var golden = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Golden", FileName(name)));

        golden.Split('\n')[0].ShouldBe(header, "the golden file was made with another header '" + header + "': raise the version with the rule and regenerate the file (" + UpdateSwitch + "=1)");
        expected.ShouldBe(golden, name + ": a changed rule needs a new version in the header and a regenerated golden file (" + UpdateSwitch + "=1)");
    }

    private static string FileName(string name) => name + ".golden.txt";

    /// <summary>The checked-in copy, found by walking up from the test binaries (the tests run from a checkout).</summary>
    private static string SourcePath(string name, string projectFile)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, projectFile)))
            {
                return Path.Combine(directory.FullName, "Golden", FileName(name));
            }
        }

        throw new FileNotFoundException("The test project file " + projectFile + " was not found above " + AppContext.BaseDirectory);
    }
}
