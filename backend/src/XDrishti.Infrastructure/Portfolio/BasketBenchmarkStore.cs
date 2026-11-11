using Microsoft.EntityFrameworkCore;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Abstractions.Reporting;
using XDrishti.Domain.Market;
using XDrishti.Domain.Platform;
using XDrishti.Domain.Reporting;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Portfolio;

/// <summary>The basket benchmark setting (BK-06c): one <see cref="AppSetting"/> row holds the chosen index id; without one the master-list Nifty 500 applies. The lookup of an instrument is the portfolio store's; the portfolio benchmark is not read here.</summary>
internal sealed class BasketBenchmarkStore(XDrishtiDbContext db, IPortfolioBenchmark master) : IBasketBenchmark
{
    public async ValueTask<BasketBenchmark> GetAsync(CancellationToken cancellationToken)
    {
        var stored = await db.AppSettings.AsNoTracking().Where(x => x.Id == AppSettingKeys.BasketBenchmarkInstrument).Select(x => x.Value).FirstOrDefaultAsync(cancellationToken);
        if (stored is not null)
        {
            var chosen = Guid.TryParse(stored, out var id) ? await master.FindAsync(id, cancellationToken) : null;
            return chosen is { Retired: false } && chosen.Type == InstrumentTypeCodes.Index
                ? new BasketBenchmark(chosen, false, chosen.Name, null)
                : new BasketBenchmark(null, false, "The chosen index", "The chosen index is not in the instrument master");
        }

        var preferred = BasketBenchmarkDefault.CanonicalSymbol;
        var displayName = LikePattern.Escape(BasketBenchmarkDefault.DisplayName);
        var found = await db.Instruments.AsNoTracking()
            .Where(i => i.Type == InstrumentTypeCodes.Index && i.RetiredAt == null && (i.CanonicalSymbol == preferred || EF.Functions.ILike(i.DisplayName, displayName, LikePattern.EscapeCharacter)))
            .OrderBy(i => i.CanonicalSymbol == preferred ? 0 : 1).ThenBy(i => i.CanonicalSymbol)
            .Select(i => i.Id)
            .FirstOrDefaultAsync(cancellationToken);
        return found != Guid.Empty && await master.FindAsync(found, cancellationToken) is { } index
            ? new BasketBenchmark(index, true, BasketBenchmarkDefault.DisplayName, null)
            : new BasketBenchmark(null, true, BasketBenchmarkDefault.DisplayName, $"{BasketBenchmarkDefault.DisplayName} is not in the instrument master");
    }
}
