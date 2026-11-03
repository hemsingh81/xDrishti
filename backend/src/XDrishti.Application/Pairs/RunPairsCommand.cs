using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.PairResearch;

namespace XDrishti.Application.Pairs;

/// <summary><c>xd pairs run --from --to [--variant e,L]</c> (P14-13): forms the pairs of each calendar quarter that touches the dates and walks them. <see cref="Variants"/> null is the whole grid of six.</summary>
public sealed record RunPairsCommand(DateOnly From, DateOnly To, IReadOnlyList<PairVariant>? Variants) : ICommand;
