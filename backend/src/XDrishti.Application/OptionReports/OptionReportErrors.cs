using XDrishti.Domain.Common;

namespace XDrishti.Application.OptionReports;

/// <summary>The expected failures of the P14 report reads.</summary>
public static class OptionReportErrors
{
    public const string InvalidQueryCode = "options.invalid_query";

    public const string NotFoundCode = "research.not_found";

    public static Error InvalidQuery(string message) => Error.Validation(InvalidQueryCode, message);

    public static Error RunNotFound() => Error.NotFound(NotFoundCode, "No expression run with that id is stored.");
}
