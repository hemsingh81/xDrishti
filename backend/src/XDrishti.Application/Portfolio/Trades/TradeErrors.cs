using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.Trades;

/// <summary>Failures of the trade use cases (codes are part of the API contract).</summary>
internal static class TradeErrors
{
    public static readonly Error TradesChanged = Error.Conflict(PortfolioErrors.ConflictCode, "The trades were changed meanwhile. Preview the file again.");

    /// <summary>
    /// The holdings, lots, corporate actions or trades changed since the preview that the owner confirmed: nothing was applied. The fresh preview comes with the error as <c>report</c>.
    /// </summary>
    public static Error PreviewStale(TradePreviewReport report) => new(
        PortfolioErrors.PreviewStaleCode,
        "The holdings, lots, corporate actions or trades changed since the preview — nothing was applied. Review the fresh preview and confirm again.",
        ErrorKind.Conflict,
        Payload: new Dictionary<string, object> { ["report"] = report });
}
