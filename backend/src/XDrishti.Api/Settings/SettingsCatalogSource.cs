using Microsoft.Extensions.Options;
using XDrishti.Api.Confidence;
using XDrishti.Application.Abstractions.SettingsCatalog;
using XDrishti.Application.SettingsCatalog;

namespace XDrishti.Api.Settings;

/// <summary>
/// The catalog of this host (AU-08): the Confidence keys with their configured value and whether it was set in configuration, plus the descriptors of the Application registry. Only Confidence keys show a value, and
/// none of them is a secret; stored switches and whole sections show no value here.
/// </summary>
internal sealed class SettingsCatalogSource(IOptions<ConfidenceSettings> confidence, IConfiguration configuration) : ISettingsCatalogSource
{
    public IReadOnlyList<SettingsCatalogEntry> Read()
    {
        var settings = confidence.Value;
        var entries = new List<SettingsCatalogEntry>();
        foreach (var d in ConfidenceSettingDescriptors.All)
        {
            var path = d.Key[(ConfidenceSettings.SectionName.Length + 1)..];
            var set = configuration.GetSection(d.Key).Value is not null;
            entries.Add(new SettingsCatalogEntry(d, ConfidenceSettingDescriptors.ValueOf(settings, path), set ? "configuration" : "default"));
        }

        foreach (var d in SettingsRegistry.All)
        {
            if (d.Kind == SettingKind.Configuration)
            {
                var raw = configuration[d.Key];
                entries.Add(new SettingsCatalogEntry(d, raw ?? d.Default, raw is null ? "default" : "configuration"));
            }
            else
            {
                entries.Add(new SettingsCatalogEntry(d, null, d.Kind == SettingKind.Stored ? "stored" : "section"));
            }
        }

        return entries;
    }
}
