using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Filings;
using XDrishti.Domain.Filings;

namespace XDrishti.Infrastructure.FilingArchive;

/// <summary>
/// <see cref="IPointInTimeFilings"/> (P15-02, ADR 0180): the one place a filing is read for a question about an instant. The query only narrows the rows by the instrument, the kind and the cutoff <c>t - lag</c> on <c>broadcast_ts</c> (the index
/// <c>(instrument_id, kind, broadcast_ts)</c>), plus the filings with no time, which are counted; <see cref="PointInTimeRule.AsOf"/> decides. No other column ever decides visibility.
/// </summary>
internal sealed class PointInTimeFilings(NpgsqlDataSource dataSource, IFilingSettings settings) : IPointInTimeFilings
{
    public async ValueTask<AsOfResult> AsOf(Guid instrumentId, FilingKind kind, DateTimeOffset t, CancellationToken cancellationToken)
    {
        var lag = TimeSpan.FromMinutes(settings.LagMinutes);
        await using var command = dataSource.CreateCommand(
            $"SELECT {FilingRows.Columns} FROM filings.filings WHERE instrument_id = @i AND kind = @k AND (broadcast_ts <= @cutoff OR broadcast_ts IS NULL)");
        command.Parameters.AddWithValue("i", instrumentId);
        command.Parameters.AddWithValue("k", FilingKinds.Code(kind));
        command.Parameters.Add(new NpgsqlParameter("cutoff", NpgsqlDbType.TimestampTz) { Value = (t - lag).UtcDateTime });
        var rows = new List<StoredFiling>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(FilingRows.Read(reader));
        }

        return PointInTimeRule.AsOf(rows, t, lag);
    }
}
