using Npgsql;
using XDrishti.Domain.Filings;

namespace XDrishti.Infrastructure.FilingArchive;

/// <summary>The columns of <c>filings.filings</c> and their reading, shared by the store, the point-in-time read and the metadata reader.</summary>
internal static class FilingRows
{
    public const string Columns = "id, instrument_id, kind, period_end, broadcast_ts, broadcast_state, unavailable_reason, ingested_at, content_sha256, file_bytes, file_name, supersedes";

    public static string Qualified(string alias) => string.Join(", ", Columns.Split(", ").Select(c => alias + "." + c));

    public static StoredFiling Read(NpgsqlDataReader reader)
    {
        if (!FilingKinds.TryParse(reader.GetString(2), out var kind))
        {
            throw new InvalidDataException("Unknown filing kind in the store.");
        }

        return new StoredFiling(
            reader.GetGuid(0),
            reader.GetGuid(1),
            kind,
            reader.IsDBNull(3) ? null : reader.GetFieldValue<DateOnly>(3),
            reader.IsDBNull(4) ? null : new DateTimeOffset(reader.GetDateTime(4), TimeSpan.Zero),
            reader.GetString(5) == "ok" ? BroadcastState.Ok : BroadcastState.Unavailable,
            reader.IsDBNull(6) ? null : reader.GetString(6),
            new DateTimeOffset(reader.GetDateTime(7), TimeSpan.Zero),
            reader.GetString(8),
            reader.GetInt64(9),
            reader.GetString(10),
            reader.IsDBNull(11) ? null : reader.GetGuid(11));
    }
}
