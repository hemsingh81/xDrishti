namespace XDrishti.Simulation.Tests.Support;

/// <summary>
/// The one mechanism of the golden files of this project: <c>Golden/&lt;name&gt;.golden.txt</c>, a first line <c>engine &lt;SimulationEngine.Version&gt;</c> and then the rendering. A changed rule needs a new
/// <see cref="SimulationEngine.Version"/> and a regenerated file, on purpose: <c>XD_UPDATE_GOLDEN=1 dotnet test --project tests/XDrishti.Simulation.Tests --filter-method "*golden*"</c> rewrites every golden file a
/// test checks. A new golden (part (b)) is one call of <see cref="Check"/> and one <c>.golden.txt</c> in the folder (the csproj copies the folder).
/// </summary>
internal static class GoldenFile
{
    public const string UpdateSwitch = "XD_UPDATE_GOLDEN";

    public static string Header => "engine " + SimulationEngine.Version;

    /// <summary>Compares the rendering with the checked-in file (or rewrites the file when the switch is set).</summary>
    public static void Check(string name, string rendered)
    {
        var expected = Header + "\n" + rendered;
        if (Environment.GetEnvironmentVariable(UpdateSwitch) == "1")
        {
            File.WriteAllText(SourcePath(name), expected);
            return;
        }

        var golden = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Golden", FileName(name)));

        golden.Split('\n')[0].ShouldBe(Header, "the golden file was made with another engine version: raise the version with the rule and regenerate the file (" + UpdateSwitch + "=1)");
        expected.ShouldBe(golden, name + ": a changed rule needs a new SimulationEngine.Version and a regenerated golden file (" + UpdateSwitch + "=1)");
    }

    private static string FileName(string name) => name + ".golden.txt";

    /// <summary>The checked-in copy, found by walking up from the test binaries (the tests run from a checkout).</summary>
    private static string SourcePath(string name)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "XDrishti.Simulation.Tests.csproj")))
            {
                return Path.Combine(directory.FullName, "Golden", FileName(name));
            }
        }

        throw new FileNotFoundException("The test project directory was not found above " + AppContext.BaseDirectory);
    }
}
