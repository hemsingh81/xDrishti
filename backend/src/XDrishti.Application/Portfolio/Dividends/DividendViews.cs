using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.Dividends;

/// <summary>Turns the stored dividends into the shapes of the API.</summary>
internal static class DividendViews
{
    public static DividendView Dividend(Dividend dividend) => new(
        dividend.Id,
        dividend.AccountId,
        dividend.HoldingId,
        dividend.TradingSymbol,
        dividend.Isin,
        dividend.PayDate,
        dividend.Amount,
        dividend.PerShare,
        dividend.Note,
        dividend.Source.ToString().ToLowerInvariant(),
        dividend.CreatedBy,
        dividend.CreatedAt,
        dividend.IsVoided,
        dividend.VoidedAt,
        dividend.VoidedBy);

    public static SuggestionDecisionView Decision(DividendSuggestion decision, Guid ledgerEntryId) => new(ledgerEntryId, decision.State.ToString().ToLowerInvariant(), decision.DividendId);
}
