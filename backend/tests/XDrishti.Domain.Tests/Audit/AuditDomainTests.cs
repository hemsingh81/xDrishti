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
    [InlineData("password=hunter2", "hunter2")]
    [InlineData("Password: hunter2 afterwards", "hunter2")]
    [InlineData("token: abc123", "abc123")]
    [InlineData("client_id=1100000001", "1100000001")]
    [InlineData("Bearer eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.abcdefghijk", "eyJ")]
    [InlineData("eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.abcdefghijk", "eyJ")]
    [InlineData("0123456789abcdef0123456789abcdef01234567", "0123456789abcdef")]
    [InlineData("AbCdEfGhIjKlMnOpQrStUvWxYz0123456789AbCdEfGhIjKlMnOpQrStUvWxYz01234567", "AbCdEfGhIj")]
    [InlineData("Authorization: Bearer abc123xyz", "abc123xyz")]
    [InlineData("Authorization: Bearer abc123xyz", "Bearer")]
    [InlineData("Authorization: Basic dXNlcjpwdw==", "dXNlcjpwdw")]
    [InlineData("Authorization: Basic dXNlcjpwdw==", "Basic")]
    [InlineData("{\"password\":\"hunter2\"}", "hunter2")]
    [InlineData("{'token': 'abc123'}", "abc123")]
    [InlineData("the password is hunter2 ok", "hunter2")]
    [InlineData("token abc123", "abc123")]
    [InlineData("secret x7", "x7")]
    [InlineData("my_token=abc123", "abc123")]
    [InlineData("session_id=abc123", "abc123")]
    [InlineData("sessionid=abc123", "abc123")]
    [InlineData("sid=abc123", "abc123")]
    [InlineData("session=abc123", "abc123")]
    [InlineData("passphrase=correct-horse", "correct-horse")]
    [InlineData("pw=hunter2", "hunter2")]
    [InlineData("private key: MIIBVgIBADANBgkq", "MIIBVgIBADANBgkq")]
    [InlineData("recovery code: zz9", "zz9")]
    [InlineData("refresh=abc123", "abc123")]
    [InlineData("cookie: abc123", "abc123")]
    [InlineData("credential=abc123", "abc123")]
    [InlineData("api key: abc123", "abc123")]
    [InlineData("secret: abc123", "abc123")]
    [InlineData("postgres://owner:hunter2@db.internal/xdrishti", "hunter2")]
    [InlineData("Server=db.internal;Username=owner;Password=hunter2;Database=x", "hunter2")]
    [InlineData("Server=db.internal;Username=owner;Password=hunter2;Database=x", "db.internal")]
    [InlineData("Server=db.internal;Username=owner;Password=hunter2;Database=x", "owner")]
    [InlineData("hash $argon2id$v=19$m=65536,t=3,p=4$c29tZXNhbHQ$RdescudvJCsgt3ub", "c29tZXNhbHQ")]
    [InlineData("hash $2a$11$abcdefghijklmnopqrstuuABCDEFGHIJKLMNOPQRSTUVWXYZ01234", "abcdefghijklmnopqrstuu")]
    [InlineData("blob QUJDREVGR0hJSktMTU5PUFFSU1RVVldYWVowMTIzNDU2Nzg5YWJjZGVm", "QUJDREVGR0hJSktMTU5P")]
    [InlineData("code 482913 entered", "482913")]
    [InlineData("recovery abcd-efgh-ijkl-mnop", "abcd-efgh")]
    public void A_secret_shape_is_masked(string secret, string mustNotSurvive)
    {
        var (text, redacted) = AuditRedactor.Redact("before " + secret + " after");

        redacted.ShouldBeTrue();
        text.ShouldContain(AuditRedactor.Mask);
        text.ShouldNotContain(mustNotSurvive);
        AuditRedactor.Redact(text).ShouldBe((text, false));
    }

    [Fact]
    public void A_pattern_that_runs_out_of_time_masks_the_whole_text()
    {
        var hostile = new string('a', 400_000) + "-" + new string('b', 400_000);

        var (text, redacted) = AuditRedactor.Redact(hostile);

        redacted.ShouldBeTrue();
        text.ShouldNotContain(new string('a', 100));
    }

    [Theory]
    [InlineData("5 holdings imported")]
    [InlineData("tokens are valid for 5 minutes")]
    [InlineData("Risk profile Balanced updated: risk 1% to 1.5%")]
    [InlineData("cell ORB|15m|BUY|intraday|FIXED_RR|* moved to paper")]
    [InlineData("bad password for the address")]
    [InlineData("password changed for the owner")]
    [InlineData("token expired after 30 minutes")]
    [InlineData("2026-10-12 10:30 IST, quantity 1250.5, price 1,234,567")]
    [InlineData("account 0b6f2a9e-7a52-4d7e-9c3a-5d0a7e0b7a40 renamed")]
    [InlineData("auto-approve off, reserve promotion off, min win 0.6, max 3 a day: switches checked")]
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
