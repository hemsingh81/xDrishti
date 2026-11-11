using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Planning;

namespace XDrishti.Application.Planning;

/// <summary>One loosened limit a plan shows (BK-10b): computed on read from the stored change rows, never stored in the plan.</summary>
/// <param name="NoticeId"><c>sizing:{id}</c>, <c>budget:{id}</c> or <c>basket:{id}</c>.</param>
/// <param name="Source">sizing, budget or basket.</param>
/// <param name="Key">The setting key, or for a budget the basket's name.</param>
/// <param name="BasketId">The basket of a budget; null otherwise.</param>
/// <param name="OldValue">The value before.</param>
/// <param name="NewValue">The value after.</param>
/// <param name="Actor">Who changed it.</param>
/// <param name="ChangedAt">When.</param>
/// <param name="Sentence">The line of the banner.</param>
public sealed record LooseningView(string NoticeId, string Source, string Key, Guid? BasketId, string OldValue, string NewValue, string Actor, DateTimeOffset ChangedAt, string Sentence)
{
    /// <summary>The view of a notice.</summary>
    public static LooseningView Of(LooseningNotice n)
    {
        ArgumentNullException.ThrowIfNull(n);
        return new LooseningView(n.NoticeId, n.Source, n.Key, n.BasketId, n.OldValue, n.NewValue, n.Actor, n.ChangedAt, n.Sentence);
    }
}

/// <summary>Hides one loosening for good. The actor is the owner of the session.</summary>
public sealed record DismissLooseningCommand(string NoticeId, string Actor) : ICommand;

/// <summary>The automatic dismissal setting and its history (newest first, the latest 50).</summary>
/// <param name="AutoDismiss">The value; OFF while no row exists.</param>
/// <param name="Version">The newest row id; 0 when none.</param>
/// <param name="Hash">The hash of the newest row.</param>
/// <param name="Notice">What the setting does, advisory.</param>
/// <param name="History">The rows.</param>
public sealed record LooseningSettingsView(bool AutoDismiss, long Version, string? Hash, string Notice, IReadOnlyList<LooseningSettingChangeView> History);

/// <summary>One row of the setting history.</summary>
public sealed record LooseningSettingChangeView(long Id, bool AutoDismiss, string Actor, DateTimeOffset ChangedAt, string Hash);

/// <summary>Reads the automatic dismissal setting.</summary>
public sealed record GetLooseningSettingsQuery : IQuery<LooseningSettingsView>;

/// <summary>The owner's change of the automatic dismissal setting; the fresh password was checked at the endpoint.</summary>
public sealed record ChangeLooseningSettingsCommand(string Actor, bool AutoDismiss, long ExpectedVersion) : ICommand;
