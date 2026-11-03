using XDrishti.Domain.Learning.Extensions;

namespace XDrishti.Application.Abstractions.Learning;

/// <summary>The PEAD events of the stored runs of P15-09 with their bucket; an empty list says there are none (or the strategy is not built): the honest answer is then NOT_EVALUABLE.</summary>
/// <param name="Events">The events of the four variants.</param>
/// <param name="SourceNote">Where they came from, or why there are none.</param>
/// <param name="Engine">The simulation engine version the set was read for, or null when the source has none; stored in the record's sources so a phase check can tell a record of an older engine.</param>
public sealed record PeadEventSet(IReadOnlyList<PeadEvent> Events, string SourceNote, string? Engine = null);
