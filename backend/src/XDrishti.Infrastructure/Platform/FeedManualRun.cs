using System.Globalization;
using Microsoft.EntityFrameworkCore;
using XDrishti.Application.Abstractions.Live;
using XDrishti.Domain.Platform;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Platform;

/// <summary>
/// The "Run now" marker of the live feed: the <c>platform.app_settings</c> row <see cref="IFeedManualRun.Key"/> holds an ISO-8601 UTC instant. A missing row or a value that cannot be read means no marker. A write is only collected in the scoped
/// context (the use case saves it with its audit event).
/// </summary>
internal sealed class FeedManualRun(XDrishtiDbContext db) : IFeedManualRun
{
    public async ValueTask<DateTimeOffset?> GetManualUntilAsync(CancellationToken cancellationToken)
    {
        var value = await db.AppSettings.AsNoTracking().Where(x => x.Id == IFeedManualRun.Key).Select(x => x.Value).FirstOrDefaultAsync(cancellationToken);
        return value is not null && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var until) ? until : null;
    }

    public async ValueTask SetManualUntilAsync(DateTimeOffset until, string actor, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var value = until.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        var row = await db.AppSettings.FirstOrDefaultAsync(x => x.Id == IFeedManualRun.Key, cancellationToken);
        if (row is null)
        {
            db.AppSettings.Add(AppSetting.Create(IFeedManualRun.Key, value, actor, now));
        }
        else
        {
            row.Change(value, actor, now);
        }
    }

    public async ValueTask ClearAsync(string actor, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var row = await db.AppSettings.FirstOrDefaultAsync(x => x.Id == IFeedManualRun.Key, cancellationToken);
        row?.Change(now.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture), actor, now);
    }
}
