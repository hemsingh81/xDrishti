using System.Globalization;
using System.Text;
using XDrishti.Domain.Market;
using XDrishti.Domain.Reporting;

namespace XDrishti.Domain.Paper;

/// <summary>
/// The wording of the weekly paper review (spec P10-03, criterion 8): the two pinned opening lines, the built-at line and the plain-text layout of the sections. Deterministic: no clock, no randomness; the same sections give the same text byte for byte.
/// </summary>
public static class ReviewText
{
    /// <summary>The first line of every review; the database refuses a stored text that does not start with it.</summary>
    public const string PaperOnly = "Paper only: no order was placed and none exists.";

    /// <summary>The second line of every review.</summary>
    public const string NothingChanged = "Nothing was changed: this review applies nothing; changing any setting is your own act.";

    /// <summary>The R of a figure, signed: <c>+0.50R</c>; a figure that is not a value is its state and reason.</summary>
    public static string R(Metric metric)
    {
        ArgumentNullException.ThrowIfNull(metric);
        return metric.State switch
        {
            MetricState.Value => Plus(metric.Number!.Value) + "R",
            MetricState.Provisional => $"{Plus(metric.Number!.Value)}R (provisional: {metric.Reason})",
            _ => $"{ReviewMetric.Name(metric.State)}: {metric.Reason}",
        };
    }

    /// <summary>A signed number with two decimals.</summary>
    public static string Plus(double value) => value.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture);

    /// <summary>Rupees, signed, two decimals, thousands separated.</summary>
    public static string Inr(decimal value) => value.ToString("+#,##0.00;-#,##0.00;0.00", CultureInfo.InvariantCulture);

    /// <summary>When it was built, in India time; for a week that has already ended, also that the state is the build date's.</summary>
    public static string BuiltLine(DateTimeOffset builtAt, DateOnly weekEnd)
    {
        var ist = IndiaTime.ToIst(builtAt);
        var line = string.Create(CultureInfo.InvariantCulture, $"Built at {ist:yyyy-MM-dd HH:mm} IST.");
        return DateOnly.FromDateTime(ist.DateTime) > weekEnd
            ? string.Create(CultureInfo.InvariantCulture, $"{line} State as of {ist:yyyy-MM-dd}, not as of the end of the week: tickets, trades and configuration are read as they are at the build date.")
            : line;
    }

    /// <summary>The plain text of a review: pinned lines, the title, the built line, then each section with its lines and its limits sentence.</summary>
    public static string Compose(string title, string builtLine, IReadOnlyList<ReviewSection> sections)
    {
        ArgumentNullException.ThrowIfNull(sections);
        var text = new StringBuilder();
        text.Append(PaperOnly).Append('\n').Append(NothingChanged).Append('\n').Append(title).Append('\n').Append(builtLine).Append('\n');
        var number = 1;
        foreach (var section in sections)
        {
            text.Append(CultureInfo.InvariantCulture, $"\n{number++}. {section.Title}\n");
            foreach (var line in section.Lines)
            {
                text.Append(line).Append('\n');
            }

            text.Append("Limits: ").Append(section.Limits).Append('\n');
        }

        return text.ToString();
    }
}
