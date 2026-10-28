using XDrishti.Application.Abstractions.Portfolio;

namespace XDrishti.Application.Abstractions.LiveTrading;

/// <summary>One answer of a book after mapping: the usable rows, how many rows the answer had, how many were dropped (with the reasons of <see cref="BrokerDropReasons"/>) and how many were skipped per <see cref="BrokerSkipReasons"/>. Counts only: nothing of a row's content.</summary>
public sealed record LiveBookRead<T>(IReadOnlyList<T> Rows, int Seen, int Dropped, IReadOnlyDictionary<string, int> Skipped, BrokerReadDrops Drops)
{
}
