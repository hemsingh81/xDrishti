using Microsoft.EntityFrameworkCore;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Market;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Portfolio;

/// <summary>The names of the NSE shares and ETFs of the instrument master (ADR 0028), for matching the scrip names of a dividend file: one projected, not tracked read of a few thousand rows.</summary>
internal sealed class InstrumentNames(XDrishtiDbContext db) : IInstrumentNames
{
    private const string Nse = "NSE";

    private const string ExchangePrefix = "NSE:";

    public async ValueTask<IReadOnlyList<ShareName>> ListSharesAsync(CancellationToken cancellationToken)
    {
        var rows = await db.Instruments.AsNoTracking()
            .Where(i => i.Exchange == Nse && (i.Type == InstrumentTypeCodes.Share || i.Type == InstrumentTypeCodes.Etf))
            .Select(i => new { i.Id, i.CanonicalSymbol, i.Isin, i.Name, i.DisplayName, Retired = i.RetiredAt != null })
            .ToListAsync(cancellationToken);
        return [.. rows.Select(r => new ShareName(r.Id, Ticker(r.CanonicalSymbol), r.Isin, r.Name, r.DisplayName, r.Retired))];
    }

    private static string Ticker(string canonicalSymbol) =>
        canonicalSymbol.StartsWith(ExchangePrefix, StringComparison.Ordinal) ? canonicalSymbol[ExchangePrefix.Length..] : canonicalSymbol;
}
