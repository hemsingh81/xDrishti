using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.SettingsCatalog;

namespace XDrishti.Application.SettingsCatalog;

/// <summary>The catalog of settings: key, label, tier, value, default, range, safer direction, source (AU-08).</summary>
public sealed record GetSettingsCatalogQuery : IQuery<SettingsCatalogResponse>;

public sealed record SettingsCatalogResponse(IReadOnlyList<SettingsCatalogItem> Items);

/// <summary><c>Tier</c> is Essential, Advanced or Internal; <c>Safer</c> is higher, lower or none (none loosens both ways); <c>Source</c> is default, configuration, stored or section.</summary>
public sealed record SettingsCatalogItem(string Key, string Label, string Tier, string? Value, string? Default, SettingRange? Range, string Safer, bool Learnable, string Source);

public sealed record SettingRange(decimal? Min, decimal? Max);

internal sealed class GetSettingsCatalogHandler(ISettingsCatalogSource source) : IQueryHandler<GetSettingsCatalogQuery, SettingsCatalogResponse>
{
    public ValueTask<SettingsCatalogResponse> HandleAsync(GetSettingsCatalogQuery query, CancellationToken cancellationToken)
    {
        var items = source.Read()
            .OrderBy(e => e.Descriptor.Tier)
            .ThenBy(e => e.Descriptor.Key, StringComparer.Ordinal)
            .Select(e => new SettingsCatalogItem(
                e.Descriptor.Key,
                e.Descriptor.Label,
                e.Descriptor.Tier.ToString(),
                e.Value,
                e.Descriptor.Default,
                e.Descriptor.HasRange ? new SettingRange(e.Descriptor.Min, e.Descriptor.Max) : null,
                e.Descriptor.Safer.ToString().ToLowerInvariant(),
                e.Descriptor.Learnable,
                e.Source))
            .ToList();
        return ValueTask.FromResult(new SettingsCatalogResponse(items));
    }
}
