using System.Globalization;
using System.Text;
using XDrishti.Domain.Research;

namespace XDrishti.Domain.Lab;

/// <summary>
/// The hash of a frozen plan (spec CF-06, D2): the SHA-256 of a canonical JSON text with fixed key order. Changing any one input changes it; the order of the sweep parameters, of their values and of the exit flags does not, because a plan is a set of choices, not a sequence.
/// </summary>
public static class PlanHash
{
    /// <summary>The canonical text of the plan.</summary>
    public static string Text(PlanInput plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var sweeps = plan.Sweeps
            .OrderBy(s => s.Parameter, StringComparer.Ordinal)
            .Select(s => "{\"parameter\":" + Quote(s.Parameter) + ",\"values\":[" + string.Join(",", s.Values.Order().Select(CanonicalText.Number)) + "]}");
        var exits = plan.ExitFlags.Order(StringComparer.Ordinal).Select(Quote);
        var text = new StringBuilder("{\"v\":1");
        text.Append(",\"strategy\":").Append(Quote(plan.StrategyHash))
            .Append(",\"universe\":").Append(Quote(plan.UniverseLabel))
            .Append(",\"members\":").Append(Quote(plan.UniverseMembersHash))
            .Append(",\"from\":").Append(Quote(plan.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))
            .Append(",\"to\":").Append(Quote(plan.To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))
            .Append(",\"costs\":").Append(Quote(plan.CostSheet))
            .Append(",\"sweeps\":[").Append(string.Join(",", sweeps)).Append(']')
            .Append(",\"exits\":[").Append(string.Join(",", exits)).Append(']')
            .Append(",\"slippage\":").Append(Quote(plan.SlippagePreset))
            .Append(",\"registration\":").Append(Quote(plan.RegistrationHash))
            .Append('}');
        return text.ToString();
    }

    /// <summary>The lower-case hex SHA-256 of <see cref="Text"/>.</summary>
    public static string Of(PlanInput plan) => CanonicalText.Sha256Hex(Text(plan));

    private static string Quote(string value)
    {
        var text = new StringBuilder("\"");
        foreach (var c in value)
        {
            text.Append(c switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                < ' ' => "\\u" + ((int)c).ToString("x4", CultureInfo.InvariantCulture),
                _ => c.ToString(),
            });
        }

        return text.Append('"').ToString();
    }
}
