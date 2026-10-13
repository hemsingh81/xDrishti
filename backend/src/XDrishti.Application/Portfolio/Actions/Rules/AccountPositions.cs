using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.Groups;

namespace XDrishti.Application.Portfolio.Actions.Rules;

/// <summary>The priced positions of one account (open holdings with a usable price and an open lot) and their value: what the weights of the overweight and concentration rules are shares of.</summary>
internal sealed record AccountPositions(Guid AccountId, IReadOnlyList<HoldingData> Holdings, decimal Value)
{
    public static IReadOnlyList<AccountPositions> Of(IEnumerable<HoldingData> holdings)
    {
        ArgumentNullException.ThrowIfNull(holdings);
        return
        [
            .. holdings
                .Where(h => h.IsOpen && h.IsPriced && h.Lots.Any(l => l.IsOpen))
                .GroupBy(h => h.AccountId)
                .Select(g =>
                {
                    List<HoldingData> list = [.. g];
                    return new AccountPositions(g.Key, list, GroupCalculator.ValueOf(list) ?? 0m);
                }),
        ];
    }
}
