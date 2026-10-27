using System.Globalization;
using System.Text.Json;
using XDrishti.Domain.Common;

namespace XDrishti.Domain.Assistant.Evaluation;

/// <summary>
/// The thresholds of <c>eval/thresholds.json</c> (spec P9-09 AC 9, D1): data about 27 of 30, plans 8 and 9 of 10 with no escape and every symbol covered, ideas 8 and 9 of 10 with intent 0.85 and no idea below 0.60, both declines right, events precision 0.80 once 200 rows exist.
/// Read from the file, never from code, so a change is reviewed. Pure: the text in, the table or a typed failure out.
/// </summary>
public sealed record ThresholdTable(
    int DataCases, int DataPassMin,
    int PlanCases, int PlanRawMin, int PlanRepairedMin, int PlanEscapeMax, bool PlanCoveredAll,
    int IdeaCases, int IdeaFirstShotMin, int IdeaRepairedMin, double IdeaIntentMin, double IdeaIntentFloor, int IdeaDeclineMin,
    decimal EventsPrecisionMin, int EventsMinRows)
{
    public const string InvalidCode = "eval.invalid_thresholds";

    public static Result<ThresholdTable> Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var data = root.GetProperty("data");
            var plans = root.GetProperty("plans");
            var ideas = root.GetProperty("ideas");
            var events = root.GetProperty("events");
            var table = new ThresholdTable(
                data.GetProperty("of").GetInt32(), data.GetProperty("pass_min").GetInt32(),
                plans.GetProperty("of").GetInt32(), plans.GetProperty("raw_pass_min").GetInt32(), plans.GetProperty("repaired_pass_min").GetInt32(), plans.GetProperty("escape_max").GetInt32(), plans.GetProperty("covered_all").GetBoolean(),
                ideas.GetProperty("of").GetInt32(), ideas.GetProperty("first_shot_valid_min").GetInt32(), ideas.GetProperty("repaired_valid_min").GetInt32(), ideas.GetProperty("intent_min").GetDouble(), ideas.GetProperty("intent_floor").GetDouble(), ideas.GetProperty("decline_correct_min").GetInt32(),
                decimal.Parse(events.GetProperty("precision_material_min").GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture), events.GetProperty("min_rows").GetInt32());
            return table.Sane() ? Result.Success(table) : Fail();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            return Fail();
        }
    }

    /// <summary>Data: at least the minimum of all cases; fewer cases than the set holds is not a pass.</summary>
    public SetOutcome EvaluateData(int passes, int cases)
    {
        if (cases != DataCases)
        {
            return SetOutcome.Of("data", SetState.NotRun, $"{cases} of {DataCases} cases ran");
        }

        return passes >= DataPassMin ? SetOutcome.Of("data", SetState.Pass) : SetOutcome.Of("data", SetState.Fail, $"data pass {passes} of {cases} is below {DataPassMin}");
    }

    public SetOutcome EvaluatePlans(PlanCounts c)
    {
        ArgumentNullException.ThrowIfNull(c);
        if (c.Cases != PlanCases)
        {
            return SetOutcome.Of("plans", SetState.NotRun, $"{c.Cases} of {PlanCases} cases ran");
        }

        var reasons = new List<string>();
        Below(reasons, "raw_pass", c.RawPass, PlanRawMin);
        Below(reasons, "repaired_pass", c.RepairedPass, PlanRepairedMin);
        if (c.Escapes > PlanEscapeMax)
        {
            reasons.Add($"escape {c.Escapes} is above {PlanEscapeMax}");
        }

        if (PlanCoveredAll && c.Covered != c.Cases)
        {
            reasons.Add($"covered {c.Covered} of {c.Cases}");
        }

        return reasons.Count == 0 ? SetOutcome.Of("plans", SetState.Pass) : SetOutcome.Of("plans", SetState.Fail, [.. reasons]);
    }

    public SetOutcome EvaluateIdeas(IdeaCounts c)
    {
        ArgumentNullException.ThrowIfNull(c);
        if (c.Cases != IdeaCases)
        {
            return SetOutcome.Of("ideas", SetState.NotRun, $"{c.Cases} of {IdeaCases} cases ran");
        }

        var reasons = new List<string>();
        Below(reasons, "first_shot_valid", c.FirstShotValid, IdeaFirstShotMin);
        Below(reasons, "repaired_valid", c.RepairedValid, IdeaRepairedMin);
        Below(reasons, "decline_correct", c.DeclineCorrect, IdeaDeclineMin);
        if (c.Intent < IdeaIntentMin)
        {
            reasons.Add($"intent {c.Intent.ToString("0.000", CultureInfo.InvariantCulture)} is below {IdeaIntentMin.ToString("0.00", CultureInfo.InvariantCulture)}");
        }

        if (c.LowestIdeaIntent < IdeaIntentFloor)
        {
            reasons.Add($"an idea has intent {c.LowestIdeaIntent.ToString("0.000", CultureInfo.InvariantCulture)}, below {IdeaIntentFloor.ToString("0.00", CultureInfo.InvariantCulture)}");
        }

        return reasons.Count == 0 ? SetOutcome.Of("ideas", SetState.Pass) : SetOutcome.Of("ideas", SetState.Fail, [.. reasons]);
    }

    /// <summary>The sentence the report prints under every rate (spec AC 9): the sample cannot separate the threshold from three cases fewer.</summary>
    public string SmallSampleLine() => $"{DataCases} cases cannot separate {DataPassMin} from {DataPassMin - 3} correct";

    private static void Below(List<string> reasons, string name, int value, int minimum)
    {
        if (value < minimum)
        {
            reasons.Add($"{name} {value} is below {minimum}");
        }
    }

    private bool Sane() =>
        DataCases > 0 && DataPassMin is > 0 && DataPassMin <= DataCases
        && PlanCases > 0 && PlanRawMin <= PlanRepairedMin && PlanRepairedMin <= PlanCases && PlanEscapeMax >= 0
        && IdeaCases > 0 && IdeaFirstShotMin <= IdeaRepairedMin && IdeaRepairedMin <= IdeaCases && IdeaIntentMin is >= 0 and <= 1 && IdeaIntentFloor is >= 0 and <= 1
        && EventsPrecisionMin is > 0 and <= 1 && EventsMinRows > 0;

    private static Result<ThresholdTable> Fail() => Result.Failure<ThresholdTable>(new Error(InvalidCode, "thresholds.json is not a valid threshold table", ErrorKind.Validation));
}
