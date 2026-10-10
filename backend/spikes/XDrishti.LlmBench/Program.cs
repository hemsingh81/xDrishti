using System.Text.Json;
using XDrishti.LlmBench;

// Benchmark an OpenAI-compatible local LLM server (P0-15).
// Usage: xd-llm-bench --url http://127.0.0.1:8080 --label NAME [--model NAME] [--ctx 1024,8192,30000] [--repeats 3] [--gen 256]
//                     [--smoke] [--embeddings-url URL] [--load-ms N] [--no-think] [--out DIR]
return await Cli.RunAsync(args);

internal static class Cli
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static async Task<int> RunAsync(string[] args)
    {
        var url = Option(args, "--url") ?? "http://127.0.0.1:8080";
        var label = Option(args, "--label") ?? "unlabelled";
        var model = Option(args, "--model") ?? "local";
        var contexts = (Option(args, "--ctx") ?? "1024,8192,30000").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(int.Parse).ToArray();
        var repeats = int.Parse(Option(args, "--repeats") ?? "3", System.Globalization.CultureInfo.InvariantCulture);
        var gen = int.Parse(Option(args, "--gen") ?? "256", System.Globalization.CultureInfo.InvariantCulture);
        var outDir = Option(args, "--out") ?? Path.Combine(RepoRoot(), "data", "llm-bench");
        double? loadMs = Option(args, "--load-ms") is { } l ? double.Parse(l, System.Globalization.CultureInfo.InvariantCulture) : null;
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = !cts.IsCancellationRequested;
            cts.Cancel();
        };

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(20) };
        var client = new ChatClient(http, url, new ChatSettings(model, args.Contains("--no-think")), TimeProvider.System);
        var scenarios = new Scenarios(client, repeats, gen, Console.WriteLine);
        try
        {
            await scenarios.CalibrateAsync(cts.Token);
            var throughput = new List<ThroughputResult>();
            foreach (var ctx in contexts)
            {
                try
                {
                    throughput.Add(await scenarios.ThroughputAsync(ctx, cts.Token));
                }
                catch (Exception ex) when (ex is HttpRequestException or FormatException or JsonException)
                {
                    // e.g. the prompt does not fit the server's context window: report it and carry on with the other sizes.
                    Console.WriteLine($"Skipped ~{ctx} tokens: {ex.Message[..Math.Min(ex.Message.Length, 160)]}");
                }
            }

            var briefing = await scenarios.BriefingAsync(cts.Token);
            var smoke = args.Contains("--smoke") ? await scenarios.SmokeAsync(Option(args, "--embeddings-url"), cts.Token) : [];
            var results = new BenchResults(label, model, DateTimeOffset.Now, throughput, briefing, smoke, loadMs);
            Directory.CreateDirectory(outDir);
            var safe = string.Concat(label.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
            await File.WriteAllTextAsync(Path.Combine(outDir, safe + ".json"), JsonSerializer.Serialize(results, Indented), cts.Token);
            var report = ReportBuilder.Render(results, new Thresholds());
            await File.WriteAllTextAsync(Path.Combine(outDir, safe + ".md"), report, cts.Token);
            Console.WriteLine(report);
            return 0;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException or FormatException or JsonException or InvalidOperationException)
        {
            Console.Error.WriteLine($"Benchmark stopped: {ex.Message}");
            return 1;
        }
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
