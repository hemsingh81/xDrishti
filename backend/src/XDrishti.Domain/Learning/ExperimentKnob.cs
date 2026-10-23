using System.Globalization;

namespace XDrishti.Domain.Learning;

/// <summary>The one knob an experiment changes (P6-11, ADR 0072; doc 09 §12: one variable at a time). The values are what the two runs were made with; this type only names them and guards the cost rule.</summary>
/// <param name="Name">What is changed, 1 to 64 characters without control characters (for example <c>intraday_stop_atr</c>).</param>
/// <param name="Kind">The kind of assumption.</param>
/// <param name="BaselineValue">The value the baseline run used, 1 to 64 characters.</param>
/// <param name="CandidateValue">The value the candidate run used, 1 to 64 characters. Only a label: for <see cref="ExperimentKnobKind.Slippage"/> the direction of the change is read from the stored fill rules of the two runs (<see cref="FillCostComparison"/>), never from these.</param>
public sealed record ExperimentKnob(string Name, ExperimentKnobKind Kind, string BaselineValue, string CandidateValue)
{
    private const int MaxText = 64;

    /// <summary>The problems of the values, empty when they are usable.</summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        Text("name", Name, problems);
        Text("baseline value", BaselineValue, problems);
        Text("candidate value", CandidateValue, problems);
        if (!Enum.IsDefined(Kind))
        {
            problems.Add("the kind of knob is unknown");
        }

        if (SameValue(BaselineValue, CandidateValue))
        {
            problems.Add("the candidate value is the baseline value: nothing is changed");
        }

        return problems;
    }

    private static void Text(string what, string? value, List<string> problems)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxText || value.Any(char.IsControl))
        {
            problems.Add(what + " is required, at most " + MaxText.ToString(CultureInfo.InvariantCulture) + " characters, without control characters");
        }
    }

    /// <summary>True when the two values are the same text, or the same number (4, 4.0 and 04 are one value).</summary>
    public static bool SameValue(string? a, string? b) =>
        string.Equals(a, b, StringComparison.Ordinal) || (Number(a) is { } x && Number(b) is { } y && x == y);

    /// <summary>The number a value is, or null when it is not one: the form used for the experiment key.</summary>
    public static double? Number(string? text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) ? (v == 0 ? 0 : v) : null;
}
