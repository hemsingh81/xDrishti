using System.Reflection;
using XDrishti.Application.Abstractions.Platform;

namespace XDrishti.Hosting;

internal sealed class ApplicationInfo(string name, string environment) : IApplicationInfo
{
    public string Name { get; } = name;

    public string Version { get; } =
        Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0";

    public string Environment { get; } = environment;

    public string Instance { get; } = System.Environment.MachineName;

    public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;
}
