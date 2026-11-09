using System.Globalization;
using XDrishti.Application.Abstractions.LearnedSettings;
using XDrishti.Application.SettingsCatalog;

namespace XDrishti.Application.LearnedSettings;

/// <summary>
/// The one place that says when the owner's fresh password is needed (AU-10, ADR 0242). A move needs it unless the descriptor declares a safer direction and the move goes that way; an unknown key, an unreadable value or an undeclared direction all need it.
/// The handlers refuse without it. The database adds a second line of defence that does not know numbers: it refuses an automatic decision on any row that is not labelled a tightening made by learning, and the label is set by the application from the descriptor, so the numeric comparison itself lives here and is covered by tests.
/// </summary>
internal static class LearnedRules
{
    public static bool Loosens(SettingDescriptor? descriptor, string from, string to) =>
        descriptor is null
        || !decimal.TryParse(from, NumberStyles.Float, CultureInfo.InvariantCulture, out var a)
        || !decimal.TryParse(to, NumberStyles.Float, CultureInfo.InvariantCulture, out var b)
        || descriptor.IsLoosening(a, b);

    /// <summary>Approving a stored proposal: the stored direction or the descriptor's, whichever is the less safe.</summary>
    public static bool ApproveNeedsPassword(LearnedChangeRow row, SettingDescriptor? descriptor) =>
        row.Direction != "tighten" || Loosens(descriptor, row.OldValue, row.NewValue);

    /// <summary>Undoing puts the old value back: that loosens whenever the change tightened.</summary>
    public static bool UndoNeedsPassword(LearnedChangeRow row, SettingDescriptor? descriptor) =>
        Loosens(descriptor, row.NewValue, row.OldValue);
}
