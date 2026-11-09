using System.Globalization;
using Microsoft.Extensions.Options;
using XDrishti.Api.Confidence;

namespace XDrishti.Api.Settings;

/// <summary>
/// Checks every Confidence value that has descriptor bounds against them at start-up (AU-08). The bounds are the outer bounds the existing validators already enforce, so this stops the start-up in the same cases as before
/// and never in a new one; it makes the descriptor the checked record rather than a comment.
/// </summary>
internal sealed class ConfidenceRangeValidator : IValidateOptions<ConfidenceSettings>
{
    public ValidateOptionsResult Validate(string? name, ConfidenceSettings options)
    {
        var failures = new List<string>();
        foreach (var d in ConfidenceSettingDescriptors.All.Where(x => x.HasRange))
        {
            var path = d.Key[(ConfidenceSettings.SectionName.Length + 1)..];
            var text = ConfidenceSettingDescriptors.ValueOf(options, path);
            if (text is not null && decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && !d.InRange(value))
            {
                failures.Add($"{d.Key} = {text} is outside {d.Min}..{d.Max}.");
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
