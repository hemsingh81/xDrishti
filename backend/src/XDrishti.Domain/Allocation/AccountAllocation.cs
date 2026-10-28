using XDrishti.Domain.Accounts;

namespace XDrishti.Domain.Allocation;

/// <summary>What one account got and why. Every supplied account appears once, with numbers when it got nothing.</summary>
/// <param name="AccountId">The account.</param>
/// <param name="Mode">The mode it was supplied with (carried on every row).</param>
/// <param name="Quantity">Shares or units allocated; 0 when none.</param>
/// <param name="Code">An <see cref="AllocationCodes"/> value.</param>
/// <param name="Binding">The limit that capped the account (a risk, capital, funds or exposure code), or null when the code is a gate or unavailable.</param>
/// <param name="Capacity">Most shares the account's own limits allow, rounded down to a lot; null when it was not computed.</param>
/// <param name="RiskPct">Risk of the allocated quantity, percent of equity; 0 when none.</param>
/// <param name="Message">One line with the numbers.</param>
/// <param name="RiskScale">The ramp scale the sizing used (live pool only).</param>
/// <param name="RampEventId">The ramp event the scale comes from (live pool only).</param>
public sealed record AccountAllocation(Guid AccountId, TradingMode Mode, int Quantity, string Code, string? Binding, int? Capacity, double RiskPct, string Message, double? RiskScale = null, long? RampEventId = null)
{
    /// <summary>Whether a quantity was allocated.</summary>
    public bool IsAllocated => Quantity > 0;
}
