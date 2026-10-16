using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Portfolio.Dividends;

/// <summary>Structured log events of the dividends (event ids 4171 to 4179). Account slugs and ids only — never amounts, symbols or notes.</summary>
internal static partial class DividendsLog
{
    [LoggerMessage(EventId = 4171, Level = LogLevel.Information, Message = "Dividend {DividendId} of account {Slug} added")]
    public static partial void Added(ILogger logger, Guid dividendId, string slug);

    [LoggerMessage(EventId = 4172, Level = LogLevel.Information, Message = "Dividend {DividendId} of account {Slug} voided")]
    public static partial void Voided(ILogger logger, Guid dividendId, string slug);

    [LoggerMessage(EventId = 4173, Level = LogLevel.Information, Message = "Suggested dividend {LedgerEntryId} of account {Slug} accepted as dividend {DividendId}")]
    public static partial void SuggestionAccepted(ILogger logger, Guid ledgerEntryId, string slug, Guid dividendId);

    [LoggerMessage(EventId = 4174, Level = LogLevel.Information, Message = "Suggested dividend {LedgerEntryId} of account {Slug} dismissed")]
    public static partial void SuggestionDismissed(ILogger logger, Guid ledgerEntryId, string slug);
}
