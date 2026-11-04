using XDrishti.Domain.Options.Remark;

namespace XDrishti.Domain.Options.Structures;

/// <summary>
/// The words of the defined-risk structures (spec P14-10): the legs' positions, the side word of a row, the three fates beyond the option re-mark's twelve (<see cref="NotRange"/>, <see cref="StructureInvalid"/>, <see cref="LotUnknown"/>), the exit words of a structure stop
/// and the label of a structure with no cash twin. The list of unpaired reasons is closed; each is counted and none carries a premium or an R.
/// </summary>
public static class StructureNames
{
    public const string LongPosition = "long";
    public const string ShortPosition = "short";

    /// <summary>The side word of a condor row (calls and puts both).</summary>
    public const string CondorSide = "IC";

    /// <summary>A credit spread on a session whose previous session was not labelled <c>range</c>.</summary>
    public const string NotRange = "not_range";

    /// <summary>The entry value is not between 0 and the width: no defined risk to size on (a debit at or above the width, a credit at or below zero).</summary>
    public const string StructureInvalid = "structure_invalid";

    /// <summary>A condor is sized by one lot and the lot in force is unknown.</summary>
    public const string LotUnknown = "lot_unknown";

    /// <summary>The raw value of the structure at its exit bar or its stop-trigger bar is outside 0 and its width: the two legs' vendor bars disagree. Counted; never clamped into a price.</summary>
    public const string StructureInconsistent = "structure_inconsistent";

    public const string StructureStop = "structure_stop";
    public const string CondorStop = "condor_stop";
    public const string NoCashTwin = "no_cash_twin";
    public const string SpreadNotMeasuredLabel = "spread: not measured (credit structures need a measured model)";

    /// <summary>The reasons a structure row can be unpaired, in a fixed order: the option re-mark's twelve then the three above.</summary>
    public static IReadOnlyList<string> Unpaired { get; } = [.. RemarkFates.Unpaired, NotRange, StructureInvalid, LotUnknown, StructureInconsistent];
}
