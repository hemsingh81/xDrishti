using Microsoft.EntityFrameworkCore;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Market.Calendar;

internal sealed class TradingDayStore(XDrishtiDbContext db) : ITradingDayStore
{
    public async ValueTask<IReadOnlyList<TradingDay>> ListAsync(CancellationToken cancellationToken) =>
        await db.TradingDays.OrderBy(x => x.Id).ToListAsync(cancellationToken);

    public async ValueTask<TradingDay?> FindAsync(DateOnly date, CancellationToken cancellationToken) =>
        await db.TradingDays.FirstOrDefaultAsync(x => x.Id == date, cancellationToken);

    public async ValueTask<int> CountProposedAsync(CancellationToken cancellationToken) =>
        await db.TradingDays.AsNoTracking().CountAsync(x => x.Status == CalendarStatus.Proposed || x.ProposalStatus == CalendarStatus.Proposed, cancellationToken);

    public void Add(TradingDay day) => db.TradingDays.Add(day);

    public void Remove(TradingDay day) => db.TradingDays.Remove(day);
}
