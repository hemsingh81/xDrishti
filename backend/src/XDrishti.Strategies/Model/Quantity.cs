namespace XDrishti.Strategies.Model;

/// <summary>
/// A number in a field of the strategy that may name a parameter instead of giving the number (doc 11: <c>lookback: swing_lookback</c>). Exactly one of <see cref="Number"/> and <see cref="Param"/> is set; anything else is
/// the problem <c>bad_quantity</c>. A named parameter is resolved to its number before the ranges are checked.
/// </summary>
public readonly record struct Quantity(double? Number, string? Param)
{
    /// <summary>A literal number.</summary>
    public static Quantity Of(double number) => new(number, null);

    /// <summary>The value of the declared parameter of this name.</summary>
    public static Quantity Named(string param) => new(null, param);
}
