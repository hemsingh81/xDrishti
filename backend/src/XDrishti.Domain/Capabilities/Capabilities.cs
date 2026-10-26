namespace XDrishti.Domain.Capabilities;

/// <summary>
/// The capabilities the owner can switch on (spec P8-18 Part A, ADR 0128): one name per screen write or outward act of the P8 slices, each OFF until the owner switches it on with a fresh password and the newest row of the log says so. The names are a code whitelist, not a
/// table or a check constraint, so adding one is a code change and not a migration. A capability only ALLOWS the owner to do something; no setting, rule or job does it for her, and none lets an order be placed. Pure.
/// </summary>
public static class Capabilities
{
    /// <summary>Starting a replay job from the Learning area (P8-18 Part B).</summary>
    public const string ExperimentsUi = "experiments_ui";

    /// <summary>The lesson, hypothesis and revert moves of the Learning area (P8-19).</summary>
    public const string LearningMovesUi = "learning_moves_ui";

    /// <summary>The form of the autonomy switches (P8-19).</summary>
    public const string AutonomyForm = "autonomy_form";

    /// <summary>Saving a table as a file on this device (P8-20).</summary>
    public const string Exports = "exports";

    /// <summary>The installable shell with an offline cache (P8-21).</summary>
    public const string Pwa = "pwa";

    /// <summary>The longest name.</summary>
    public const int MaxNameLength = 40;

    /// <summary>The longest reason (the column is 500).</summary>
    public const int MaxReasonLength = 500;

    /// <summary>Every capability, in the order the screen shows them.</summary>
    public static IReadOnlyList<CapabilityInfo> All { get; } =
    [
        new(ExperimentsUi, "Start a replay from the Learning area", "Allows you to start one replay job that judges a changed exit or a dearer slippage against a stored run. Every start asks for your password. The result is a proposal: nothing is applied to a strategy or setting."),
        new(LearningMovesUi, "Move lessons and hypotheses, revert a change", "Allows you to link, close or dismiss a lesson, move a hypothesis and revert a learning change from the screens. Every move asks for your password."),
        new(AutonomyForm, "Change the autonomy switches in the app", "Allows you to change the autonomy settings from the screen. Every change asks for your password. No setting places an order."),
        new(Exports, "Export a table as a file", "Allows you to save a research table of the Reports screens as a CSV file on this device, outside the app's protection. The file holds research tables only, with their evidence, and nothing about accounts. Each export is recorded; it asks no password."),
        new(Pwa, "Installable shell with an offline cache", "Allows the app to register a service worker and keep a cache of its own pages on this device."),
    ];

    /// <summary>True for a name of the whitelist (exact spelling).</summary>
    public static bool IsKnown(string? name) => Find(name) is not null;

    /// <summary>The capability of a name, or null.</summary>
    public static CapabilityInfo? Find(string? name)
    {
        if (name is null)
        {
            return null;
        }

        foreach (var info in All)
        {
            if (string.Equals(info.Name, name, StringComparison.Ordinal))
            {
                return info;
            }
        }

        return null;
    }
}
