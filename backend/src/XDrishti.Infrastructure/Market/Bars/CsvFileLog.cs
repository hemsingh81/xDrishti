using Microsoft.EntityFrameworkCore;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Market.Bars;

internal sealed class CsvFileLog(XDrishtiDbContext db) : ICsvFileLog
{
    public async ValueTask<IReadOnlyDictionary<string, LoadedCsvFile>> ListAsync(CancellationToken cancellationToken) =>
        await db.CsvFiles.AsNoTracking()
            .Select(x => new LoadedCsvFile(x.FileName, x.SizeBytes, x.ModifiedAt, x.InstrumentId, x.Rows, x.Inserted, x.Updated, x.Repaired, x.Rejected, x.LoadedAt))
            .ToDictionaryAsync(x => x.Name, StringComparer.Ordinal, cancellationToken);
}
