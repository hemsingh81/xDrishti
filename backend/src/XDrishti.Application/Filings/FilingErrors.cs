using XDrishti.Domain.Common;

namespace XDrishti.Application.Filings;

/// <summary>The expected failures of the filings reads.</summary>
public static class FilingErrors
{
    public const string InvalidQueryCode = "filings.invalid_query";

    public const string NotFoundCode = "filings.not_found";

    public static Error InvalidQuery(string message) => Error.Validation(InvalidQueryCode, message);

    public static Error NotFound() => Error.NotFound(NotFoundCode, "No filing with that id.");
}
