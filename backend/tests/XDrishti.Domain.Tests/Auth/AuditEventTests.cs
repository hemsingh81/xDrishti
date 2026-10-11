using XDrishti.Domain.Auth;

namespace XDrishti.Domain.Tests.Auth;

public sealed class AuditEventTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 10, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Record_keeps_the_facts()
    {
        var entry = AuditEvent.Record(AuditEventType.LoginFailed, T0, "a@example.invalid", "203.0.113.7", "agent");

        entry.At.ShouldBe(T0);
        entry.Event.ShouldBe(AuditEventType.LoginFailed);
        entry.Email.ShouldBe("a@example.invalid");
        entry.Ip.ShouldBe("203.0.113.7");
        entry.UserAgent.ShouldBe("agent");
    }

    [Fact]
    public void Record_cuts_untrusted_text_to_the_column_size()
    {
        var entry = AuditEvent.Record(AuditEventType.LoginFailed, T0, new string('e', 1000), null, new string('u', 1000));

        entry.Email!.Length.ShouldBe(AuditEvent.MaxEmailLength);
        entry.UserAgent!.Length.ShouldBe(AuditEvent.MaxUserAgentLength);
    }

    [Fact]
    public void Record_stores_blank_values_as_null()
    {
        var entry = AuditEvent.Record(AuditEventType.Logout, T0, " ", "", null);

        entry.Email.ShouldBeNull();
        entry.Ip.ShouldBeNull();
        entry.UserAgent.ShouldBeNull();
    }
}
