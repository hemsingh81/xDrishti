using Microsoft.EntityFrameworkCore;
using XDrishti.Application.Abstractions.Pairs;
using XDrishti.Domain.Market;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Pairs;

/// <summary>The F&amp;O shares of the master list for the pairs study (P14-13): not retired, with today's sector and ASM/GSM flag. Read only; the labels are today's, the history is not dated (stated as limits).</summary>
internal sealed class PairUniverseSource(XDrishtiDbContext db) : IPairUniverseSource
{
    public async ValueTask<IReadOnlyList<PairInstrument>> ListAsync(CancellationToken cancellationToken)
    {
        var rows = await db.Instruments.AsNoTracking()
            .Where(x => x.FnoEligible && x.RetiredAt == null && x.Type == InstrumentTypeCodes.Share)
            .OrderBy(x => x.CanonicalSymbol)
            .Select(x => new { x.Id, x.CanonicalSymbol, x.Sector, x.TickSize, Flagged = x.AsmGsmFlag != null })
            .ToListAsync(cancellationToken);
        return [.. rows.Select(x => new PairInstrument(x.Id, x.CanonicalSymbol, x.Sector, x.TickSize, x.Flagged))];
    }
}
