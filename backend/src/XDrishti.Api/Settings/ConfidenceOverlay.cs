using System.Globalization;
using Microsoft.Extensions.Options;
using XDrishti.Api.Confidence;

namespace XDrishti.Api.Settings;

/// <summary>Sets one numeric Confidence value by its key and runs every existing validator over a whole <see cref="ConfidenceSettings"/> (AU-10): the same checks the host makes at start, so a learned change is judged by the real rules and never by a copy of them.</summary>
internal static class ConfidenceOverlay
{
    /// <summary>The text of the value at <paramref name="key"/> (for example <c>Confidence:Pbo:MaxPbo</c>), or null for an unknown key.</summary>
    public static string? ValueOf(ConfidenceSettings settings, string key) =>
        key.StartsWith(ConfidenceSettings.SectionName + ":", StringComparison.Ordinal) ? ConfidenceSettingDescriptors.ValueOf(settings, key[(ConfidenceSettings.SectionName.Length + 1)..]) : null;

    /// <summary>Sets a numeric value; false when the key is unknown, not numeric, or the text does not fit the property (for example a fraction for a whole number).</summary>
    public static bool TrySet(ConfidenceSettings settings, string key, string text)
    {
        if (!key.StartsWith(ConfidenceSettings.SectionName + ":", StringComparison.Ordinal)
            || !decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            || ConfidenceSettingDescriptors.PropertyAt(settings, key[(ConfidenceSettings.SectionName.Length + 1)..], out var holder) is not { CanWrite: true } property)
        {
            return false;
        }

        var type = property.PropertyType;
        if (type == typeof(double))
        {
            property.SetValue(holder, (double)number);
            return true;
        }

        if (type == typeof(decimal))
        {
            property.SetValue(holder, number);
            return true;
        }

        if (type == typeof(int) && number == decimal.Truncate(number) && number is >= int.MinValue and <= int.MaxValue)
        {
            property.SetValue(holder, (int)number);
            return true;
        }

        return false;
    }

    /// <summary>The failure messages of every validator over the whole settings object; empty when valid.</summary>
    public static IReadOnlyList<string> Failures(IEnumerable<IValidateOptions<ConfidenceSettings>> validators, ConfidenceSettings settings)
    {
        var failures = new List<string>();
        foreach (var validator in validators)
        {
            var result = validator.Validate(Options.DefaultName, settings);
            if (result.Failed)
            {
                failures.AddRange(result.Failures ?? [result.FailureMessage]);
            }
        }

        return failures;
    }
}
