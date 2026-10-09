using System.Text.Json;
using XDrishti.DhanTrial;

// Read-only Dhan Data API trial (P0-13).
// Usage: xd-dhan-trial <run|login|renew|analyze> [--offline] [--try-totp] [--login] [--burst] [--out DIR] [--symbols FILE]
return await Cli.RunAsync(args);

internal static class Cli
{
    private static readonly string[] ValueOptions = ["--out", "--symbols"];

    public static async Task<int> RunAsync(string[] args)
    {
        var command = Command(args) ?? "run";
        var offline = command == "analyze" || args.Contains("--offline");
        var root = RepoRoot();
        var outDir = Option(args, "--out") ?? Path.Combine(root, "data", "trial");
        var symbols = Option(args, "--symbols") ?? Path.Combine(AppContext.BaseDirectory, "symbols.json");
        var secretsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "xDrishti", "secrets", "dhan-trial");
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            // First Ctrl-C asks politely; a second one (e.g. at a hidden prompt that cannot observe the token) ends the process.
            e.Cancel = !cts.IsCancellationRequested;
            cts.Cancel();
        };

        var redactor = new Redactor();
        var clock = TimeProvider.System;
        using var handler = GuardedHttp.CreateHandler();
        using var http = GuardedHttp.CreateClient(handler);
        using var limiter = new MinIntervalRateLimiter(TimeSpan.FromMilliseconds(220), clock);
        var api = new DhanApi(http, limiter, redactor, clock);
        var burstApi = new DhanApi(http, new NoRateLimiter(), redactor, clock, maxAttempts: 1);
        var tokens = new FileTokenStore(Path.Combine(secretsDir, "access-token.json"));
        var prompt = new ConsolePrompt();
        var flow = new TokenFlow(api, tokens, prompt, redactor, clock);
        var totpFlow = new TokenFlow(api, new FileTokenStore(Path.Combine(secretsDir, "access-token-totp.json")), prompt, redactor, clock);
        var today = DateOnly.FromDateTime(IstTime.ToIst(clock.GetUtcNow()).DateTime);
        var options = new TrialOptions(offline, args.Contains("--try-totp"), args.Contains("--login"), args.Contains("--burst"), outDir, symbols, today);
        var runner = new TrialRunner(api, burstApi, flow, totpFlow, tokens, new KeychainSecrets(), new FileRawStore(Path.Combine(outDir, "raw")), options, clock, redactor, Console.WriteLine);

        try
        {
            switch (command)
            {
                case "login":
                    await runner.LoginOnlyAsync(cts.Token);
                    return 0;
                case "renew":
                    Console.WriteLine(await runner.RenewOnlyAsync(cts.Token));
                    return 0;
                case "run" or "analyze":
                    var report = await runner.RunAsync(cts.Token);
                    var path = Path.Combine(outDir, "report.md");
                    await File.WriteAllTextAsync(path, report, cts.Token);
                    Console.WriteLine($"\nReport written to {path}");
                    return 0;
                default:
                    Console.Error.WriteLine("Unknown command. Use: run | login | renew | analyze");
                    return 2;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or FormatException or IOException or OperationCanceledException
            or JsonException or KeyNotFoundException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(redactor.Redact($"Trial stopped: {ex.Message}"));
            return 1;
        }
    }

    /// <summary>The first argument that is neither an option nor an option's value.</summary>
    private static string? Command(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (ValueOptions.Contains(args[i]))
            {
                i++;
            }
            else if (!args[i].StartsWith('-'))
            {
                return args[i];
            }
        }

        return null;
    }

    private static string? Option(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "backend", "XDrishti.slnx")))
            {
                return dir.FullName;
            }
        }

        return Directory.GetCurrentDirectory();
    }
}
