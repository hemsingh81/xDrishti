using System.Globalization;
using Microsoft.Extensions.Options;
using XDrishti.Api.Confidence;
using XDrishti.Application.Abstractions.LearnedSettings;
using XDrishti.Application.LearnedSettings;

namespace XDrishti.Api.Settings;

/// <summary>
/// Lays the applied learned values over the Confidence options while the host builds them (AU-10, ADR 0247). A learned change must never make the host refuse to start, so every value is tried on its own: it is used only when its key is on the allow-list, an automatic one is not
/// looser than the configured value, and the options still pass every validator with it; otherwise it is left out and a warning says so. When the log cannot be read, the configured values stand. Nothing here writes anything.
/// </summary>
internal sealed partial class LearnedOverridesPostConfigure(ILearnedOverrideSource source, IEnumerable<IValidateOptions<ConfidenceSettings>> validators, ILogger<LearnedOverridesPostConfigure> logger) : IPostConfigureOptions<ConfidenceSettings>
{
    public void PostConfigure(string? name, ConfidenceSettings options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var read = source.ReadApplied();
        if (read is null)
        {
            Unreadable(logger); // the configured values stand; a tightening learned earlier is NOT in force until the log can be read
            return;
        }

        if (read is not { Count: > 0 } applied)
        {
            return;
        }

        foreach (var change in applied)
        {
            var descriptor = ConfidenceSettingDescriptors.All.FirstOrDefault(d => d.Key == change.Key);
            var before = ConfidenceOverlay.ValueOf(options, change.Key);
            if (descriptor is null || !LearnedAllowList.Keys.Contains(change.Key) || before is null)
            {
                Left(logger, change.Key, "not_on_allow_list");
                continue;
            }

            if (change.DecidedBy == LearnedSettingProposer.AutoActor
                && (!decimal.TryParse(before, NumberStyles.Float, CultureInfo.InvariantCulture, out var from)
                    || !decimal.TryParse(change.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var to)
                    || descriptor.IsLoosening(from, to)))
            {
                Left(logger, change.Key, "automatic_value_is_not_tighter");
                continue;
            }

            if (!ConfidenceOverlay.TrySet(options, change.Key, change.Value))
            {
                Left(logger, change.Key, "not_settable");
                continue;
            }

            if (ConfidenceOverlay.Failures(validators, options).Count > 0)
            {
                ConfidenceOverlay.TrySet(options, change.Key, before);
                Left(logger, change.Key, "configuration_would_be_invalid");
            }
        }
    }

    [LoggerMessage(EventId = 5565, Level = LogLevel.Warning, Message = "learned.log_unreadable: the learned-change log could not be read; the configured values stand, so earlier learned tightenings are not in force")]
    private static partial void Unreadable(ILogger logger);

    [LoggerMessage(EventId = 5564, Level = LogLevel.Warning, Message = "learned.left_out {Key}: {Reason}")]
    private static partial void Left(ILogger logger, string key, string reason);
}
