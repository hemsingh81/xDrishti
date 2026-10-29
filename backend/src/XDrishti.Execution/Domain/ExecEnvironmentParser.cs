namespace XDrishti.Execution.Domain;

/// <summary>The one strict reading of the environment text: an exact, case-insensitive name; no trimming, no number, no list. Anything else is <see cref="ExecEnvironment.None"/>.</summary>
public static class ExecEnvironmentParser
{
    public static ExecEnvironment Parse(string? text) =>
        text is not null && Enum.TryParse<ExecEnvironment>(text, ignoreCase: true, out var e) && Enum.IsDefined(e) && string.Equals(text, e.ToString(), StringComparison.OrdinalIgnoreCase)
            ? e
            : ExecEnvironment.None;
}
