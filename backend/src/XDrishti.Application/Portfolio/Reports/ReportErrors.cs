using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.Reports;

/// <summary>Failures of the report import use cases (codes are part of the API contract).</summary>
internal static class ReportErrors
{
    public static readonly Error OtherAccount = Error.Validation(ReportViews.OtherAccountCode, ReportViews.OtherAccountText);

    public static readonly Error CostsChanged = Error.Conflict(PortfolioErrors.ConflictCode, "The costs were changed meanwhile. Preview the file again.");

    public static readonly Error NamesChanged = Error.Conflict(PortfolioErrors.ConflictCode, "The remembered names were changed meanwhile. Preview the file again.");

    /// <summary>What the file looks like to the detector but cannot be imported (the P&amp;L reports and the like), or looks like nothing known: the accepted kinds are listed.</summary>
    public static Error Unknown() => PortfolioErrors.WrongFileKind(
        "unknown",
        "This file is not one that can be imported. Accepted: Dhan's global transaction report (CSV), DP charges sheet (XLS, XLSX or CSV), demat holding statement (CSV), a tradebook with a buy/sell column (CSV), and dividend payout lists (on the dividend import).");

    /// <summary>The preview the owner confirmed is no longer what the import would do: nothing was stored. The fresh preview comes with it.</summary>
    public static Error PreviewStale() => new(
        PortfolioErrors.PreviewStaleCode,
        "What the file would do changed since the preview (holdings, remembered names or what is stored) - nothing was applied. Review the fresh preview and confirm again.",
        ErrorKind.Conflict);

    public static Error DigestRequired() => PortfolioErrors.Invalid("digest", "Required: preview the file first and send its digest, so that only what you saw is imported.");
}
