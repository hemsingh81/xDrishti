namespace XDrishti.Application.Abstractions.Platform;

/// <summary>Facts about the running process (provided by the host).</summary>
public interface IApplicationInfo
{
    string Name { get; }

    string Version { get; }

    string Environment { get; }

    string Instance { get; }

    DateTimeOffset StartedAt { get; }
}
