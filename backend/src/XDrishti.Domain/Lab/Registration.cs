using System.Globalization;
using System.Text;
using XDrishti.Domain.Common;
using XDrishti.Domain.Research;

namespace XDrishti.Domain.Lab;

/// <summary>
/// The pre-registration of a strategy version (spec CF-06, D1): what the owner commits to in words before the data is searched. Five plain-text parts; none is ever rendered as markup. A registration is frozen: it is stored once per version and never changed.
/// </summary>
/// <param name="Counterparty">(a) Who is on the other side of the trade and why they lose (a forced seller, a slow reaction, a risk-transfer premium).</param>
/// <param name="ParameterSigns">(b) The expected sign of the effect of every parameter.</param>
/// <param name="Applicability">(c) The instrument tiers and the sessions the idea applies to.</param>
/// <param name="ExpectedEdge">(d) The expected gross edge in basis points and the cost it must beat.</param>
/// <param name="Falsifier">(e) What would retire it ("if out-of-sample expectancy is under X or the placebo scores within Y, retire").</param>
public sealed record Registration(string Counterparty, string ParameterSigns, string Applicability, string ExpectedEdge, string Falsifier)
{
    /// <summary>The most characters of one part.</summary>
    public const int MaxPartLength = 2000;

    /// <summary>The names of the parts, in order, as the API spells them.</summary>
    public static readonly IReadOnlyList<string> PartNames = ["counterparty", "parameterSigns", "applicability", "expectedEdge", "falsifier"];

    /// <summary>The five texts in the order of <see cref="PartNames"/>.</summary>
    public IReadOnlyList<string> Parts => [Counterparty, ParameterSigns, Applicability, ExpectedEdge, Falsifier];

    /// <summary>
    /// Checks and trims the five parts: each is non-empty after trimming, at most <see cref="MaxPartLength"/> characters, and holds no control character other than a line feed, a carriage return or a tab. Every problem is listed.
    /// </summary>
    public static Result<Registration> Create(string? counterparty, string? parameterSigns, string? applicability, string? expectedEdge, string? falsifier)
    {
        string?[] raw = [counterparty, parameterSigns, applicability, expectedEdge, falsifier];
        var fields = new List<FieldError>();
        var parts = new string[raw.Length];
        for (var i = 0; i < raw.Length; i++)
        {
            parts[i] = (raw[i] ?? string.Empty).Trim();
            var problem = Problem(parts[i]);
            if (problem is not null)
            {
                fields.Add(new FieldError(PartNames[i], problem));
            }
        }

        return fields.Count == 0
            ? Result.Success(new Registration(parts[0], parts[1], parts[2], parts[3], parts[4]))
            : Result.Failure<Registration>(new Error("lab.invalid_input", "The registration has problems: " + string.Join("; ", fields.Select(f => f.Field + " " + f.Message)), ErrorKind.Validation, Fields: fields));
    }

    /// <summary>The lower-case hex SHA-256 of the canonical text: each part with its length in characters, so no part can run into the next.</summary>
    public string TextHash()
    {
        var text = new StringBuilder("xd-registration 1\n");
        for (var i = 0; i < PartNames.Count; i++)
        {
            text.Append(PartNames[i]).Append('=').Append(Parts[i].Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(Parts[i]).Append('\n');
        }

        return CanonicalText.Sha256Hex(text.ToString());
    }

    private static string? Problem(string part)
    {
        if (part.Length == 0)
        {
            return "is required";
        }

        if (part.Length > MaxPartLength)
        {
            return "is longer than " + MaxPartLength.ToString(CultureInfo.InvariantCulture) + " characters";
        }

        return part.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t')) ? "holds a control character" : null;
    }
}
