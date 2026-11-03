using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using XDrishti.Domain.Common;

namespace XDrishti.Domain.Learning;

/// <summary>
/// The pre-registered definition of how a cash signal becomes an instrument expression (P14-06, ADR 0193): a kind plus parameters. Its canonical text is <c>expression-profile/1</c>, the kind and the parameters sorted by name with invariant
/// formatting; <see cref="Hash"/> is the SHA-256 of that text in lower hex. A profile is part of the identity of a re-mark run and of a ledger trial, never of a strategy version (P14-00 D4). P14-08 owns the parameter list of each kind.
/// </summary>
public sealed class ExpressionProfile
{
    /// <summary>The first line of the canonical text; a change of the format changes this and so every hash.</summary>
    public const string FormatLine = "expression-profile/1";

    /// <summary>The error code of a refused profile.</summary>
    public const string InvalidCode = "expression_profile.invalid";

    private const int MaxParameters = 32;
    private const int MaxNameLength = 32;
    private const int MaxValueLength = 64;

    private ExpressionProfile(Expression kind, IReadOnlyList<KeyValuePair<string, string>> parameters)
    {
        Kind = kind;
        Parameters = parameters;
        var text = new StringBuilder(FormatLine).Append('\n').Append("kind=").Append(kind.Code());
        foreach (var (name, value) in parameters)
        {
            text.Append('\n').Append(name).Append('=').Append(value);
        }

        CanonicalText = text.ToString();
        Hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(CanonicalText)));
    }

    /// <summary>The identity profile: kind <c>cash</c>, no parameters.</summary>
    public static ExpressionProfile Cash { get; } = new(Expression.Cash, []);

    /// <summary>The kind.</summary>
    public Expression Kind { get; }

    /// <summary>The parameters as canonical text, sorted by name.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Parameters { get; }

    /// <summary>The text that is hashed.</summary>
    public string CanonicalText { get; }

    /// <summary>SHA-256 of <see cref="CanonicalText"/> (UTF-8), 64 lower-case hex characters.</summary>
    public string Hash { get; }

    /// <summary>
    /// Makes a profile. A value is a string, a bool, an int, a long, a decimal or a finite double; numbers are written with invariant formatting and no trailing zeros, so the same numbers in any order give one hash. Cash takes no parameters.
    /// Refuses (does not throw) a name or value that is not canonical.
    /// </summary>
    public static Result<ExpressionProfile> Create(Expression kind, IEnumerable<KeyValuePair<string, object>> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (!Enum.IsDefined(kind))
        {
            return Invalid("the kind is not a known expression");
        }

        var list = new List<KeyValuePair<string, string>>();
        foreach (var (name, value) in parameters)
        {
            if (!ValidName(name))
            {
                return Invalid($"the parameter name '{name}' must be 1 to {MaxNameLength} characters of a-z, 0-9 and _, starting with a letter");
            }

            if (Write(value) is not { } text)
            {
                return Invalid($"the parameter '{name}' must be a string, a bool, a whole number, a decimal or a finite double of 1 to {MaxValueLength} characters without line breaks");
            }

            list.Add(new KeyValuePair<string, string>(name, text));
        }

        if (list.Count > MaxParameters)
        {
            return Invalid($"has at most {MaxParameters} parameters");
        }

        list.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
        for (var i = 1; i < list.Count; i++)
        {
            if (list[i].Key == list[i - 1].Key)
            {
                return Invalid($"gives the parameter '{list[i].Key}' twice");
            }
        }

        if (kind == Expression.Cash && list.Count > 0)
        {
            return Invalid("of kind cash takes no parameters");
        }

        return Result.Success(new ExpressionProfile(kind, list));
    }

    private static Result<ExpressionProfile> Invalid(string message) => Result.Failure<ExpressionProfile>(Error.Validation(InvalidCode, "An expression profile " + message + "."));

    private static bool ValidName(string? name) =>
        name is { Length: > 0 and <= MaxNameLength } && name[0] is >= 'a' and <= 'z' && name.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_');

    private static string? Write(object? value)
    {
        var text = value switch
        {
            string s => s,
            bool b => b ? "true" : "false",
            int i => i.ToString(CultureInfo.InvariantCulture),
            long l => l.ToString(CultureInfo.InvariantCulture),
            decimal d => d == 0 ? "0" : d.ToString("0.############################", CultureInfo.InvariantCulture),
            double x when double.IsFinite(x) => x == 0 ? "0" : x.ToString("R", CultureInfo.InvariantCulture),
            _ => null,
        };
        return text is { Length: > 0 and <= MaxValueLength } && !text.Any(char.IsControl) ? text : null;
    }
}
