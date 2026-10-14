using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.DividendImport;

/// <summary>Failures of the dividend import use cases (codes are part of the API contract).</summary>
internal static class DividendImportErrors
{
    public static readonly Error DividendsChanged = Error.Conflict(PortfolioErrors.ConflictCode, "The dividends or the remembered names were changed meanwhile. Preview the file again.");

    /// <summary>The holdings, aliases or dividends changed in a way that matters to a row since the preview that the owner confirmed: nothing was stored. The fresh preview comes with the error as <c>report</c>.</summary>
    public static Error PreviewStale(DividendPreviewReport report) => new(
        PortfolioErrors.PreviewStaleCode,
        "The holdings, remembered names or dividends changed since the preview — nothing was imported. Review the fresh preview and confirm again.",
        ErrorKind.Conflict,
        Payload: new Dictionary<string, object> { ["report"] = report });
}
