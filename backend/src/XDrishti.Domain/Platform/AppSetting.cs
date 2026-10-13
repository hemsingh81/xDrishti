using XDrishti.Domain.Common;

namespace XDrishti.Domain.Platform;

/// <summary>
/// One owner setting kept in the database (ADR 0002: configuration lives in the app, not in files): a key, a short text value and who changed it when. The meaning and the checks of a
/// value belong to the module that owns the key (see <see cref="AppSettingKeys"/>); this is only the store. Rows are kept for good (a change overwrites the value).
/// </summary>
public sealed class AppSetting : Entity<string>
{
    public const int MaxKeyLength = 100;

    public const int MaxValueLength = 500;

    public const int MaxUpdatedByLength = 256;

    // Parameter names match property names so EF Core can bind this constructor when materialising.
    private AppSetting(string id, string value, string updatedBy, DateTimeOffset updatedAt)
        : base(id)
    {
        Value = value;
        UpdatedBy = updatedBy;
        UpdatedAt = updatedAt;
    }

    public string Value { get; private set; }

    /// <summary>Who changed it last (the signed-in owner's e-mail address).</summary>
    public string UpdatedBy { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static AppSetting Create(string key, string value, string updatedBy, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (key.Length > MaxKeyLength || value.Length > MaxValueLength)
        {
            throw new ArgumentException("The key or the value is too long.");
        }

        return new AppSetting(key, value, Limit(updatedBy), now);
    }

    public void Change(string value, string updatedBy, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length > MaxValueLength)
        {
            throw new ArgumentException("The value is too long.", nameof(value));
        }

        Value = value;
        UpdatedBy = Limit(updatedBy);
        UpdatedAt = now;
    }

    private static string Limit(string? actor)
    {
        var trimmed = actor?.Trim();
        return string.IsNullOrEmpty(trimmed) ? "unknown" : trimmed.Length <= MaxUpdatedByLength ? trimmed : trimmed[..MaxUpdatedByLength];
    }
}
