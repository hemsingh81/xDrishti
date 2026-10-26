using System.Text.Json;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Learning.Jobs;

/// <summary>
/// Reads a stored run back into what a replay needs (spec P8-18 Part B, assumption 5). It fails closed: a record of another schema, a field that is missing or not what this reader knows, or fill rules that do not rebuild to the stored text, is refused with the reason
/// (<c>experiments.baseline_not_rebuildable</c>) and nothing is guessed. Pure over the header and the record text.
/// </summary>
internal static class ReplayBaselineReader
{
    public static Result<ReplayBaseline> Read(StoredRunHeader header, string recordJson)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(recordJson);
        if (header.RecordSchemaVersion != SimulationRun.CurrentSchemaVersion)
        {
            return Refuse("it was stored with record schema " + header.RecordSchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture) + ", this build replays schema " + SimulationRun.CurrentSchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        try
        {
            using var doc = JsonDocument.Parse(recordJson);
            return ReadRecord(header, doc.RootElement);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or KeyNotFoundException or OverflowException)
        {
            return Refuse("the stored record cannot be read");
        }
    }

    private static Result<ReplayBaseline> ReadRecord(StoredRunHeader header, JsonElement root)
    {
        var strategy = root.GetProperty("strategy");
        var id = strategy.GetProperty("id").GetString() ?? string.Empty;
        var parameters = new SortedDictionary<string, double>(StringComparer.Ordinal);
        foreach (var p in strategy.GetProperty("parameters").EnumerateObject())
        {
            parameters[p.Name] = p.Value.GetDouble();
        }

        var definition = strategy.GetProperty("definition_json").GetString() ?? string.Empty;
        var sha = strategy.GetProperty("definition_sha256").GetString() ?? string.Empty;
        if (!Timeframes.All.Any(t => t.ToCode() == strategy.GetProperty("evaluation_timeframe").GetString()) || !RunRecordText.IsHex64(sha) || id.Length == 0)
        {
            return Refuse("the strategy part of the record is not readable");
        }

        var options = root.GetProperty("options");
        var replayOptions = new ReplayOptions(options.GetProperty("min_session_completeness").GetDouble(), options.GetProperty("discontinuity_threshold").GetDouble());
        var fills = ReadFills(root.GetProperty("fills").GetString() ?? string.Empty, header.Fills);
        if (fills.IsFailure)
        {
            return Result.Failure<ReplayBaseline>(fills.Error);
        }

        var universe = root.GetProperty("universe");
        var ids = universe.GetProperty("members").EnumerateArray().Select(m => m.GetProperty("id").GetGuid()).ToList();
        Guid? basket = universe.GetProperty("basket_id") is { ValueKind: JsonValueKind.String } b ? b.GetGuid() : null;
        var kind = universe.GetProperty("kind").GetString();
        if (ids.Count == 0 || ids.Distinct().Count() != ids.Count || (kind == "basket" && basket is null) || kind is not ("basket" or "instrument_list"))
        {
            return Refuse("the universe of the record is not readable");
        }

        var costs = ReadCosts(root);
        if (costs is null)
        {
            return Refuse("the costs of the record are not readable");
        }

        var (exit, timeStop) = ReadExit(definition, parameters);
        return Result.Success(new ReplayBaseline(
            id,
            sha,
            parameters,
            strategy.GetProperty("evaluation_timeframe").GetString()!,
            DateOnly.Parse(root.GetProperty("from").GetString()!, System.Globalization.CultureInfo.InvariantCulture),
            DateOnly.Parse(root.GetProperty("to").GetString()!, System.Globalization.CultureInfo.InvariantCulture),
            replayOptions,
            root.GetProperty("acknowledge_csv_adjusted").GetBoolean(),
            fills.Value,
            ids,
            kind == "basket" ? basket : null,
            costs,
            exit,
            timeStop));
    }

    // The fill rules are rebuilt from their numbers and must give back the stored canonical text: a gap rule or a field this reader does not know fails here, not in a replay.
    private static Result<FillRules> ReadFills(string text, string headerFills)
    {
        using var doc = JsonDocument.Parse(text);
        var slippage = doc.RootElement.GetProperty("slippage");
        var config = SlippageConfig.Create(slippage.GetProperty("ticks").GetDouble(), slippage.GetProperty("bar_range_frac").GetDouble(), slippage.GetProperty("stop_exit_extra_ticks").GetDouble());
        var rules = config.IsSuccess ? FillRules.Create(doc.RootElement.GetProperty("entry_limit_percent").GetDouble(), config.Value) : Result.Failure<FillRules>(config.Error);
        return rules.IsSuccess && string.Equals(rules.Value.ToCanonicalJson(), text, StringComparison.Ordinal) && string.Equals(text, headerFills, StringComparison.Ordinal)
            ? rules
            : Refuse<FillRules>("its fill rules do not rebuild to the stored text");
    }

    private static CostChoice? ReadCosts(JsonElement root)
    {
        if (!root.TryGetProperty("costs", out var costs) || costs.ValueKind == JsonValueKind.Null)
        {
            return CostChoice.Gross;
        }

        var sheet = costs.GetProperty("sheet_id").GetString();
        var profile = costs.GetProperty("profile").GetProperty("name").GetString();
        return string.IsNullOrEmpty(sheet) || string.IsNullOrEmpty(profile) ? null : CostChoice.Sheet(sheet, costs.GetProperty("capital").GetDecimal(), profile, costs.GetProperty("max_cost_r").GetDecimal());
    }

    // One plain exit with numbers, the same on both sides, and at most a time stop overlay: anything else is null (the exit knobs are then not offered, and a slippage replay must reproduce the definition without it).
    private static (ExitCandidate? Exit, TimeStopChoice? TimeStop) ReadExit(string definitionJson, IReadOnlyDictionary<string, double> parameters)
    {
        using var doc = JsonDocument.Parse(definitionJson);
        var sides = new List<(string Exits, string Overlays, JsonElement Side)>();
        foreach (var name in new[] { "buy", "sell" })
        {
            if (doc.RootElement.TryGetProperty(name, out var side) && side.ValueKind == JsonValueKind.Object)
            {
                sides.Add((side.TryGetProperty("exits", out var e) ? e.GetRawText() : string.Empty, side.TryGetProperty("overlays", out var o) ? o.GetRawText() : string.Empty, side));
            }
        }

        if (sides.Count == 0 || sides.Select(s => s.Exits).Distinct().Count() != 1 || sides.Select(s => s.Overlays).Distinct().Count() != 1)
        {
            return (null, null);
        }

        var first = sides[0].Side;
        if (!first.TryGetProperty("exits", out var exits) || exits.ValueKind != JsonValueKind.Array || exits.GetArrayLength() != 1)
        {
            return (null, null);
        }

        var spec = exits[0];
        var policy = spec.GetProperty("policy").GetString();
        var values = Parameters(spec.GetProperty("parameters"), parameters);
        if (policy is null || values is null)
        {
            return (null, null);
        }

        var candidate = new ExitCandidate(policy, values);
        if (!first.TryGetProperty("overlays", out var overlays) || overlays.ValueKind != JsonValueKind.Array || overlays.GetArrayLength() == 0)
        {
            return (candidate, null);
        }

        if (overlays.GetArrayLength() != 1 || overlays[0].GetProperty("kind").GetString() != "time_stop" || Parameters(overlays[0].GetProperty("parameters"), parameters) is not { } stop)
        {
            return (null, null);
        }

        double Value(string name) => stop.FirstOrDefault(p => p.Name == name)?.Values[0] ?? 0;
        return stop.Any(p => p.Name == "min_progress_rr") ? (candidate, new TimeStopChoice(Value("min_progress_rr"), (int)Value("intraday_bars"), (int)Value("swing_sessions"))) : (null, null);
    }

    // The values of a parameter list as numbers; a name that points at a strategy parameter is read through the effective parameters of the run. null when any value cannot be read.
    private static List<ExitParameterValue>? Parameters(JsonElement list, IReadOnlyDictionary<string, double> parameters)
    {
        var result = new List<ExitParameterValue>();
        foreach (var p in list.EnumerateArray())
        {
            var values = new List<double>();
            foreach (var v in p.GetProperty("values").EnumerateArray())
            {
                if (v.ValueKind == JsonValueKind.Number)
                {
                    values.Add(v.GetDouble());
                }
                else if (v.ValueKind == JsonValueKind.String && parameters.TryGetValue(v.GetString()!.TrimStart('$'), out var named))
                {
                    values.Add(named);
                }
                else
                {
                    return null;
                }
            }

            if (values.Count == 0)
            {
                return null;
            }

            result.Add(new ExitParameterValue(p.GetProperty("name").GetString()!, values));
        }

        return result;
    }

    private static Result<ReplayBaseline> Refuse(string reason) => Refuse<ReplayBaseline>(reason);

    private static Result<T> Refuse<T>(string reason) => Result.Failure<T>(ExperimentJobErrors.NotRebuildable(reason));
}
