using XDrishti.Domain.Learning.Extensions;

namespace XDrishti.Application.Abstractions.Learning;

/// <summary>The PEAD events of the stored runs of P15-09 with their bucket; an empty list says there are none (or the strategy is not built): the honest answer is then NOT_EVALUABLE.</summary>
/// <param name="Events">The events of the four variants.</param>
/// <param name="SourceNote">Where they came from, or why there are none.</param>
public sealed record PeadEventSet(IReadOnlyList<PeadEvent> Events, string SourceNote);
