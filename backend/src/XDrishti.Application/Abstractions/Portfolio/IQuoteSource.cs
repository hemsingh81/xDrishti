namespace XDrishti.Application.Abstractions.Portfolio;

/// <summary>The market quote of one security (ADR 0019): the last traded price and the previous session's close. The price is positive; the previous close is null when the answer gave none that is usable.</summary>
public sealed record SecurityQuote(string SecurityId, decimal LastPrice, decimal? PreviousClose);

public enum QuoteFailureKind
{
    /// <summary>Dhan did not answer as expected (error status, timeout, network, an answer of another shape). The next tick tries again.</summary>
    Failed,

    /// <summary>Dhan refused the Data account's token or Data API subscription.</summary>
    Unauthorized,

    /// <summary>Dhan answered "too many requests". The next tick tries again.</summary>
    RateLimited,
}

/// <summary>Why a quote request failed. <see cref="Message"/> is a fixed, safe sentence: never a token, a URL or a part of Dhan's answer.</summary>
public sealed record QuoteFailure(QuoteFailureKind Kind, string Message);

/// <summary>
/// What a quote request yielded. With a <see cref="Failure"/> the result is unusable as a whole (a request that failed part-way leaves no half answer): <see cref="Quotes"/> is then empty. A security that is
/// missing from the answer, or whose price is not a positive number, is simply absent from <see cref="Quotes"/>.
/// </summary>
public sealed record QuoteResult(IReadOnlyList<SecurityQuote> Quotes, QuoteFailure? Failure = null, int Requests = 0)
{
    public static QuoteResult Failed(QuoteFailure failure, int requests) => new([], failure, requests);
}

/// <summary>
/// Market quotes for securities of the NSE equity segment from the broker's Data API (ADR 0019 §1). Read-only market data used by the holdings quote service; the implementation owns the HTTP, the
/// credentials of the primary Data account and the throttle, and never logs or returns a token, a client id or the broker's raw answer. A failure is a result, not an exception.
/// </summary>
public interface IQuoteSource
{
    /// <summary>Quotes the securities (Dhan security ids of the <c>NSE_EQ</c> segment, at most 1,000 per request; more are split into several requests). An empty list costs no request.</summary>
    ValueTask<QuoteResult> QuoteAsync(IReadOnlyCollection<string> securityIds, CancellationToken cancellationToken);
}
