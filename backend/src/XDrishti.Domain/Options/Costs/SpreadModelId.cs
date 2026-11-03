using System.Globalization;
using XDrishti.Domain.Research;

namespace XDrishti.Domain.Options.Costs;

/// <summary>The content address of a half-spread model (spec P14-07 criterion 8): the SHA-256 of its inputs, the underlying, the sessions it uses and the two settings that shape it. The same inputs give the same id, so a rebuild writes nothing.</summary>
public static class SpreadModelId
{
    /// <summary>The version of the algorithm that turns sessions into buckets; a later fix raises it, so the same sessions then hash to a new id and are rebuilt.</summary>
    public const int ModelVersion = 1;

    public static string Of(Guid underlyingId, IReadOnlyList<DateOnly> sessions, int minUsableSessions, int minQuotesPerBucket)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        var text = "model_version=" + ModelVersion.ToString(CultureInfo.InvariantCulture)
            + "\nunderlying=" + underlyingId.ToString("D", CultureInfo.InvariantCulture)
            + "\nmin_usable_sessions=" + minUsableSessions.ToString(CultureInfo.InvariantCulture)
            + "\nmin_quotes_per_bucket=" + minQuotesPerBucket.ToString(CultureInfo.InvariantCulture)
            + "\nsessions=" + string.Join(',', sessions.Order().Select(s => s.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        return CanonicalText.Sha256Hex(text);
    }

    /// <summary>The settings as the JSON object the model row keeps.</summary>
    public static string SettingsJson(int minUsableSessions, int minQuotesPerBucket) =>
        "{\"model_version\":" + ModelVersion.ToString(CultureInfo.InvariantCulture) + ",\"min_usable_sessions\":" + minUsableSessions.ToString(CultureInfo.InvariantCulture) + ",\"min_quotes_per_bucket\":" + minQuotesPerBucket.ToString(CultureInfo.InvariantCulture) + "}";
}
