using System.Globalization;
using System.Text.RegularExpressions;

namespace XDrishti.Domain.Filings.Extraction;

/// <summary>
/// The grounding rule for an order value (P15-08 decision 2): a number the model gives must be a number the text contains. The numbers of the text are read with Indian and western grouping (<c>1,23,45,000</c>, <c>1,234,567</c>) and plain decimals; a number followed by
/// <c>lakh</c> (also <c>lac</c>) is multiplied by 100,000 and one followed by <c>crore</c> (also <c>cr</c>) by 10,000,000. Currency marks (the rupee sign, <c>Rs.</c>, <c>INR</c>) are not part of a number. A number with a unit counts only with its unit applied (12.5 crore is 125,000,000, not 12.5).
/// The match is to the rupee. Only ASCII digits count. Pure.
/// </summary>
public static partial class GroundedAmount
{
    /// <summary>Whether <paramref name="value"/> equals, to the rupee, a number written in <paramref name="text"/>.</summary>
    public static bool IsGrounded(decimal value, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var wanted = Math.Round(value, 0, MidpointRounding.AwayFromZero);
        return Candidates(text).Any(c => Math.Round(c, 0, MidpointRounding.AwayFromZero) == wanted);
    }

    /// <summary>Every amount the text writes, with units applied.</summary>
    public static IEnumerable<decimal> Candidates(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        MatchCollection matches;
        try
        {
            matches = Number().Matches(text);
        }
        catch (RegexMatchTimeoutException)
        {
            yield break;
        }

        foreach (var match in matches.Cast<Match>())
        {
            if (!decimal.TryParse(match.Groups["num"].Value.Replace(",", string.Empty, StringComparison.Ordinal), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number))
            {
                continue;
            }

            var unit = match.Groups["unit"].Value.ToLowerInvariant();
            var scale = unit.StartsWith("lac", StringComparison.Ordinal) || unit.StartsWith("lakh", StringComparison.Ordinal) ? 100_000m : unit.StartsWith("cr", StringComparison.Ordinal) ? 10_000_000m : 1m;
            decimal scaled;
            try
            {
                scaled = checked(number * scale);
            }
            catch (OverflowException)
            {
                continue;
            }

            yield return scaled;
        }
    }

    [GeneratedRegex(@"(?<![0-9])(?<![0-9][.,])(?<num>[0-9]{1,3}(?:,[0-9]{2,3})+(?:\.[0-9]+)?|[0-9]+(?:\.[0-9]+)?)(?:[ \t]*(?<unit>lakhs?|lacs?|crores?|cr)(?![A-Za-z]))?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex Number();
}
