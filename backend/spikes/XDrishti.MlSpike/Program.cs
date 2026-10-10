using System.Globalization;
using System.Text.Json;
using XDrishti.MlSpike;

// ML.NET gradient-boosted trees on linux/arm64 (P0-16).
// Usage: xd-mlspike probe
//        xd-mlspike run --env NAME --trainer lightgbm|fasttree|both [--rows 1000000] [--threads 8] [--trees 300] [--leaves 63] [--repeats 1] [--out DIR]
return Cli.Run(args);

internal static class Cli
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static int Run(string[] args)
    {
        var command = args.FirstOrDefault(a => !a.StartsWith('-')) ?? "probe";
        var environment = Option(args, "--env") ?? "unnamed";
        var outDir = Option(args, "--out") ?? Path.Combine(Directory.GetCurrentDirectory(), "mlnet-spike");
        try
        {
            return command switch
            {
                "probe" => Probe(environment, outDir),
                "run" => Execute(environment, outDir, args),
                "diag" => Diagnose(),
                _ => Fail("Unknown command. Use: probe | run"),
            };
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException or UnauthorizedAccessException)
        {
            return Fail(ex.ToString());
        }
    }

    private static int Execute(string environment, string outDir, string[] args)
    {
        var options = new SpikeOptions(
            environment,
            Option(args, "--trainer") ?? "both",
            Int(args, "--rows", 1_000_000),
            Int(args, "--threads", 8),
            Int(args, "--trees", 300),
            Int(args, "--leaves", 63),
            Int(args, "--repeats", 1),
            outDir);
        Directory.CreateDirectory(outDir);
        var report = new Spike(options, Console.WriteLine).Run();
        var stem = Path.Combine(outDir, $"result-{environment}-{options.Trainer}-t{options.Threads}-n{options.Rows}");
        File.WriteAllText(stem + ".json", JsonSerializer.Serialize(report, Indented));
        var markdown = ReportWriter.Render(report);
        File.WriteAllText(stem + ".md", markdown);
        Console.WriteLine(markdown);
        return 0;
    }

    /// <summary>Quick LightGBM option experiments (which settings let it use the numeric features) — a diagnostic, not part of the benchmark.</summary>
    private static int Diagnose()
    {
        foreach (var line in Diagnostics.LightGbmVariants(100_000))
        {
            Console.WriteLine(line);
        }

        return 0;
    }

    /// <summary>Tiny end-to-end run per trainer: shows whether native libraries load and every capability works, or the exact failure.</summary>
    private static int Probe(string environment, string outDir)
    {
        var env = EnvironmentInfo.Capture(environment);
        Console.WriteLine(JsonSerializer.Serialize(env, Indented));
        var failures = 0;
        foreach (var trainer in new[] { "fasttree", "lightgbm" })
        {
            try
            {
                var options = new SpikeOptions(environment, trainer, 6000, 2, 8, 7, 1, outDir);
                var report = new Spike(options, _ => { }).Run();
                var r = report.Results[0];
                Console.WriteLine($"PROBE {trainer}: OK  auc={r.Auc:F3} saveLoadDelta={r.SaveLoadMaxDelta:E1} contributions={r.TopContributions.Count} pfi={r.TopPermutationImportance.Count}");
            }
            catch (Exception ex)
            {
                failures++;
                Console.WriteLine($"PROBE {trainer}: FAILED  {ex.GetType().Name}: {FirstLine(ex)}");
                for (var inner = ex.InnerException; inner is not null; inner = inner.InnerException)
                {
                    Console.WriteLine($"    caused by {inner.GetType().Name}: {FirstLine(inner)}");
                }
            }
        }

        return failures == 0 ? 0 : 3;
    }

    private static string FirstLine(Exception ex) => ex.Message.Split('\n')[0].Trim();

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }

    private static string? Option(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static int Int(string[] args, string name, int fallback) => Option(args, name) is { } v ? int.Parse(v, CultureInfo.InvariantCulture) : fallback;
}
