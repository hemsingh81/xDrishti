using System.Diagnostics;

namespace XDrishti.Api.IntegrationTests.Infrastructure;

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
            Environment.SetEnvironmentVariable("TESTCONTAINERS_RYUK_DISABLED", "true");
        }
    }

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
