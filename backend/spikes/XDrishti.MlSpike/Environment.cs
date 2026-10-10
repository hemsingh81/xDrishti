using System.Reflection;
using System.Runtime.InteropServices;

namespace XDrishti.MlSpike;

/// <summary>Facts about where the spike runs (architecture, limits, native libraries) so every result carries its environment.</summary>
public sealed record EnvironmentInfo(
    string Name,
    string Os,
    string ProcessArchitecture,
    string RuntimeIdentifier,
    string Framework,
    int ProcessorCount,
    string? CpuLimit,
    string? MemoryLimit,
    string User,
    bool RootFileSystemWritable,
    IReadOnlyDictionary<string, string> Packages,
    IReadOnlyList<string> NativeLibraries)
{
    public static EnvironmentInfo Capture(string name)
    {
        var packages = new[] { typeof(Microsoft.ML.MLContext), typeof(Microsoft.ML.Trainers.LightGbm.LightGbmBinaryTrainer), typeof(Microsoft.ML.Trainers.FastTree.FastTreeBinaryTrainer) }
            .ToDictionary(t => t.Assembly.GetName().Name!, t => t.Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?");
        return new EnvironmentInfo(
            name,
            RuntimeInformation.OSDescription,
            RuntimeInformation.ProcessArchitecture.ToString(),
            RuntimeInformation.RuntimeIdentifier,
            RuntimeInformation.FrameworkDescription,
            Environment.ProcessorCount,
            Read("/sys/fs/cgroup/cpu.max"),
            Read("/sys/fs/cgroup/memory.max"),
            Environment.UserName,
            CanWrite("/"),
            packages,
            FindNative());
    }

    private static string? Read(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static bool CanWrite(string directory)
    {
        try
        {
            var probe = Path.Combine(directory, ".xd-write-probe-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(probe, "x");
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static List<string> FindNative()
    {
        var found = new List<string>();
        foreach (var root in new[] { AppContext.BaseDirectory, "/usr/lib", "/lib", "/usr/local/lib" })
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            try
            {
                found.AddRange(Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                    .Where(f => Path.GetFileName(f) is { } n && (n.StartsWith("lib_lightgbm", StringComparison.Ordinal) || n.StartsWith("libgomp", StringComparison.Ordinal) || n.StartsWith("libLdaNative", StringComparison.Ordinal)))
                    .Take(20));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // unreadable folder: skip
            }
        }

        return found;
    }
}
