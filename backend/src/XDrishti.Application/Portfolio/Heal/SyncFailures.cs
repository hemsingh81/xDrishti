using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Accounts.Connections;
using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.Heal;

/// <summary>
/// Why a sync failed, as a short stable code (P2-16, ADR 0027), and what follows from it: whether it may pass by itself (<see cref="IsTransient"/>: the sync is tried again quietly), whether only the owner can fix it (<see cref="IsOwnerOnly"/>: the owner
/// is told at once) and the plain sentence for the owner (<see cref="Text"/>). The codes are stored in the health rows and the run counts and are part of the API: never rename one. Pure.
/// </summary>
internal static class SyncFailures
{
    /// <summary>No usable answer from Dhan: a timeout, a network failure, a 5xx or an unreadable answer. Transient.</summary>
    public const string Unavailable = "unavailable";

    /// <summary>Dhan's rate limit (429, DH-904). Transient.</summary>
    public const string RateLimited = "rate_limited";

    /// <summary>The run was left running when the application stopped, or ran past its limit. Transient.</summary>
    public const string Interrupted = "interrupted";

    /// <summary>Dhan refused the request (another 4xx): trying again with the same input would not help.</summary>
    public const string Rejected = "rejected";

    /// <summary>One stretch of dates holds more trades than can be read in one go, even halved to one day.</summary>
    public const string HistoryTooLarge = "history_too_large";

    /// <summary>Anything else that went wrong inside the application (the log has it).</summary>
    public const string Unexpected = "unexpected";

    /// <summary>The Dhan access token ran out. Only the owner can fix it (sign in at Dhan again).</summary>
    public const string TokenExpired = "token_expired";

    /// <summary>Dhan refused the saved access token. Only the owner can fix it.</summary>
    public const string TokenRefused = "token_refused";

    /// <summary>The account is not connected to Dhan. Only the owner can fix it.</summary>
    public const string NotConnected = "not_connected";

    /// <summary>The Portfolio role of the account is off. Only the owner can fix it.</summary>
    public const string RoleOff = "role_off";

    private static readonly string[] Transient = [Unavailable, RateLimited, Interrupted];

    private static readonly string[] Token = [TokenExpired, TokenRefused, NotConnected];

    private static readonly string[] Owner = [TokenExpired, TokenRefused, NotConnected, RoleOff];

    /// <summary>The code of a failure result.</summary>
    public static string Of(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return error.Code switch
        {
            PortfolioErrors.TokenExpiredCode => TokenExpired,
            PortfolioErrors.NotConnectedCode => error.Message == PortfolioErrors.TokenRefused.Message ? TokenRefused : NotConnected,
            PortfolioErrors.HistoryTooLargeCode => HistoryTooLarge,
            PortfolioErrors.RoleOffCode => RoleOff,
            ConnectionErrors.BrokerErrorCode => ConnectionErrors.IsUnavailable(error) ? Unavailable : ConnectionErrors.IsRateLimited(error) ? RateLimited : Rejected,
            _ => Unexpected,
        };
    }

    /// <summary>The code of a failure that was recorded as a plain sentence (a run ended from outside the pipeline).</summary>
    public static string OfMessage(string? message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return Unexpected;
        }

        if (message.Contains("interrupted", StringComparison.OrdinalIgnoreCase) || message.Contains("did not finish", StringComparison.OrdinalIgnoreCase))
        {
            return Interrupted;
        }

        foreach (var known in new[] { PortfolioErrors.TokenExpired, PortfolioErrors.TokenRefused, PortfolioErrors.NotConnected, PortfolioErrors.HistoryTooLarge, PortfolioErrors.RoleOff })
        {
            if (message == known.Message)
            {
                return Of(known);
            }
        }

        return Unexpected;
    }

    /// <summary>The code of a failed quote request: <c>Failed</c> and <c>RateLimited</c> pass by themselves, a refused token needs the owner.</summary>
    public static string OfQuote(QuoteFailureKind kind) => kind switch
    {
        QuoteFailureKind.RateLimited => RateLimited,
        QuoteFailureKind.Unauthorized => TokenRefused,
        _ => Unavailable,
    };

    /// <summary>True when the failure may pass by itself, so the sync is tried again quietly.</summary>
    public static bool IsTransient(string? reason) => reason is not null && Transient.Contains(reason, StringComparer.Ordinal);

    /// <summary>True when only the owner can fix it: the owner is told at once.</summary>
    public static bool IsOwnerOnly(string? reason) => reason is not null && Owner.Contains(reason, StringComparer.Ordinal);

    /// <summary>True when the cause is the Dhan login (expired, refused or missing): signing in again fixes it, and the sync catches up by itself afterwards.</summary>
    public static bool IsLogin(string? reason) => reason is not null && Token.Contains(reason, StringComparer.Ordinal);

    /// <summary>The reason in plain words, lower case, to finish a sentence.</summary>
    public static string Text(string? reason) => reason switch
    {
        Unavailable => "Dhan could not be reached or gave an unexpected answer",
        RateLimited => "Dhan's rate limit was reached",
        Interrupted => "the sync was interrupted",
        Rejected => "Dhan refused the request",
        HistoryTooLarge => "Dhan returned more trades for one stretch of dates than can be read in one go",
        TokenExpired => "the Dhan login has expired",
        TokenRefused => "Dhan refused the saved login",
        NotConnected => "the account is not connected to Dhan",
        RoleOff => "the Portfolio role of the account is off",
        _ => "the sync failed unexpectedly (see the application log)",
    };

    /// <summary>The health kind of a broker history run.</summary>
    public static SyncHealthKind KindOf(BrokerSyncKind kind) => kind == BrokerSyncKind.Trades ? SyncHealthKind.Trades : SyncHealthKind.Ledger;
}
