namespace XDrishti.Simulation.Walking;

/// <summary>
/// The price grid of an instrument and the one conversion from a <see cref="double"/> price to the <see cref="decimal"/> the walker computes with (ADR 0038, T3 and T5). Plain <c>double</c> arithmetic gets the grid wrong
/// (<c>floor(100.00 × 1.001 / 0.05) × 0.05</c> is 100.05, not 100.10), so every price, level and comparison of the walker is a decimal.
/// </summary>
internal static class TickGrid
{
    /// <summary>The largest price magnitude the walker converts; far beyond any share price and far inside the range of <see cref="decimal"/>.</summary>
    private const double MaxPrice = 1e18;

    private const int Decimals = 8;

    /// <summary>The nearest grid price at or below <paramref name="price"/>.</summary>
    public static decimal Down(decimal price, decimal tick) => Math.Floor(price / tick) * tick;

    /// <summary>The nearest grid price at or above <paramref name="price"/>.</summary>
    public static decimal Up(decimal price, decimal tick) => Math.Ceiling(price / tick) * tick;

    /// <summary>A <see cref="double"/> price as a decimal rounded to 8 places; false for a value that is not a finite number of a sane size.</summary>
    public static bool TryConvert(double price, out decimal value)
    {
        if (!double.IsFinite(price) || Math.Abs(price) > MaxPrice)
        {
            value = 0m;
            return false;
        }

        value = decimal.Round((decimal)price, Decimals);
        return true;
    }
}
