using Microsoft.EntityFrameworkCore;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Platform;
using XDrishti.Domain.Portfolio;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Portfolio;

/// <summary>
/// The benchmark setting (ADR 0020 §2): one <see cref="AppSetting"/> row holds the id of the chosen master-list index; without one the master-list Nifty 50 applies. Reads are projections without tracking; a change is only collected in the scoped
/// context (the use case saves it together with its audit event).
/// </summary>
internal sealed class PortfolioBenchmarkStore(XDrishtiDbContext db) : IPortfolioBenchmark
{
    public async ValueTask<ConfiguredBenchmark?> GetAsync(CancellationToken cancellationToken)
    {
        var stored = await db.AppSettings.AsNoTracking().Where(x => x.Id == AppSettingKeys.PortfolioBenchmarkInstrument).Select(x => x.Value).FirstOrDefaultAsync(cancellationToken);
        if (stored is not null && Guid.TryParse(stored, out var id) && await FindAsync(id, cancellationToken) is { } chosen)
        {
            return new ConfiguredBenchmark(chosen, false);
        }

        var preferred = PortfolioBenchmark.DefaultCanonicalSymbol;
        var displayName = LikePattern.Escape(PortfolioBenchmark.DefaultDisplayName);
        var fallback = await db.Instruments.AsNoTracking()
            .Where(i => i.Type == "index" && i.RetiredAt == null && (i.CanonicalSymbol == preferred || EF.Functions.ILike(i.DisplayName, displayName, LikePattern.EscapeCharacter)))
            .OrderBy(i => i.CanonicalSymbol == preferred ? 0 : 1).ThenBy(i => i.CanonicalSymbol)
            .Select(i => i.Id)
            .FirstOrDefaultAsync(cancellationToken);
        return fallback != Guid.Empty && await FindAsync(fallback, cancellationToken) is { } found ? new ConfiguredBenchmark(found, true) : null;
    }

    public async ValueTask<BenchmarkInstrument?> FindAsync(Guid instrumentId, CancellationToken cancellationToken) =>
        await db.Instruments.AsNoTracking()
            .Where(i => i.Id == instrumentId)
            .Select(i => new BenchmarkInstrument(
                i.Id,
                i.CanonicalSymbol,
                i.DisplayName,
                i.Type,
                i.RetiredAt != null,
                db.TrackedInstruments.Any(t => t.Id == i.Id && t.Active),
                db.BarCoverage.Any(c => c.InstrumentId == i.Id)))
            .FirstOrDefaultAsync(cancellationToken);

    public async ValueTask SetAsync(Guid instrumentId, string actor, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var value = instrumentId.ToString("D");
        var row = await db.AppSettings.FirstOrDefaultAsync(x => x.Id == AppSettingKeys.PortfolioBenchmarkInstrument, cancellationToken);
        if (row is null)
        {
            db.AppSettings.Add(AppSetting.Create(AppSettingKeys.PortfolioBenchmarkInstrument, value, actor, now));
        }
        else
        {
            row.Change(value, actor, now);
        }
    }
}
