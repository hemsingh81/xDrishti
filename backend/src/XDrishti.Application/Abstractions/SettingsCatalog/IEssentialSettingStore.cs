namespace XDrishti.Application.Abstractions.SettingsCatalog;

/// <summary>A stored value of an Essential setting with who changed it and when.</summary>
public sealed record StoredEssential(string Value, string UpdatedBy, DateTimeOffset UpdatedAt);

/// <summary>The Essential settings that are one short text in <c>platform.app_settings</c> (<c>auto.enabled</c>, <c>suggest.scope</c>, <c>notices.failure</c>): read, and set by the owner. A key with no row has its default.</summary>
public interface IEssentialSettingStore
{
    ValueTask<StoredEssential?> GetAsync(string key, CancellationToken cancellationToken);

    ValueTask SetAsync(string key, string value, string actor, DateTimeOffset now, CancellationToken cancellationToken);
}
