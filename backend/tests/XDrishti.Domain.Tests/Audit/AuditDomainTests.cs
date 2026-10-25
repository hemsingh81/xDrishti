using XDrishti.Domain.Accounts;
using XDrishti.Domain.Audit;
using XDrishti.Domain.Auth;

namespace XDrishti.Domain.Tests.Audit;

public sealed class AuditDomainTests
{
    [Fact]
    public void Every_sign_in_event_has_a_category()
    {
        foreach (var type in Enum.GetValues<AuditEventType>())
        {
            AuditKinds.CategoryOf(type).ShouldNotBeNull($"{type} has no row in the audit category table");
        }
    }

    [Fact]
    public void Every_account_event_has_a_category()
    {
        foreach (var type in Enum.GetValues<AccountEventType>())
        {
            AuditKinds.CategoryOf(type).ShouldNotBeNull($"{type} has no row in the audit category table");
        }
    }

    [Fact]
    public void Failed_and_refused_outcomes_are_the_listed_kinds_and_everything_else_is_ok()
    {
        Enum.GetValues<AuditEventType>().Where(t => AuditKinds.OutcomeOf(t) == AuditKinds.Failed).ShouldBe([AuditEventType.LoginFailed, AuditEventType.StepUpFailed], ignoreOrder: true);
        Enum.GetValues<AuditEventType>().Where(t => AuditKinds.OutcomeOf(t) == AuditKinds.Refused).ShouldBe([AuditEventType.AccountLocked, AuditEventType.LoginRefusedLocked], ignoreOrder: true);
        Enum.GetValues<AccountEventType>().Where(t => AuditKinds.OutcomeOf(t) != AuditKinds.Ok).ShouldBe([AccountEventType.BrokerConnectFailed]);
        AuditKinds.OutcomeOf(AuditSource.Approval, "approval.reject").ShouldBe(AuditKinds.Ok);
    }

    [Fact]
    public void An_unknown_kind_is_other_and_a_label_has_words()
    {
        AuditKinds.CategoryOf(AuditSource.Account, "SomethingNew").ShouldBe("other");
        AuditKinds.Label(AuditSource.Auth, "LoginFailed").ShouldBe("Login failed");
        AuditKinds.Label(AuditSource.Learning, "learning.revert").ShouldBe("Move reverted");
    }

    [Theory]
    [InlineData("password=hunter2")]
    [InlineData("Password: hunter2 afterwards")]
    [InlineData("token: abc123")]
    [InlineData("client_id=1100000001")]
    [InlineData("Bearer eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.abcdefghijk")]
    [InlineData("eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.abcdefghijk")]
    [InlineData("0123456789abcdef0123456789abcdef01234567")]
    [InlineData("AbCdEfGhIjKlMnOpQrStUvWxYz0123456789AbCdEfGhIjKlMnOpQrStUvWxYz01234567")]
    public void A_secret_shape_is_masked(string secret)
    {
        var (text, redacted) = AuditRedactor.Redact("before " + secret + " after");

        redacted.ShouldBeTrue();
        text.ShouldContain(AuditRedactor.Mask);
        text.ShouldNotContain("hunter2");
        text.ShouldNotContain("abc123");
        text.ShouldNotContain("1100000001");
        text.ShouldNotContain("eyJ");
        text.ShouldNotContain("0123456789abcdef");
        text.ShouldNotContain("AbCdEfGhIj");
        AuditRedactor.Redact(text).ShouldBe((text, false));
    }

    [Theory]
    [InlineData("5 holdings imported")]
    [InlineData("tokens are valid for 5 minutes")]
    [InlineData("Risk profile Balanced updated: risk 1% to 1.5%")]
    [InlineData("cell ORB|15m|BUY|intraday|FIXED_RR|* moved to paper")]
    public void Ordinary_text_is_unchanged(string text) => AuditRedactor.Redact(text).ShouldBe((text, false));

    [Fact]
    public void A_cursor_round_trips_and_a_tampered_one_is_refused()
    {
        var position = new AuditPosition(new DateTimeOffset(2026, 10, 12, 6, 30, 0, TimeSpan.Zero), AuditSource.Approval, "42");

        AuditPosition.TryDecode(position.Encode(), out var back).ShouldBeTrue();
        back.ShouldBe(position);
        AuditPosition.TryDecode(position.Encode()[..^2] + "AA", out _).ShouldBeFalse();
        AuditPosition.TryDecode("not a cursor", out _).ShouldBeFalse();
        AuditPosition.TryDecode(null, out _).ShouldBeFalse();
        AuditPosition.TryDecode(new string('A', 300), out _).ShouldBeFalse();
    }

    [Fact]
    public void The_window_default_is_30_days_in_india_and_a_range_is_at_most_366_days()
    {
        var window = AuditWindow.Default(new DateTimeOffset(2026, 10, 12, 20, 0, 0, TimeSpan.Zero));

        window.To.ShouldBe(new DateOnly(2026, 10, 13));
        window.From.ShouldBe(new DateOnly(2026, 9, 14));
        AuditWindow.Problem(new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1)).ShouldBeNull();
        AuditWindow.Problem(new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 2)).ShouldNotBeNull();
        AuditWindow.Problem(new DateOnly(2026, 2, 1), new DateOnly(2026, 1, 1)).ShouldNotBeNull();
    }

    [Fact]
    public void Actors_are_kinds_and_a_foreign_sign_in_address_is_not_shown()
    {
        AuditActors.Classify(AuditSource.Auth, "Owner@Example.com", "owner@example.com").ShouldBe((AuditActors.Owner, "Owner@Example.com"));
        AuditActors.Classify(AuditSource.Auth, "hunter2@x", "owner@example.com").ShouldBe((AuditActors.OtherAddress, null));
        AuditActors.Classify(AuditSource.Auth, null, "owner@example.com").Kind.ShouldBe(AuditActors.System);
        AuditActors.Classify(AuditSource.Approval, "rule:autonomy", "o@x").Kind.ShouldBe(AuditActors.Rule);
        AuditActors.Classify(AuditSource.Approval, "owner:o@x", "o@x").Kind.ShouldBe(AuditActors.Owner);
        AuditActors.Classify(AuditSource.Account, "system:refresh", null).Kind.ShouldBe(AuditActors.System);
        AuditActors.Classify(AuditSource.Learning, "cli:xd", null).Kind.ShouldBe(AuditActors.Cli);
    }
}
