using Microsoft.EntityFrameworkCore;
using XDrishti.Application.Abstractions.Live;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Platform;

/// <summary>The feed's <c>NOTIFY</c> signals (ADR 0024 §6): the channel names are fixed, there is no payload, and nothing here depends on anybody listening.</summary>
internal sealed class LiveNotifier(XDrishtiDbContext db) : ILiveNotifier
{
    public async ValueTask PricesChangedAsync(CancellationToken cancellationToken) =>
        await db.Database.ExecuteSqlRawAsync("NOTIFY live_prices", cancellationToken);

    public async ValueTask ActiveSetChangedAsync(CancellationToken cancellationToken) =>
        await db.Database.ExecuteSqlRawAsync("NOTIFY active_set_changed", cancellationToken);
}
