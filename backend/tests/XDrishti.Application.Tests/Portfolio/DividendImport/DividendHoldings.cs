using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Tests.Portfolio.DividendImport;

/// <summary>Read models of holdings, master shares and aliases for the matcher and planner tests (made-up companies).</summary>
internal static class DividendHoldings
{
    public static HoldingData Held(string name, string symbol, string? isin = null, bool open = true, Guid? instrument = null, DateOnly? acquired = null, Guid? account = null)
    {
        var lot = HoldingFixtures.Open(10, 100m, acquired ?? new DateOnly(2025, 1, 1));
        var holding = HoldingFixtures.Holding(120m, symbol, open ? "open" : "closed", instrument, lot);
        return holding with { Name = name, Isin = isin, AccountId = account ?? holding.AccountId };
    }

    public static ShareName Share(string name, string symbol, string? isin = null, bool retired = false, string? displayName = null) =>
        new(Guid.CreateVersion7(), symbol, isin, name, displayName ?? name, retired);

    public static DividendAlias AliasToHolding(string name, HoldingData holding) =>
        DividendAlias.Create(holding.AccountId, XDrishti.Application.Portfolio.DividendImport.DividendNames.Key(name), holding.Id, null, "owner", DateTimeOffset.UnixEpoch);

    public static DividendAlias AliasToShare(string name, ShareName share, Guid account) =>
        DividendAlias.Create(account, XDrishti.Application.Portfolio.DividendImport.DividendNames.Key(name), null, share.Id, "owner", DateTimeOffset.UnixEpoch);

    public static DividendAlias AliasToNothing(string name, Guid account) =>
        DividendAlias.Create(account, XDrishti.Application.Portfolio.DividendImport.DividendNames.Key(name), null, null, "owner", DateTimeOffset.UnixEpoch);
}
