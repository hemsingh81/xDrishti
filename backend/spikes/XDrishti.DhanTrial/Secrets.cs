using System.Diagnostics;

namespace XDrishti.DhanTrial;

internal interface ISecretSource
{
    string? Get(string name);
}

/// <summary>Reads secrets from the macOS Keychain (account "xdrishti", service "xdrishti/dhan/main/&lt;name&gt;"). Values never touch argv, env or logs.</summary>
internal sealed class KeychainSecrets : ISecretSource
{
    public const string Account = "xdrishti";
    public const string ServicePrefix = "xdrishti/dhan/main/";

    public string? Get(string name)
    {
        var info = new ProcessStartInfo("security") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in new[] { "find-generic-password", "-a", Account, "-s", ServicePrefix + name, "-w" })
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start 'security'.");
        var output = process.StandardOutput.ReadToEnd().TrimEnd('\n', '\r');
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode == 0 && output.Length > 0 ? output : null;
    }
}

internal interface IPrompt
{
    void Info(string text);

    string ReadHidden(string label);
}

internal sealed class ConsolePrompt : IPrompt
{
    public void Info(string text) => Console.WriteLine(text);

    public string ReadHidden(string label)
    {
        Console.Write(label);
        var buffer = new System.Text.StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return buffer.ToString().Trim();
            }

            if (key.Key == ConsoleKey.Backspace && buffer.Length > 0)
            {
                buffer.Length--;
            }
            else if (!char.IsControl(key.KeyChar))
            {
                buffer.Append(key.KeyChar);
            }
        }
    }
}
