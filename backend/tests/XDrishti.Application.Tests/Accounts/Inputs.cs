using XDrishti.Application.Accounts;
using XDrishti.Application.Accounts.RiskProfiles;
using XDrishti.Domain.Accounts;

namespace XDrishti.Application.Tests.Accounts;

/// <summary>Request builders with valid, made-up defaults; tests override only what they care about.</summary>
internal static class Inputs
{
    public static CreateAccountInput Account(
        string name = "Example One",
        string? clientId = AccountsFixture.ClientId,
        RolesInput? roles = null,
        TradingInput? trading = null,
        PortfolioInput? portfolio = null,
        DataInput? data = null,
        SecretsInput? secrets = null,
        DateOnly? apiKeyIssuedOn = null) =>
        new(name, clientId, secrets ?? Secrets(), apiKeyIssuedOn, null, null, roles, trading, portfolio, data);

    /// <summary>Made-up API key and secret that carry the leak marker, so tests can prove they appear nowhere.</summary>
    public static SecretsInput Secrets(string? apiKey = AccountsFixture.ApiKey, string? apiSecret = AccountsFixture.ApiSecret) => new(apiKey, apiSecret);

    public static RolesInput DataRole(string kind) => new(new DataRoleInput(true, kind), null, null);

    public static RolesInput Roles(bool trading, bool portfolio, DataRoleInput? data = null) =>
        new(data, new RoleToggleInput(trading), new RoleToggleInput(portfolio));

    public static TradingInput Trading(Guid? profileId, string mode = "paper", decimal? amount = 500_000m) => new(
        mode, profileId, "fixed", amount, [AccountCodes.Intraday], new InstrumentsInput(["EQ_MIS", "FUT"], null, null), null);

    public static PortfolioInput Portfolio(string? bucket = "Trading", DateOnly? from = null) => new(bucket, from, true);

    public static ConnectionInput Connection(string name = "Example One", string? clientId = null) =>
        new(name, clientId, null, null);
}

/// <summary>Valid risk profile values (the documented "Standard" profile).</summary>
internal static class AccountDataTestValues
{
    public static readonly RiskProfileInput Standard = new(
        "Mine", 0.5m, 25m, 3, 5, 3.0m, 2, 0.70m, 2, 2, 2m, 1.5m, 5m, "halve_size");
}

internal static class RiskProfileMappingForTests
{
    public static RiskLimits Limits(RiskProfileInput input) => new(
        input.RiskPerTradePct!.Value, input.MaxCapitalPerTradePct!.Value, input.MaxNewEntriesPerDay!.Value, input.MaxOpenPositions!.Value,
        input.MaxTotalRiskPct!.Value, input.MaxPerSector!.Value, input.MaxPairCorrelation!.Value, input.MaxIntraday!.Value, input.MaxSwing!.Value,
        input.DailyLossR!.Value, input.DailyLossPct!.Value, input.WeeklyLossR!.Value, WeeklyLossAction.HalveSize);
}
