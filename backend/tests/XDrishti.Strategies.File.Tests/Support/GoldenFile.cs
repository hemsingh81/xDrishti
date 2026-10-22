using System.Runtime.CompilerServices;

namespace XDrishti.Strategies.File.Tests.Support;

/// <summary>Compares a text with a checked-in golden file; set <c>XD_UPDATE_GOLDEN=1</c> to rewrite the file in the source tree (a schema change then shows as a diff to review).</summary>
internal static class GoldenFile
{
    public static void Check(string name, string actual, [CallerFilePath] string here = "")
    {
        var source = Path.Combine(Path.GetDirectoryName(here)!, "golden", name);
        if (Environment.GetEnvironmentVariable("XD_UPDATE_GOLDEN") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(source)!);
            System.IO.File.WriteAllText(source, actual + "\n");
            return;
        }

        System.IO.File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "golden", name)).TrimEnd('\n').ShouldBe(actual);
    }
}
