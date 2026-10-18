namespace XDrishti.Simulation.Tests.Support;

/// <summary>
/// The golden files of this project: <c>Golden/&lt;name&gt;.golden.txt</c>, a first line <c>engine &lt;SimulationEngine.Version&gt;</c> and then the rendering. A thin wrapper over the shared helper
/// (<see cref="XDrishti.Tests.Golden.GoldenFile"/>, spec P3-04b1 E9), which keeps the convention; this one supplies the header and the project file. A changed rule needs a new <see cref="SimulationEngine.Version"/> and a regenerated
/// file, on purpose: <c>XD_UPDATE_GOLDEN=1 dotnet test --project tests/XDrishti.Simulation.Tests --filter-method "*golden*"</c> rewrites every golden file a test checks. A new golden is one call of <see cref="Check"/> and one
/// <c>.golden.txt</c> in the folder (the csproj copies the folder).
/// </summary>
internal static class GoldenFile
{
    public const string UpdateSwitch = XDrishti.Tests.Golden.GoldenFile.UpdateSwitch;

    public static string Header => "engine " + SimulationEngine.Version;

    /// <summary>Compares the rendering with the checked-in file (or rewrites the file when the switch is set).</summary>
    public static void Check(string name, string rendered) =>
        XDrishti.Tests.Golden.GoldenFile.Check(name, rendered, Header, "XDrishti.Simulation.Tests.csproj");
}
