using Microsoft.Extensions.Options;
using XDrishti.Api.Confidence;
using XDrishti.Application.Abstractions.LearnedSettings;
using XDrishti.Application.SettingsCatalog;

namespace XDrishti.Api.Settings;

/// <summary>
/// The Confidence settings as learned changes see them (AU-10): the descriptors, the configured value (bound fresh, before any learned change, so a change is always judged against the owner's own configuration), and the check of the WHOLE candidate configuration with every
/// existing validator (the pairs that limit each other included) and the descriptor bounds.
/// </summary>
internal sealed class LearnedConfig(IConfiguration configuration, IEnumerable<IValidateOptions<ConfidenceSettings>> validators) : ILearnedConfig
{
    public SettingDescriptor? Descriptor(string key) => ConfidenceSettingDescriptors.All.FirstOrDefault(d => d.Key == key);

    public string? ConfiguredValue(string key) => ConfidenceOverlay.ValueOf(Configured(), key);

    public IReadOnlyList<string> Failures(IReadOnlyDictionary<string, string> overrides)
    {
        ArgumentNullException.ThrowIfNull(overrides);
        var settings = Configured();
        var failures = new List<string>();
        foreach (var (key, value) in overrides)
        {
            if (!ConfidenceOverlay.TrySet(settings, key, value))
            {
                failures.Add($"{key} = {value} cannot be set.");
            }
        }

        failures.AddRange(ConfidenceOverlay.Failures(validators, settings));
        return failures;
    }

    private ConfidenceSettings Configured()
    {
        var settings = new ConfidenceSettings();
        configuration.GetSection(ConfidenceSettings.SectionName).Bind(settings);
        return settings;
    }
}
