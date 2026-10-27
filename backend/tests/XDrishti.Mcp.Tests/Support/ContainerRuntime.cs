using System.Diagnostics;
using Testcontainers.PostgreSql;

namespace XDrishti.Mcp.Tests.Support;

/// <summary>
/// Points Testcontainers at Podman when DOCKER_HOST is not set (macOS + Podman Desktop),
/// so integration tests run from the IDE and the terminal without extra setup.
/// </summary>
internal static class ContainerRuntime
{
    public static void UsePodmanIfNeeded()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DOCKER_HOST")))
        {
            return;
        }

        var socket = TryPodmanSocket();
        if (socket is not null)
        {
            Environment.SetEnvironmentVariable("DOCKER_HOST", "unix://" + socket);
            // Ryuk cannot start under rootless Podman Desktop (its container vanishes at once: 404 on start), so leaks are cleaned by scripts/clean-test-containers.sh.
            Environment.SetEnvironmentVariable("TESTCONTAINERS_RYUK_DISABLED", "true");
        }
    }

    /// <summary>Labels the server (xdrishti.test, for scripts/clean-test-containers.sh) and caps it at 1.5 GiB (it idles near 200 MB), so a leaked container cannot eat the Podman VM.</summary>
    public static PostgreSqlBuilder WithMemoryLimit(this PostgreSqlBuilder builder) =>
        builder.WithLabel("xdrishti.test", "1").WithCreateParameterModifier(parameters => { parameters.HostConfig ??= new(); parameters.HostConfig.Memory = 1536L * 1024 * 1024; });

    private static string? TryPodmanSocket()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("podman", "machine inspect --format {{.ConnectionInfo.PodmanSocket.Path}}")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            if (process is null)
            {
                return null;
            }

            var path = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(5000);
            return process.ExitCode == 0 && File.Exists(path) ? path : null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null; // podman not installed — Testcontainers falls back to its defaults
        }
    }
}
