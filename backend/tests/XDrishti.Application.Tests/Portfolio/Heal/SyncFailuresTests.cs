using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Accounts.Connections;
using XDrishti.Application.Portfolio.Heal;
using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Tests.Portfolio.Heal;

/// <summary>Why a sync failed, as a code, and what follows from it (ADR 0027): whether it passes by itself, whether only the owner can fix it.</summary>
public sealed class SyncFailuresTests
{
    public static TheoryData<Error, string> Errors => new()
    {
        { ConnectionErrors.Broker(new BrokerFailure(BrokerFailureKind.Unavailable)), "unavailable" }, // a timeout, a network failure, a 5xx
        { ConnectionErrors.Broker(new BrokerFailure(BrokerFailureKind.Rejected, "DH-904")), "rate_limited" }, // 429
        { ConnectionErrors.Broker(new BrokerFailure(BrokerFailureKind.Rejected, "DH-910")), "rejected" },
        { ConnectionErrors.Broker(new BrokerFailure(BrokerFailureKind.Rejected)), "rejected" },
        { PortfolioErrors.HistoryTooLarge, "history_too_large" },
        { PortfolioErrors.TokenExpired, "token_expired" },
        { PortfolioErrors.TokenRefused, "token_refused" },
        { PortfolioErrors.NotConnected, "not_connected" },
        { PortfolioErrors.RoleOff, "role_off" },
        { PortfolioErrors.SyncFailed, "unexpected" },
    };

    [Theory]
    [MemberData(nameof(Errors))]
    public void An_error_is_given_its_code(Error error, string code)
    {
        SyncFailures.Of(error).ShouldBe(code);
    }

    [Theory]
    [InlineData("The sync was interrupted because the application is stopping.", "interrupted")]
    [InlineData("The sync did not finish (the application stopped or crashed while it ran).", "interrupted")]
    [InlineData("Something else", "unexpected")]
    [InlineData(null, "unexpected")]
    public void A_failure_recorded_as_a_sentence_is_given_its_code(string? message, string code)
    {
        SyncFailures.OfMessage(message).ShouldBe(code);
    }

    [Fact]
    public void The_messages_of_the_known_errors_are_given_their_codes()
    {
        SyncFailures.OfMessage(PortfolioErrors.TokenExpired.Message).ShouldBe("token_expired");
        SyncFailures.OfMessage(PortfolioErrors.TokenRefused.Message).ShouldBe("token_refused");
    }

    [Theory]
    [InlineData(QuoteFailureKind.Failed, "unavailable")]
    [InlineData(QuoteFailureKind.RateLimited, "rate_limited")]
    [InlineData(QuoteFailureKind.Unauthorized, "token_refused")]
    public void A_failed_quote_request_is_given_its_code(QuoteFailureKind kind, string code)
    {
        SyncFailures.OfQuote(kind).ShouldBe(code);
    }

    [Theory]
    [InlineData("unavailable", true, false)]
    [InlineData("rate_limited", true, false)]
    [InlineData("interrupted", true, false)]
    [InlineData("rejected", false, false)]
    [InlineData("history_too_large", false, false)]
    [InlineData("unexpected", false, false)]
    [InlineData("token_expired", false, true)]
    [InlineData("token_refused", false, true)]
    [InlineData("not_connected", false, true)]
    [InlineData("role_off", false, true)]
    public void Only_a_reason_that_may_pass_is_transient_and_only_the_owners_to_fix_is_told_at_once(string reason, bool transient, bool ownerOnly)
    {
        (SyncFailures.IsTransient(reason), SyncFailures.IsOwnerOnly(reason)).ShouldBe((transient, ownerOnly));
    }

    [Theory]
    [InlineData("token_expired", true)]
    [InlineData("token_refused", true)]
    [InlineData("not_connected", true)]
    [InlineData("role_off", false)]
    [InlineData("unavailable", false)]
    public void A_login_reason_is_one_a_renewed_login_fixes(string reason, bool login)
    {
        SyncFailures.IsLogin(reason).ShouldBe(login);
    }
}
