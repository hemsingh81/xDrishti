using XDrishti.Application.Abstractions.Market.Context;

namespace XDrishti.Application.Market.Context;

/// <summary>The named prerequisites of the context job and the fix for each (criterion 8): an index that is not tracked, has no bars or is not in the master.</summary>
internal static class ContextPrerequisites
{
    public const string IndexNotTracked = "index_not_tracked";

    public const string VixNotTracked = "vix_not_tracked";

    public const string Disabled = "disabled";

    public const string IndexFix = "Track NIFTY 50 on the Instruments screen and run Update data.";

    public const string VixFix = "Track INDIA VIX on the Instruments screen and run Update data.";

    /// <summary>The first missing prerequisite, or null when both indices are usable.</summary>
    public static (string Code, string Fix)? Missing(ContextLegs legs) =>
        legs.Index is null ? (IndexNotTracked, IndexFix) : legs.Vix is null ? (VixNotTracked, VixFix) : null;
}
