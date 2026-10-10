using XDrishti.Application.Auth;
using XDrishti.Domain.Auth;

namespace XDrishti.Application.Tests.Auth;

public sealed class GetAuditHandlerTests
{
    private readonly AuthFixture _fx = new();

    private void Seed(int count)
    {
        for (var i = 0; i < count; i++)
        {
            _fx.Data.Record(AuditEvent.Record(AuditEventType.LoginFailed, AuthFixture.Start.AddMinutes(i), $"e{i}@example.invalid", "203.0.113.7", "agent"));
        }
    }

    [Fact]
    public async Task Returns_newest_first_with_the_event_name()
    {
        Seed(3);

        var list = await _fx.Audit().HandleAsync(new GetAuditQuery(), CancellationToken.None);

        list.Select(e => e.Email).ShouldBe(["e2@example.invalid", "e1@example.invalid", "e0@example.invalid"]);
        list[0].Event.ShouldBe("LoginFailed");
        list[0].Ip.ShouldBe("203.0.113.7");
        list[0].UserAgent.ShouldBe("agent");
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(10, 10)]
    [InlineData(100000, 200)]
    public async Task Take_is_clamped(int take, int expected)
    {
        Seed(250);

        var list = await _fx.Audit().HandleAsync(new GetAuditQuery(take), CancellationToken.None);

        list.Count.ShouldBe(expected);
    }
}
