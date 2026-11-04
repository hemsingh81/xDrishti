using XDrishti.Domain.Options.Remark;

namespace XDrishti.Domain.Options.Structures;

/// <summary>
/// The re-mark of one structure (spec P14-10): the trade row (its entry premium is the structure's net debit or credit and its exit premium the structure's value at the exit, both per unit, in paise), the legs of a paired structure (none for an unpaired one),
/// and the maximum loss and maximum gain per unit in paise, known at entry. An unpaired row has no legs and no maxima.
/// </summary>
public sealed record StructureRow(RemarkRow Row, IReadOnlyList<StructureLegRow> Legs, int? MaxLossPx, int? MaxGainPx);
