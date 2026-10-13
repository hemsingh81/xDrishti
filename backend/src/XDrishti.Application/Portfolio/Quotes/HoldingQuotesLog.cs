using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Portfolio.Quotes;

/// <summary>Structured log events of the holdings quote poll (event ids 4021 to 4030). Counts and slugs only — never a token, a client id, a URL, Dhan's raw answer or a holding. A tick dropped because the previous run goes on is logged by the services framework (<c>service.tick_dropped</c>).</summary>
internal static partial class HoldingQuotesLog
{
    [LoggerMessage(EventId = 4021, Level = LogLevel.Information, Message = "holdings_quotes.quoted securities={Quoted} of={Total} not_on_nse={NotOnNse} without_quote={WithoutQuote} holdings={Holdings} requests={Requests}")]
    public static partial void Quoted(ILogger logger, int quoted, int total, int notOnNse, int withoutQuote, int holdings, int requests);

    [LoggerMessage(EventId = 4022, Level = LogLevel.Warning, Message = "holdings_quotes.failed kind={Kind} requests={Requests}")]
    public static partial void Failed(ILogger logger, string kind, int requests);

    [LoggerMessage(EventId = 4023, Level = LogLevel.Information, Message = "holdings_quotes.skipped reason={Reason}")]
    public static partial void Skipped(ILogger logger, string reason);

    [LoggerMessage(EventId = 4024, Level = LogLevel.Information, Message = "holdings_quotes.empty: no open holdings, nothing to quote")]
    public static partial void NoHoldings(ILogger logger);
}
