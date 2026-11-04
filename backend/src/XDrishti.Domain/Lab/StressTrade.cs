namespace XDrishti.Domain.Lab;

/// <summary>One stored out-of-sample trade as the cost stress reads it (CF-05): R from the exact prices and net R after slippage and the sheet's charges.</summary>
/// <param name="RExact">R from the exact prices (no slippage, no charges).</param>
/// <param name="NetR">R after the modelled slippage and the charges of the sheet.</param>
public readonly record struct StressTrade(double RExact, double NetR);
