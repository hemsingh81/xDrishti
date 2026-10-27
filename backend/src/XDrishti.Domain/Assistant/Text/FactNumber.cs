using XDrishti.Domain.Assistant.Grounding;

namespace XDrishti.Domain.Assistant.Text;

/// <summary>A figure of a fact, as the facts print it: the ticket scope it belongs to (empty = global), its sign and its value with the precision it was printed with.</summary>
public sealed record FactNumber(string Scope, bool Negative, ParsedNumber Number)
{
    /// <summary>The figures of every fact line, read with the same tokenizer that reads the text under test (so a year or an identifier is skipped on both sides).</summary>
    public static IReadOnlyList<FactNumber> Of(BriefingFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var list = new List<FactNumber>();
        foreach (var line in facts.Lines)
        {
            foreach (var figure in NumberTokenizer.Figures(line.Text))
            {
                if (figure.Number is { } number)
                {
                    list.Add(new FactNumber(line.Scope, IsNegative(figure.Text), number));
                }
            }
        }

        return list;
    }

    /// <summary>Whether a printed figure starts with a minus sign (ASCII, unicode minus or en dash) before its first digit.</summary>
    public static bool IsNegative(string printed)
    {
        ArgumentNullException.ThrowIfNull(printed);
        foreach (var c in printed)
        {
            if (c is '-' or '−' or '–')
            {
                return true;
            }

            if (char.IsAsciiDigit(c))
            {
                break;
            }
        }

        return false;
    }

    /// <summary>
    /// The matching rule (spec P9-06 D2): same sign and unit kind, and either the same printed value, or the fact's value rounded half away from zero to the token's own precision where the token is printed with fewer decimals (45.83 allows 45.8 and 46,
    /// not 45.9 and not 45.834). No arithmetic is allowed: a sum or a ratio is not in the facts.
    /// </summary>
    public bool Matches(ParsedNumber token, bool tokenNegative)
    {
        ArgumentNullException.ThrowIfNull(token);
        if (token.IsPercent != Number.IsPercent || (token.Value != 0m && tokenNegative != Negative))
        {
            return false;
        }

        var tokenUnit = token.HalfUnit * 2m;
        var factUnit = Number.HalfUnit * 2m;
        if (tokenUnit < factUnit)
        {
            return false;
        }

        if (tokenUnit == factUnit)
        {
            return token.Value == Number.Value;
        }

        return Math.Round(Number.Value / tokenUnit, 0, MidpointRounding.AwayFromZero) * tokenUnit == token.Value;
    }
}
