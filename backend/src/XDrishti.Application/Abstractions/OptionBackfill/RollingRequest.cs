namespace XDrishti.Application.Abstractions.OptionBackfill;

/// <summary>
/// What the backfill asks of <c>POST /v2/charts/rollingoption</c> (P14-03): one underlying, one expiry kind and code, one strike offset from the money, one side, one window. Every Dhan shape here is UNVERIFIED until the owner's probe result is
/// recorded in ADR 0177.
/// </summary>
/// <param name="SecurityId">The Dhan id of the underlying (an index id, or a share's id).</param>
/// <param name="Instrument">Dhan's <c>OPTIDX</c> or <c>OPTSTK</c>.</param>
/// <param name="IntervalMinutes">5 for the indices, 15 for the shares (Dhan allows 1, 5, 15, 25, 60).</param>
/// <param name="ExpiryFlag"><c>W</c> (Dhan <c>WEEK</c>) or <c>M</c> (Dhan <c>MONTH</c>).</param>
/// <param name="ExpiryCode">Dhan's <c>expiryCode</c>: 1 is the nearest (assumed; UNVERIFIED), 2 the next.</param>
/// <param name="Offset">Strikes from the money, -10 to 10 (Dhan <c>ATM</c>, <c>ATM+3</c>, <c>ATM-3</c>).</param>
/// <param name="Side"><c>CE</c> (Dhan <c>CALL</c>) or <c>PE</c> (Dhan <c>PUT</c>).</param>
/// <param name="From">The first day of the window.</param>
/// <param name="To">The day after the last day of the window (Dhan's <c>toDate</c> is not inclusive).</param>
public sealed record RollingRequest(long SecurityId, string Instrument, int IntervalMinutes, char ExpiryFlag, int ExpiryCode, int Offset, string Side, DateOnly From, DateOnly To);

/// <summary>The outcome words of a fetch (the value of <c>options.fetch_log.outcome</c>).</summary>
public static class BackfillOutcomes
{
    public const string Ok = "ok";
    public const string Empty = "empty";
    public const string NoData = "no_data";
    public const string RateLimited = "rate_limited";
    public const string Timeout = "timeout";
    public const string ServerError = "server_error";
    public const string BadBody = "bad_body";
    public const string TokenRefused = "token_refused";
    public const string NotSubscribed = "not_subscribed";
    public const string DiskStop = "disk_stop";

    /// <summary>Dhan answered that the request itself is wrong (<c>DH-905</c>, bad or missing input): ends the run, because every key would fail the same way. Not a failed attempt of the key.</summary>
    public const string BadRequest = "bad_request";

    /// <summary>The request was sent but its answer could not be stored: a row says so, so that the daily cap still counts it. Not done, not a failed attempt of the key.</summary>
    public const string StoreFailed = "store_failed";

    /// <summary>A key with one of these is finished; it is never fetched again.</summary>
    public static bool IsDone(string outcome) => outcome is Ok or Empty or NoData;

    /// <summary>The outcomes that count as a failed attempt towards giving a key up (the others - a rate limit, a refused token, no plan, a disk stop - are not the key's fault; a rate limit has the run-level breaker, an answer that cannot be stored its own per-key count).</summary>
    public static bool IsFailedAttempt(string outcome) => outcome is Timeout or ServerError or BadBody;

    /// <summary>Every word, for the database check and the tests.</summary>
    public static IReadOnlyList<string> All { get; } = [Ok, Empty, NoData, RateLimited, Timeout, ServerError, BadBody, TokenRefused, NotSubscribed, DiskStop, BadRequest, StoreFailed];
}

/// <summary>
/// The answer to one request, as far as the transport can tell: the outcome word, the HTTP status, Dhan's own code (a well-formed <c>DH-9xx</c> or <c>80x</c> only), the body when the call succeeded and the time it took. Never carries a URL, a token
/// or a client id.
/// </summary>
public sealed record BackfillReply(string Outcome, int? HttpStatus, string? DhanCode, byte[]? Body, int LatencyMs, bool NoCredentials = false);
