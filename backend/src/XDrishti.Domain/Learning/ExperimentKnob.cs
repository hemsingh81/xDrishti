using System.Globalization;

namespace XDrishti.Domain.Learning;

/// <summary>The one knob an experiment changes (P6-11, ADR 0072; doc 09 §12: one variable at a time). The values are what the two runs were made with; this type only names them and guards the cost rule.</summary>
/// <param name="Name">What is changed, 1 to 64 characters without control characters (for example <c>intraday_stop_atr</c>).</param>
/// <param name="Kind">The kind of assumption.</param>
/// <param name="BaselineValue">The value the baseline run used, 1 to 64 characters.</param>
/// <param name="CandidateValue">The value the candidate run used, 1 to 64 characters; for <see cref="ExperimentKnobKind.Slippage"/> both are basis points.</param>
public sealed record ExperimentKnob(string Name, ExperimentKnobKind Kind, string BaselineValue, string CandidateValue)
{
    private const int MaxText = 64;

    /// <summary>The candidate assumes less cost than the baseline: a slippage in basis points below the baseline's. Such a candidate cannot win (doc 09 §12 rule 3).</summary>
    public bool IsCheaperAssumption =>
        Kind == ExperimentKnobKind.Slippage && Bps(BaselineValue) is { } baseline && Bps(CandidateValue) is { } candidate && candidate < baseline;

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
        else if (Kind == ExperimentKnobKind.Slippage && (Bps(BaselineValue) is null || Bps(CandidateValue) is null))
        {
            problems.Add("a slippage is a number of basis points, zero or more");
        }

        if (string.Equals(BaselineValue, CandidateValue, StringComparison.Ordinal))
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

    private static double? Bps(string? text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) && v >= 0 ? v : null;
}
