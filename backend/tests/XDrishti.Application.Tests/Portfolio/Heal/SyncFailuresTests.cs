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
        { ConnectionErrors.Broker(new BrokerFailure(BrokerFailureKind.RateLimited, "DH-904")), "rate_limited" }, // 429
        { ConnectionErrors.Broker(new BrokerFailure(BrokerFailureKind.Rejected, "DH-910")), "rejected" },
        { ConnectionErrors.Broker(new BrokerFailure(BrokerFailureKind.Rejected)), "rejected" },
        { PortfolioErrors.HistoryTooLarge, "history_too_large" },
        { PortfolioErrors.TokenExpired, "token_expired" },
        { PortfolioErrors.TokenRefused, "token_refused" },
        { PortfolioErrors.NotConnected, "not_connected" },
        { PortfolioErrors.RoleOff, "role_off" },
        { PortfolioErrors.SyncFailed, "unexpected" },
        { PortfolioErrors.HoldingChanged, "data_changed" }, // our own database collided: not "Dhan could not be reached"
    };

    [Theory]
    [MemberData(nameof(Errors))]
    public void An_error_is_given_its_code(Error error, string code)
    {
        SyncFailures.Of(error).ShouldBe(code);
    }

    [Fact]
    public void An_error_of_the_role_off_code_is_role_off_and_any_other_without_a_cause_is_unexpected()
    {
        (SyncFailures.Of(PortfolioErrors.RoleOff), SyncFailures.Of(PortfolioErrors.SyncRunning), SyncFailures.Of(Error.Conflict("x.y", "z"))).ShouldBe(("role_off", "unexpected", "unexpected"));
    }

    [Theory]
    [InlineData(FailureKind.Transient, "unavailable")]
    [InlineData(FailureKind.RateLimited, "rate_limited")]
    [InlineData(FailureKind.Rejected, "rejected")]
    [InlineData(FailureKind.TokenExpired, "token_expired")]
    [InlineData(FailureKind.TokenRefused, "token_refused")]
    [InlineData(FailureKind.NotConnected, "not_connected")]
    [InlineData(FailureKind.HistoryTooLarge, "history_too_large")]
    [InlineData(FailureKind.Unexpected, "unexpected")]
    [InlineData(FailureKind.DataChanged, "data_changed")]
    public void Every_failure_kind_has_its_stored_code(FailureKind kind, string code)
    {
        SyncFailures.Of(kind).ShouldBe(code);
    }

    [Fact]
    public void Rewording_every_message_changes_no_code_and_no_retry_decision()
    {
        foreach (var error in new[]
        {
            ConnectionErrors.Broker(new BrokerFailure(BrokerFailureKind.Unavailable)),
            ConnectionErrors.Broker(new BrokerFailure(BrokerFailureKind.RateLimited, "DH-904")),
            ConnectionErrors.Broker(new BrokerFailure(BrokerFailureKind.Rejected, "DH-910")),
            PortfolioErrors.HistoryTooLarge,
            PortfolioErrors.TokenExpired,
            PortfolioErrors.TokenRefused,
            PortfolioErrors.NotConnected,
            PortfolioErrors.RoleOff,
            PortfolioErrors.SyncFailed,
            PortfolioErrors.HoldingChanged,
        })
        {
            var reworded = error with { Message = "Completely different words, nothing like the original." };

            var (code, was) = (SyncFailures.Of(reworded), SyncFailures.Of(error));

            code.ShouldBe(was);
            (SyncFailures.IsTransient(code), SyncFailures.IsOwnerOnly(code), SyncFailures.IsLogin(code))
                .ShouldBe((SyncFailures.IsTransient(was), SyncFailures.IsOwnerOnly(was), SyncFailures.IsLogin(was)));
        }
    }

    [Fact]
    public void A_message_that_looks_like_another_failure_does_not_change_the_code()
    {
        var misleading = ConnectionErrors.Broker(new BrokerFailure(BrokerFailureKind.Rejected, "DH-904")) with { Message = "Dhan could not be reached. Try again in a moment. DH-904" };

        SyncFailures.Of(misleading).ShouldBe("rejected");
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
    [InlineData("data_changed", true, false)]
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

    [Fact]
    public void A_local_data_conflict_has_its_own_plain_text_and_never_blames_dhan()
    {
        var text = SyncFailures.Text(SyncFailures.Of(PortfolioErrors.HoldingChanged));

        text.ShouldBe("the data changed while it was being saved; the next run does it again");
        text.ShouldNotContain("Dhan");
        text.ShouldNotBe(SyncFailures.Text(SyncFailures.Unavailable));
    }
}
