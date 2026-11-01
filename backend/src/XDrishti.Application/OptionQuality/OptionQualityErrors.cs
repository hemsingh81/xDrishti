using XDrishti.Domain.Common;

namespace XDrishti.Application.OptionQuality;

/// <summary>The expected failures of the coverage read.</summary>
public static class OptionQualityErrors
{
    public const string InvalidQueryCode = "options.invalid_query";

    public static Error InvalidQuery(string message) => Error.Validation(InvalidQueryCode, message);
}
