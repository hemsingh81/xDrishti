using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning.CellsRefresh;

/// <summary>The expected failures of the scheduled refresh (spec LC-01). The service puts the code first in a failed run message ("code: text"), so the gate reason and the bell can name it; every text carries the drift note.</summary>
public static class CellsRefreshErrors
{
    public const string NoSnapshotCode = "cells_refresh.no_snapshot";
    public const string NoHoldOutCode = "cells_refresh.no_holdout";
    public const string TemplateUnreadableCode = "cells_refresh.template_unreadable";
    public const string NotRebuildableCode = "cells_refresh.not_rebuildable";
    public const string TooManySourcesCode = "cells_refresh.too_many_sources";
    public const string SourceFailedCode = "cells_refresh.source_failed";

    public static Error NoSnapshot(DateOnly? end) => Error.Conflict(NoSnapshotCode, Say("No cell statistics snapshot is stored; the first one is the owner's xd learn cells.", end));

    public static Error TooManySources(int count, DateOnly? end) => Error.Conflict(TooManySourcesCode, Say($"The newest snapshot names {count} runs; a snapshot reads at most 50.", end));

    public static Error NoHoldOut(DateOnly? end) => Error.Conflict(NoHoldOutCode, Say("No hold-out block is fixed, so no safe refresh end can be determined.", end));

    public static Error TemplateUnreadable(DateOnly? end) => Error.Conflict(TemplateUnreadableCode, Say("The sources of the newest snapshot cannot be read.", end));

    public static Error NotRebuildable(string runKey, string reason, DateOnly? end) => Error.Conflict(NotRebuildableCode, Say($"Run {Short(runKey)} cannot be simulated again: {reason}", end));

    public static Error SourceFailed(string runKey, string code, DateOnly? end) => Error.Conflict(SourceFailedCode, Say($"Run {Short(runKey)} failed to simulate or store ({code}).", end));

    /// <summary>Another code of a use case this one calls, with the drift note.</summary>
    public static Error Wrap(Error inner, DateOnly? end) => inner with { Message = Say(inner.Message, end) };

    private static string Short(string key) => key.Length <= 12 ? key : key[..12];

    private static string Say(string text, DateOnly? end) => text + " " + CellsRefreshRules.DriftNote(end) + ".";
}
