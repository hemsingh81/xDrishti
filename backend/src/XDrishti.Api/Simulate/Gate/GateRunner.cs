using System.Globalization;
using System.Text;
using System.Text.Json;
using XDrishti.Api.Simulate.Assist;
using XDrishti.Domain.Market.Bars;
using XDrishti.SimExport.Verification;
using XDrishti.SimReader;

namespace XDrishti.Api.Simulate.Gate;

/// <summary>What the gate did: the process exit code (the status's, or the assisted read's when it could not finish), the console lines and the status (null when no decision was made and no gate file was written).</summary>
internal sealed record GateOutcome(int Exit, IReadOnlyList<string> Lines, GateStatus? Status);

/// <summary>
/// The automated gate (spec P3-09d, ADR 0048): the assisted read of <see cref="AssistRunner"/> unchanged, then the spot checks of the stored bars, then the pure <see cref="GateRules"/>, then <c>gate-decision.json</c> and <c>gate-decision.md</c> in the pack folder.
/// It never reads or writes <c>check-log.csv</c> and never writes an owner's verdict. The engine's values are read by the assisted read after its log is written; the spot checks read only the stored bars and <c>pack.json</c>.
/// </summary>
internal static class GateRunner
{
    public const string JsonName = "gate-decision.json";

    public const string MarkdownName = "gate-decision.md";

    /// <summary>How many days back the previous session of a flagged day is looked for (a long holiday run is at most nine days; seven covers the usual gaps and the check says so when it finds none).</summary>
    public const int LookbackDays = 7;

    public static async Task<GateOutcome> RunAsync(IPackFolder folder, ISecondSource source, IStoredDays stored, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(stored);

        var assist = await AssistRunner.RunAsync(folder, source, stored, cancellationToken);
        if (assist.Exit != SimulateCommand.Done || assist.Details is not { } details)
        {
            return new GateOutcome(assist.Exit, assist.Lines, null);
        }

        var packBytes = folder.Read(PackFiles.PackJsonName);
        var spots = packBytes is null ? null : ReadSpots(packBytes);
        if (packBytes is null || spots is null)
        {
            return new GateOutcome(SimulateCommand.UsageError, ["pack.json has no readable spot lines"], null);
        }

        var integrity = PackIntegrity.Check(packBytes, AssistRunner.Hash(packBytes), folder.Read(ExportPaths.ManifestName), details.Engine);
        var tradeDays = details.Verdicts.Select(v => (v.Row.Pack.Symbol, v.Row.Pack.Date)).ToHashSet();
        var results = new List<SpotResult>();
        foreach (var spot in spots)
        {
            var day = await stored.GetAsync(spot.Symbol, spot.Date, cancellationToken);
            ReaderBar? previous = null;
            if (spot.Kind == "flagged_day")
            {
                previous = await PreviousAsync(stored, spot, cancellationToken);
            }

            results.Add(SpotRules.Evaluate(spot, new SpotDay(day.Bars, previous), tradeDays.Contains((spot.Symbol, spot.Date))));
        }

        if (spots.Count == 0)
        {
            results.Add(new SpotResult(new SpotLine(0, "spot|none", "none", string.Empty, string.Empty, default), SpotOutcome.NotMachineCheckable, string.Empty, "the pack has 0 spot lines: no excluded session, flagged day or open position was checked"));
        }

        var decision = GateRules.Decide(details.Verdicts, results, details.Engine, integrity);
        var inputs = new List<(string, string)>
        {
            (PackFiles.PackJsonName, AssistRunner.Hash(packBytes)),
            (PackFiles.EngineValuesName, assist.EngineHash!),
            (DiskPackFolder.LogName, assist.LogHash!),
            (AssistBarsCsv.CacheName, details.CacheHash),
        };
        if (folder.Read(ExportPaths.ManifestName) is { } manifest)
        {
            inputs.Add((ExportPaths.ManifestName, AssistRunner.Hash(manifest)));
        }

        folder.Write(JsonName, Encoding.UTF8.GetBytes(GateText.Json(details.PackId, details.Seed, decision, inputs)));
        folder.Write(MarkdownName, Encoding.UTF8.GetBytes(GateText.Markdown(details.PackId, details.Seed, decision, inputs)));
        return new GateOutcome(GateStatuses.ExitCode(decision.Status), Lines(decision), decision.Status);
    }

    private static List<string> Lines(GateDecision d)
    {
        var c = d.Counts;
        var lines = new List<string>
        {
            "gate status: " + GateStatuses.Code(d.Status),
            string.Create(CultureInfo.InvariantCulture, $"rows {c.Rows}; defects {c.Defects}; data repairs {c.Repairs}; rerun rows {c.Reruns}; data findings {c.DataFindings}; spot lines confirmed {c.SpotsConfirmed}, contradicted {c.SpotsContradicted}, not machine-checkable {c.SpotsNotCheckable}"),
            "spot lines in the pack: " + (d.Spots.Count(s => s.Line.Seq != 0 || s.Line.Kind != "none")).ToString(CultureInfo.InvariantCulture) + (d.Spots.Any(s => s.Line.Kind == "none") ? " (NONE: no spot line was checked; reported as not machine-checkable)" : string.Empty),
            "next actions:",
        };
        lines.AddRange(d.NextActions.Select((a, i) => "  " + (i + 1).ToString(CultureInfo.InvariantCulture) + ". " + a));
        lines.Add("written: " + JsonName + ", " + MarkdownName + " (check-log.csv was not touched and no verdict was written)");
        lines.Add(GateText.Limit);
        return lines;
    }

    private static async Task<ReaderBar?> PreviousAsync(IStoredDays stored, SpotLine spot, CancellationToken cancellationToken)
    {
        for (var back = 1; back <= LookbackDays; back++)
        {
            var day = await stored.GetAsync(spot.Symbol, spot.Date.AddDays(-back), cancellationToken);
            var last = day.Bars.Where(b => SessionClock.InRegularSession(SessionClock.MinuteOfDay(b.EpochSeconds))).OrderBy(b => b.EpochSeconds).LastOrDefault();
            if (last is not null)
            {
                return last;
            }
        }

        return null;
    }

    /// <summary>The spot lines of pack.json (null when the file has no <c>spots</c> array of the expected shape).</summary>
    internal static List<SpotLine>? ReadSpots(byte[] packJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(packJson);
            var list = new List<SpotLine>();
            foreach (var s in doc.RootElement.GetProperty("spots").EnumerateArray())
            {
                var key = s.GetProperty("key").GetString()!;
                var parts = key.Split('|');
                var kind = parts[1].Split(':', 2);
                list.Add(new SpotLine(s.GetProperty("seq").GetInt32(), key, kind[0], kind.Length > 1 ? kind[1] : string.Empty, s.GetProperty("symbol").GetString()!, DateOnly.ParseExact(s.GetProperty("date").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture)));
            }

            return list;
        }
        catch (Exception bad) when (bad is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or IndexOutOfRangeException)
        {
            return null;
        }
    }
}
