namespace XDrishti.Simulation.Tests.Oracle;

/// <summary>
/// Writes the printed lists of the checks (coverage, fault-to-rows, the id table) to a folder when the environment variable <c>XD_ORACLE_REPORTS</c> names one, so that a deliberate run can keep them; a default run writes nothing (a unit
/// test touches no disk) and the same lines go to the test output.
/// </summary>
internal static class Report
{
    internal static void Write(string name, IEnumerable<string> lines)
    {
        string? dir = Environment.GetEnvironmentVariable("XD_ORACLE_REPORTS");
        if (string.IsNullOrWhiteSpace(dir))
        {
            return;
        }

        Directory.CreateDirectory(dir);
        File.WriteAllLines(Path.Combine(dir, name), lines);
    }
}
