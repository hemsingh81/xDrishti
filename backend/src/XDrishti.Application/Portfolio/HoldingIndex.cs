using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio;

/// <summary>Finds the holding of an account that a broker row belongs to: by ISIN first, then by security id and exchange.</summary>
internal sealed class HoldingIndex
{
    private readonly Dictionary<string, Holding> _byIsin = new(StringComparer.Ordinal);

    private readonly Dictionary<string, Holding> _bySecurity = new(StringComparer.Ordinal);

    public HoldingIndex(IEnumerable<Holding> holdings)
    {
        foreach (var holding in holdings)
        {
            if (holding.Isin is { } isin)
            {
                _byIsin.TryAdd(isin, holding);
            }

            _bySecurity.TryAdd(SecurityKey(holding.SecurityId, holding.Exchange), holding);
        }
    }

    public Holding? Find(BrokerHolding row)
    {
        if (!string.IsNullOrWhiteSpace(row.Isin) && _byIsin.TryGetValue(row.Isin.Trim().ToUpperInvariant(), out var byIsin))
        {
            return byIsin;
        }

        return _bySecurity.GetValueOrDefault(SecurityKey(row.SecurityId, row.Exchange));
    }

    private static string SecurityKey(string securityId, string exchange) => securityId.Trim() + ":" + exchange.Trim().ToUpperInvariant();
}
