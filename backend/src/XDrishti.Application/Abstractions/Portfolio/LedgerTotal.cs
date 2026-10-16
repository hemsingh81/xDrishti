using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Abstractions.Portfolio;

/// <summary>The ledger entries of one class in a period: how many and their signed total (rupees).</summary>
public sealed record LedgerTotal(LedgerClass Class, int Count, decimal Amount);
