using XDrishti.Domain.Accounts;

namespace XDrishti.Domain.Tests.Accounts;

public sealed class PendingConnectTests
{
    private static readonly DateTimeOffset T0 = AccountData.T0;

    private static readonly Guid AccountId = Guid.Parse("00000000-0000-7000-8000-0000000000c1");

    private static readonly Guid Session = Guid.Parse("00000000-0000-7000-8000-0000000000d1");

    [Fact]
    public void A_sign_in_is_open_for_ten_minutes_for_its_own_session()
    {
        var pending = PendingConnect.Start(AccountId, Session, T0);

        pending.ExpiresAt.ShouldBe(T0.AddMinutes(10));
        pending.Check(Session, T0.AddMinutes(9)).ShouldBe(PendingConnectCheck.Open);
        pending.Check(Session, T0.AddMinutes(10)).ShouldBe(PendingConnectCheck.Expired);
        pending.Check(Guid.NewGuid(), T0).ShouldBe(PendingConnectCheck.OtherSession);
    }

    [Fact]
    public void A_sign_in_can_be_used_once()
    {
        var pending = PendingConnect.Start(AccountId, Session, T0);

        pending.Consume(T0.AddMinutes(1)).ShouldBeTrue();
        pending.Consume(T0.AddMinutes(2)).ShouldBeFalse();

        pending.ConsumedAt.ShouldBe(T0.AddMinutes(1));
        pending.Check(Session, T0.AddMinutes(2)).ShouldBe(PendingConnectCheck.Used);
    }
}
