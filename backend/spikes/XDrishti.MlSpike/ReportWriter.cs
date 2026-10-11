using System.Globalization;
using System.Text;

namespace XDrishti.MlSpike;

public static class ReportWriter
{
    public static string Render(SpikeReport r)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine(inv, $"# ML.NET spike — {r.Environment.Name}").AppendLine();
        sb.AppendLine(inv, $"{r.Environment.Os} · {r.Environment.ProcessArchitecture} ({r.Environment.RuntimeIdentifier}) · {r.Environment.Framework} · {r.Environment.ProcessorCount} CPUs · cpu limit `{r.Environment.CpuLimit ?? "none"}` · memory limit `{r.Environment.MemoryLimit ?? "none"}` · user {r.Environment.User} · writable root fs: {r.Environment.RootFileSystemWritable}").AppendLine();
        sb.AppendLine(inv, $"Packages: {string.Join(", ", r.Environment.Packages.Select(kv => $"{kv.Key} {kv.Value}"))}").AppendLine();
        sb.AppendLine(inv, $"Native libraries seen: {(r.Environment.NativeLibraries.Count == 0 ? "none" : string.Join("; ", r.Environment.NativeLibraries))}").AppendLine();
        sb.AppendLine(inv, $"Data: {r.Options.Rows:N0} rows (generated in {r.GenerateSeconds:F1}s), positive rate {r.PositiveRate:P1}, logistic baseline AUC {r.LogisticAuc:F4}").AppendLine();
        sb.AppendLine("| Trainer | Threads | Train s (each) | AUC | Optimal AUC | Logistic AUC | LogLoss | Brier | ECE Platt | ECE ML.NET isotonic | ECE own PAV |").AppendLine("|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (var t in r.Results)
        {
            sb.AppendLine(inv, $"| {t.Trainer} | {t.Threads} | {string.Join(" / ", t.TrainSeconds.Select(x => x.ToString("F1", inv)))} | {t.Auc:F4} | {t.AucOptimal:F4} | {t.AucLogistic:F4} | {t.LogLoss:F4} | {t.Brier:F4} | {t.Ece.Raw * 100:F2} pp | {t.Ece.MlNetIsotonic * 100:F2} pp | {t.Ece.OwnPav * 100:F2} pp |");
        }

        sb.AppendLine().AppendLine("| Trainer | Batch rows/s | Single-row p50 ms | p95 ms | Model MB | Save/load max Δp | Determinism max Δscore | Peak RSS MB |").AppendLine("|---|---|---|---|---|---|---|---|");
        foreach (var t in r.Results)
        {
            sb.AppendLine(inv, $"| {t.Trainer} | {t.BatchRowsPerSecond:N0} | {t.SingleRowP50Ms:F3} | {t.SingleRowP95Ms:F3} | {t.ModelFileMb:F2} | {t.SaveLoadMaxDelta:E1} | {t.DeterminismMaxDelta:E1} | {t.PeakMemoryMb:F0} |");
        }

        foreach (var t in r.Results)
        {
            sb.AppendLine().AppendLine(inv, $"**{t.Trainer}** top contributions (planted features in top 6: {t.PlantedInTopContributions}/6): {string.Join(", ", t.TopContributions.Select(f => $"{f.Name} ({f.Score:F2})"))}");
            sb.AppendLine(inv, $"**{t.Trainer}** top permutation importance (planted in top 6: {t.PlantedInTopPermutation}/6): {string.Join(", ", t.TopPermutationImportance.Select(f => $"{f.Name} ({f.Score:F4})"))}");
        }

        return sb.ToString();
    }
}
