using XDrishti.Domain.Research;

namespace XDrishti.Simulation.Walking;

/// <summary>What the rows X2 to X5 decided about one minute that holds a trade: the position leaves (at <paramref name="Exit"/>, for <paramref name="Reason"/>) or stays, with the extremes that count for the excursions after this minute.</summary>
internal readonly record struct MinuteVerdict(bool Exits, ExitReason Reason, decimal Exit, decimal Low, decimal High, ExitFlags Flags);
