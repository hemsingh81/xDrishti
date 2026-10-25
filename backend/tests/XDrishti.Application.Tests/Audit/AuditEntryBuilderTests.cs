using XDrishti.Application.Abstractions.Audit;
using XDrishti.Application.Audit;
using XDrishti.Domain.Audit;

namespace XDrishti.Application.Tests.Audit;

public sealed class AuditEntryBuilderTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 12, 6, 30, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(AuditSource.Account, "BrokerConnectFailed", "Dhan refused: client_id=1100000001 token: abc123")]
    [InlineData(AuditSource.Approval, "approval.approve", "looks fine, Authorization: Bearer abc123xyz")]
    [InlineData(AuditSource.Plan, "plan.ticket_moved", "proposed to approved: secret x7")]
    [InlineData(AuditSource.Learning, "learning.transition", "research to candidate - password is hunter2")]
    public void Every_free_text_reason_is_redacted(AuditSource source, string kind, string reason)
    {
        var id = AuditSources.IsUuid(source) ? Guid.NewGuid().ToString("D") : "1";
        var view = AuditEntryBuilder.Build(new AuditRaw(source, id, At, kind, "owner:o@example.com", null, reason, null, null, []), "o@example.com");

        view.Redacted.ShouldBeTrue();
        view.Summary.ShouldNotContain("1100000001");
        view.Summary.ShouldNotContain("abc123");
        view.Summary.ShouldNotContain("x7");
        view.Summary.ShouldNotContain("hunter2");
    }

    [Fact]
    public void Detail_fields_are_redacted_too()
    {
        var raw = new AuditRaw(AuditSource.Approval, "1", At, "approval.approve", "owner:o@example.com", null, "ok", null, null, [new AuditField("Confirm reason", "checked, token: abc123"), new AuditField("Cutoff", "2026-10-12 09:00 IST")]);

        var fields = AuditEntryBuilder.Fields(raw);

        fields[0].Value.ShouldNotContain("abc123");
        fields[1].Value.ShouldBe("2026-10-12 09:00 IST");
    }
}
