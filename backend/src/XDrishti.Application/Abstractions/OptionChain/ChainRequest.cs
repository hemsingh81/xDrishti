namespace XDrishti.Application.Abstractions.OptionChain;

/// <summary>Which of the two allowed Dhan requests.</summary>
public enum ChainKind
{
    /// <summary>The expiry dates of one underlying (<c>POST /v2/optionchain/expirylist</c>).</summary>
    Expiries,

    /// <summary>The chain of one underlying and expiry (<c>POST /v2/optionchain</c>).</summary>
    Chain,
}

/// <summary>What the recorder asks: the kind, the Dhan underlying id, its segment (<c>IDX_I</c> or <c>NSE_EQ</c>) and, for a chain, the expiry.</summary>
public sealed record ChainRequest(ChainKind Kind, long UnderlyingScrip, string Segment, DateOnly? Expiry);

/// <summary>The outcome words of a request (the value of <c>options.chain_requests.outcome</c>).</summary>
public static class ChainOutcomes
{
    public const string Ok = "ok";
    public const string RateLimited = "rate_limited";
    public const string Timeout = "timeout";
    public const string ServerError = "server_error";
    public const string NoData = "no_data";
    public const string BadBody = "bad_body";
    public const string TokenRefused = "token_refused";
    public const string NotSubscribed = "not_subscribed";
    public const string SlotOverrun = "slot_overrun";
    public const string WindowEnd = "window_end";
    public const string DiskStop = "disk_stop";
}

/// <summary>
/// The answer to one request, as far as the transport can tell: the outcome word, the HTTP status and Dhan's own code (a well-formed <c>DH-9xx</c> only), the body when the call succeeded (for the recorder to parse), the time it took and whether
/// there was no usable credential to send at all. Never carries a URL, a token or a client id.
/// </summary>
public sealed record ChainReply(string Outcome, int? HttpStatus, string? DhanCode, byte[]? Body, int LatencyMs, bool NoCredentials = false);
