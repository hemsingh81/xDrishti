using XDrishti.Domain.Accounts;

namespace XDrishti.Domain.Tests.Accounts;

/// <summary>Synthetic accounts for the tests (made-up names and client ids).</summary>
internal static class AccountData
{
    public static readonly DateTimeOffset T0 = new(2026, 10, 10, 9, 0, 0, TimeSpan.Zero);

    /// <summary>The owner's calendar day at <see cref="T0"/> (India).</summary>
    public static readonly DateOnly Today = new(2026, 10, 10);

    public static readonly Guid ProfileId = Guid.Parse("00000000-0000-7000-8000-0000000000aa");

    public static BrokerAccount New(string name = "Example One", string clientId = "1000000001") =>
        BrokerAccount.Create(name, clientId, null, null, null, Today, T0).Value;

    public static TradingSpec CompleteTrading() => new(
        TradingMode.Paper,
        ProfileId,
        CapitalSource.Fixed,
        500_000m,
        Intraday: true,
        Swing: false,
        Instruments.EquityIntraday | Instruments.Futures,
        Instruments.None,
        Instruments.None,
        TradingOverrides.None);

    public static RiskLimits StandardLimits() => new(0.5m, 25m, 3, 5, 3.0m, 2, 0.70m, 2, 2, 2m, 1.5m, 5m, WeeklyLossAction.HalveSize);

    public static BrokerAccount WithPrimaryData(string name = "Example Primary")
    {
        var account = New(name, "2000000002");
        account.ChangeRoles(new RolesChange(new DataRoleChange(true, DataRoleKind.Primary), null, null), null, null, T0).IsSuccess.ShouldBeTrue();
        return account;
    }

    public static RolesChange Data(DataRoleKind kind) => new(new DataRoleChange(true, kind), null, null);

    public static RolesChange DataOff() => new(new DataRoleChange(false, null), null, null);
}
